using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Commerce.Features.Orders;

/// <summary>
/// Orders and order requests (PRD 4.9, ADR 0025). Everything goes through the tenant-scoped context
/// (SEC-06). Confirming an order — a team order, or approving a request — takes the stock in the
/// same transaction, with a conditional update per line so two approvals can't oversell (SEC-41).
/// </summary>
internal sealed class OrdersHandler(
    CommerceDbContext db,
    OrderPricer pricer,
    IOrderConversations conversations,
    IOrderNotifications notifications,
    ITeamDirectory team,
    IAuditTrail audit,
    ITenantContext tenant,
    TimeProvider time)
{
    /// <summary><paramref name="conversationId"/>: one chat's orders, for the inbox's customer panel.</summary>
    public async Task<OrderPageResponse> ListAsync(OrderStatus? status, Guid? conversationId, string? cursor, int? limit, CancellationToken ct)
    {
        var orders = db.Orders.AsNoTracking();
        if (conversationId is { } conversation)
        {
            orders = orders.Where(o => o.ConversationId == conversation);
        }

        if (status is { } wanted)
        {
            orders = orders.Where(o => o.Status == wanted);
        }

        if (TryDecode(cursor, out var after))
        {
            orders = orders.Where(o => EF.Functions.LessThan(ValueTuple.Create(o.CreatedAt, o.Id), ValueTuple.Create(after.At, after.Id)));
        }

        var size = limit ?? OrderValues.DefaultPageSize;
        var page = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(size + 1)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.Source,
                o.CustomerName,
                o.Total,
                o.Currency,
                ItemCount = o.Items.Sum(i => i.Quantity),
                o.CreatedAt,
            })
            .ToListAsync(ct);

        var items = page.Take(size).Select(o => new OrderSummaryResponse(
            o.Id, o.Number, Text(o.Status), Text(o.Source), o.CustomerName, o.Total, o.Currency, o.ItemCount, o.CreatedAt)).ToList();
        var next = page.Count > size ? Encode(page[size - 1].CreatedAt, page[size - 1].Id) : null;
        return new OrderPageResponse(items, next);
    }

    public async Task<OrderCountsResponse> CountsAsync(CancellationToken ct)
    {
        var counts = await db.Orders.AsNoTracking().GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Of(OrderStatus status) => counts.SingleOrDefault(c => c.Key == status)?.Count ?? 0;
        return new OrderCountsResponse(Of(OrderStatus.Pending), Of(OrderStatus.Confirmed), Of(OrderStatus.Rejected));
    }

    /// <summary>The same prices a saved order would get; nothing is stored and no stock is taken.</summary>
    public async Task<Result<QuoteResponse>> QuoteAsync(QuoteRequest request, CancellationToken ct)
    {
        var priced = await pricer.PriceAsync(request.Items!, request.ShippingZoneId, ct);
        if (!priced.IsSuccess)
        {
            return priced.Error;
        }

        var p = priced.Value;
        return Result.Success(new QuoteResponse(
            p.Lines.Select(l => new OrderItemResponse(l.ProductId, l.VariantId, l.Title, l.Variant, l.Sku, l.UnitPrice, l.Quantity, l.UnitPrice * l.Quantity)).ToList(),
            p.ShippingZoneName,
            p.ShippingCost,
            p.Subtotal,
            p.Total,
            p.Currency));
    }

    public async Task<OrderResponse?> GetAsync(Guid id, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct) is { } order ? ToResponse(order) : null;

    /// <summary>A team member's order: confirmed at once, stock taken now.</summary>
    public async Task<Result<OrderResponse>> CreateAsync(OrderRequest request, CancellationToken ct)
    {
        Guid? contactId = null;
        if (request.ConversationId is { } conversationId)
        {
            contactId = await conversations.ContactIdAsync(conversationId, ct);
            if (contactId is null)
            {
                return OrderErrors.ConversationNotFound;
            }
        }

        var priced = await pricer.PriceAsync(request.Items!, request.ShippingZoneId, ct);
        if (!priced.IsSuccess)
        {
            return priced.Error;
        }

        var me = await MeAsync(ct);
        var now = time.GetUtcNow();
        var created = await SaveNewAsync(
            number => Order.ByTeam(tenant.TenantId, number, me.UserId, request.ConversationId, contactId, Details(request), priced.Value, now),
            takeStock: true,
            ct);
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        await audit.RecordAsync("order.created", ct);
        var order = ToResponse(created.Value);
        // Created from a chat: the customer gets the summary there (PRD 4.9).
        if (order.ConversationId is { } chat)
        {
            await notifications.ConfirmedAsync(chat, Summary(order), ct);
        }

        return Result.Success(order);
    }

    /// <summary>The AI's order request (via <see cref="IOrderRequests"/>): pending, no stock taken yet.</summary>
    public async Task<Result<Order>> CreateRequestAsync(Guid conversationId, OrderRequest request, CancellationToken ct)
    {
        var contactId = await conversations.ContactIdAsync(conversationId, ct);
        if (contactId is null)
        {
            return OrderErrors.ConversationNotFound;
        }

        // One open request per chat (SEC-42): the team decides it before the AI takes another.
        if (await db.Orders.AnyAsync(o => o.ConversationId == conversationId && o.Status == OrderStatus.Pending, ct))
        {
            return OrderErrors.PendingExists;
        }

        var priced = await pricer.PriceAsync(request.Items!, request.ShippingZoneId, ct);
        if (!priced.IsSuccess)
        {
            return priced.Error;
        }

        var created = await SaveNewAsync(
            number => Order.RequestFromAi(tenant.TenantId, number, conversationId, contactId.Value, Details(request), priced.Value),
            takeStock: false,
            ct);
        if (created.IsSuccess)
        {
            await notifications.RequestCreatedAsync(conversationId, created.Value.Id, ct);
        }

        return created;
    }

    /// <summary>"Edit then approve": a pending request gets new details and lines, priced again from the catalog.</summary>
    public async Task<Result<OrderResponse>> UpdateAsync(Guid id, OrderRequest request, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        if (order.Status != OrderStatus.Pending)
        {
            return OrderErrors.NotPending;
        }

        var priced = await pricer.PriceAsync(request.Items!, request.ShippingZoneId, ct);
        if (!priced.IsSuccess)
        {
            return priced.Error;
        }

        db.OrderItems.RemoveRange(order.Items);
        order.Edit(Details(request), priced.Value);
        db.OrderItems.AddRange(order.Items);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("order.edited", ct);
        return Result.Success(ToResponse(order));
    }

    public async Task<Result<OrderResponse>> ApproveAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var result = await DecideAsync(id, order => order.Approve(me.UserId, time.GetUtcNow()), takeStock: true, ct);
        if (result.IsSuccess)
        {
            await audit.RecordAsync("order.approved", ct);
            if (FromAiChat(result.Value) is { } chat)
            {
                await notifications.ConfirmedAsync(chat, Summary(result.Value), ct);
            }
        }

        return result;
    }

    public async Task<Result<OrderResponse>> RejectAsync(Guid id, RejectRequest request, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var result = await DecideAsync(id, order => order.Reject(me.UserId, request.Reason!, time.GetUtcNow()), takeStock: false, ct);
        if (result.IsSuccess)
        {
            await audit.RecordAsync("order.rejected", ct);
            if (FromAiChat(result.Value) is { } chat)
            {
                await notifications.RejectedAsync(chat, result.Value.Number, ct);
            }
        }

        return result;
    }

    /// <summary>The customer hears back only about requests the AI took in their chat; the team handles its own orders.</summary>
    private static Guid? FromAiChat(OrderResponse order) => order.Source == "ai" ? order.ConversationId : null;

    private static ConfirmedOrderSummary Summary(OrderResponse o) => new(
        o.Number,
        o.Items.Select(i => new ConfirmedOrderLine(i.Title, i.Variant, i.Quantity, i.LineTotal)).ToList(),
        o.ShippingZoneName,
        o.ShippingCost,
        o.Total,
        o.Currency,
        o.Street,
        o.PostalCode,
        o.City);

    /// <summary>Locks the order row, so the same request is never approved twice, then decides it.</summary>
    private async Task<Result<OrderResponse>> DecideAsync(Guid id, Action<Order> decide, bool takeStock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS value FROM commerce.orders WHERE id = {id} FOR UPDATE")
            .ToListAsync(ct);
        var order = locked.Count == 0 ? null : await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        if (order.Status != OrderStatus.Pending)
        {
            return OrderErrors.NotPending;
        }

        if (takeStock && !await TakeStockAsync(order.Items, ct))
        {
            return OrderErrors.NotAvailable;
        }

        decide(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(ToResponse(order));
    }

    /// <summary>Stores a new order with the next number (#1001, #1002…), retrying when two orders take the same one.</summary>
    private async Task<Result<Order>> SaveNewAsync(Func<int, Order> build, bool takeStock, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var number = (await db.Orders.MaxAsync(o => (int?)o.Number, ct) ?? Order.FirstNumber - 1) + 1;
            var order = build(number);
            if (takeStock && !await TakeStockAsync(order.Items, ct))
            {
                return OrderErrors.NotAvailable;
            }

            db.Orders.Add(order);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Result.Success(order);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && attempt < 3)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// One conditional UPDATE per line inside the caller's transaction: it only succeeds while there is
    /// enough stock (or stock isn't limited), so concurrent confirmations can't oversell.
    /// </summary>
    private async Task<bool> TakeStockAsync(IEnumerable<OrderItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            var quantity = item.Quantity;
            var changed = await db.Variants
                .Where(v => v.Id == item.VariantId
                    && (!v.TrackInventory || v.InventoryPolicy == InventoryPolicy.Continue || v.Stock >= quantity))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Stock, v => v.TrackInventory ? v.Stock - quantity : v.Stock), ct);
            if (changed == 0)
            {
                return false;
            }
        }

        return true;
    }

    internal static DeliveryDetails Details(OrderRequest r) =>
        new(r.CustomerName!, r.Phone!, r.Street!, r.PostalCode!, r.City!, r.Province, r.Country!, r.Notes);

    private async Task<TeamMemberInfo> MeAsync(CancellationToken ct) =>
        await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Order endpoints require a team member.");

    internal static string Text<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private static OrderResponse ToResponse(Order o) => new(
        o.Id,
        o.Number,
        Text(o.Status),
        Text(o.Source),
        o.ConversationId,
        o.CustomerName,
        o.Phone,
        o.Street,
        o.PostalCode,
        o.City,
        o.Province,
        o.Country,
        o.Notes,
        o.ShippingZoneId,
        o.ShippingZoneName,
        o.ShippingCost,
        o.Subtotal,
        o.Total,
        o.Currency,
        o.PaymentMethod,
        o.Items.OrderBy(i => i.Position).Select(i => new OrderItemResponse(i.ProductId, i.VariantId, i.Title, i.Variant, i.Sku, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
        o.CreatedAt,
        o.DecidedAt,
        o.RejectReason);

    private static string Encode(DateTimeOffset at, Guid id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{at.UtcTicks}.{id:N}")));

    internal static bool TryDecode(string? cursor, out (DateTimeOffset At, Guid Id) value)
    {
        value = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 100)
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is > 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}

/// <summary>The AI's read of a customer's earlier orders (ADR 0026 §4): number, status and date only.</summary>
internal sealed class ContactOrdersService(CommerceDbContext db) : IContactOrders
{
    public async Task<IReadOnlyList<ContactOrder>> RecentAsync(Guid contactId, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.Orders
            .AsNoTracking()
            .Where(o => o.ContactId == contactId)
            .OrderByDescending(o => o.Number)
            .Take(limit)
            .Select(o => new { o.Number, o.Status, o.CreatedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(o => new ContactOrder(o.Number, o.Status.ToString().ToLowerInvariant(), o.CreatedAt)).ToList();
    }
}

/// <summary>The AI's entry point (PRD 4.9): same validation and prices as the team's, result is a pending request.</summary>
internal sealed class OrderRequestsService(OrdersHandler orders, OrderPricer pricer, FluentValidation.IValidator<OrderRequest> validator) : IOrderRequests, IOrderDetailsCheck
{
    public async Task<Result<OrderQuote>> QuoteAsync(IReadOnlyList<OrderRequestLine> lines, Guid shippingZoneId, CancellationToken cancellationToken)
    {
        if (lines.Count is 0 or > OrderValues.MaxLines || lines.Any(l => l.Quantity is < 1 or > OrderValues.MaxQuantity))
        {
            return Error.Validation("order.invalid", $"Send 1 to {OrderValues.MaxLines} items with a quantity of 1 to {OrderValues.MaxQuantity}.");
        }

        var priced = await pricer.PriceAsync(lines.Select(l => new OrderItemRequest(l.ProductId, l.VariantId, l.Quantity)).ToList(), shippingZoneId, cancellationToken);
        if (!priced.IsSuccess)
        {
            return priced.Error;
        }

        var p = priced.Value;
        return Result.Success(new OrderQuote(
            p.Lines.Select(l => new OrderQuoteLine(l.ProductId, l.VariantId, l.Title, l.Variant, l.UnitPrice, l.Quantity)).ToList(),
            p.ShippingZoneName,
            p.Subtotal,
            p.ShippingCost,
            p.Total,
            p.Currency));
    }

    public async Task<Result> ValidateAsync(IReadOnlyList<OrderRequestLine> lines, OrderRequestDetails details, CancellationToken cancellationToken)
    {
        var invalid = await InvalidAsync(Request(null, lines, details), cancellationToken);
        return invalid is null ? Result.Success() : Result.Failure(invalid);
    }

    public async Task<Result<OrderRequestCreated>> CreateAsync(
        Guid conversationId, IReadOnlyList<OrderRequestLine> lines, OrderRequestDetails details, CancellationToken cancellationToken)
    {
        var request = Request(conversationId, lines, details);
        if (await InvalidAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var created = await orders.CreateRequestAsync(conversationId, request, cancellationToken);
        return created.IsSuccess
            ? Result.Success(new OrderRequestCreated(created.Value.Id, created.Value.Number, created.Value.Subtotal, created.Value.ShippingCost, created.Value.Total, created.Value.Currency))
            : created.Error;
    }

    private static OrderRequest Request(Guid? conversationId, IReadOnlyList<OrderRequestLine> lines, OrderRequestDetails details) =>
        new(
            conversationId, details.CustomerName, details.Phone, details.Street, details.PostalCode, details.City,
            details.Province, details.Country, details.ShippingZoneId, details.Notes,
            lines.Select(l => new OrderItemRequest(l.ProductId, l.VariantId, l.Quantity)).ToList());

    /// <summary>Field names only, so the AI knows what to ask again; never the customer's values.</summary>
    private async Task<Error?> InvalidAsync(OrderRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid
            ? null
            : Error.Validation("order.invalid", $"Ask the customer again for: {string.Join(", ", validation.Errors.Select(e => e.PropertyName).Distinct())}.");
    }
}
