using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Shipping.Domain;
using Nihasdesk.Modules.Shipping.Infrastructure;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Shipping.Features.Zones;

internal sealed record ZoneResponse(
    Guid Id, string Name, string? Areas, long Price, string Currency, long? FreeOver, int? MinDays, int? MaxDays);

/// <summary>One zone; <see cref="Id"/> keeps an existing zone, null (or an unknown id) adds one.</summary>
internal sealed record ZoneRequest(
    Guid? Id, string? Name, string? Areas, long? Price, string? Currency, long? FreeOver, int? MinDays, int? MaxDays);

/// <summary>The whole list, in order: zones left out are removed.</summary>
internal sealed record ZonesRequest(List<ZoneRequest>? Zones);

internal sealed class ZonesValidator : AbstractValidator<ZonesRequest>
{
    public const int MaxZones = 30;

    public ZonesValidator()
    {
        RuleFor(r => r.Zones).NotNull().Must(z => z is null || z.Count <= MaxZones).WithMessage($"At most {MaxZones} zones.");
        RuleFor(r => r.Zones)
            .Must(z => z is null || z.Select(x => x.Name?.Trim().ToUpperInvariant()).Distinct().Count() == z.Count)
            .WithMessage("Each zone needs its own name.");
        RuleForEach(r => r.Zones).ChildRules(zone =>
        {
            zone.RuleFor(z => z.Name).NotEmpty().Must(n => !string.IsNullOrWhiteSpace(n)).MaximumLength(ShippingZone.NameMaxLength);
            zone.RuleFor(z => z.Areas).MaximumLength(ShippingZone.AreasMaxLength);
            zone.RuleFor(z => z.Price).NotNull().InclusiveBetween(0, ShippingZone.MaxPrice);
            zone.RuleFor(z => z.Currency).NotEmpty().Matches("^[A-Z]{3}$").WithMessage("Currency must be an ISO code like EUR.");
            zone.RuleFor(z => z.FreeOver).InclusiveBetween(0, ShippingZone.MaxPrice);
            zone.RuleFor(z => z.MinDays).InclusiveBetween(0, ShippingZone.DaysLimit);
            zone.RuleFor(z => z.MaxDays).InclusiveBetween(0, ShippingZone.DaysLimit);
            zone.RuleFor(z => z.MaxDays)
                .Must((z, max) => z.MinDays is null || max is null || max >= z.MinDays)
                .WithMessage("Max days can't be less than min days.");
        });
    }
}

/// <summary>
/// <c>/api/v1/shipping/zones</c> (ADR 0020): every member sees the zones; Owner and Admin change
/// them, because the AI quotes these costs to customers.
/// </summary>
internal static class ZoneEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var shipping = endpoints.MapGroup("/api/v1/shipping").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();

        shipping.MapGet("/zones", async (ShippingDbContext db, CancellationToken ct) =>
            Results.Ok(await ListAsync(db, ct)));

        shipping.MapPut("/zones", async (ZonesRequest request, ShippingDbContext db, ITenantContext tenant, IAuditTrail audit, CancellationToken ct) =>
            {
                // Unknown ids (also other workspaces' ids, SEC-06) are not found here and become new zones.
                var existing = await db.Zones.ToDictionaryAsync(z => z.Id, ct);
                foreach (var (z, position) in request.Zones!.Select((z, i) => (z, i)))
                {
                    if (z.Id is { } id && existing.Remove(id, out var kept))
                    {
                        kept.Update(position, z.Name!, z.Areas, z.Price!.Value, z.Currency!, z.FreeOver, z.MinDays, z.MaxDays);
                    }
                    else
                    {
                        db.Zones.Add(ShippingZone.Create(tenant.TenantId, position, z.Name!, z.Areas, z.Price!.Value, z.Currency!, z.FreeOver, z.MinDays, z.MaxDays));
                    }
                }

                db.Zones.RemoveRange(existing.Values);
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("shipping.zones_updated", ct);
                return Results.Ok(await ListAsync(db, ct));
            })
            .AddEndpointFilter(new RequireTeamRole("owner", "admin"))
            .AddEndpointFilter<ValidationFilter<ZonesRequest>>();
    }

    private static async Task<List<ZoneResponse>> ListAsync(ShippingDbContext db, CancellationToken ct) =>
        await db.Zones.AsNoTracking()
            .OrderBy(z => z.Position)
            .Select(z => new ZoneResponse(z.Id, z.Name, z.Areas, z.Price, z.Currency, z.FreeOver, z.MinDays, z.MaxDays))
            .ToListAsync(ct);
}
