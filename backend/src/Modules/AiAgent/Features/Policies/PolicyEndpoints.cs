using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Features.Policies;

internal sealed record PoliciesResponse(
    string? ShippingNotes,
    int? ReturnDays,
    string? ReturnPolicy,
    // Fixed in Phase 1 (ADR 0004): shown, not chosen.
    IReadOnlyList<string> PaymentMethods,
    string? PaymentNotes,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactAddress,
    string? OpeningHours);

internal sealed record PoliciesRequest(
    string? ShippingNotes,
    int? ReturnDays,
    string? ReturnPolicy,
    string? PaymentNotes,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactAddress,
    string? OpeningHours);

internal sealed class PoliciesValidator : AbstractValidator<PoliciesRequest>
{
    public PoliciesValidator()
    {
        RuleFor(r => r.ShippingNotes).MaximumLength(ShopPolicies.ShippingNotesMaxLength);
        RuleFor(r => r.ReturnDays).InclusiveBetween(0, ShopPolicies.MaxReturnDays);
        RuleFor(r => r.ReturnPolicy).MaximumLength(ShopPolicies.ReturnPolicyMaxLength);
        RuleFor(r => r.PaymentNotes).MaximumLength(ShopPolicies.PaymentNotesMaxLength);
        RuleFor(r => r.ContactPhone)
            .Matches(@"^\+?[0-9 ()./-]{5,40}$").When(r => !string.IsNullOrWhiteSpace(r.ContactPhone))
            .WithMessage("Enter a phone number, e.g. +39 333 1234567.");
        RuleFor(r => r.ContactEmail)
            .EmailAddress().MaximumLength(ShopPolicies.EmailMaxLength).When(r => !string.IsNullOrWhiteSpace(r.ContactEmail));
        RuleFor(r => r.ContactAddress).MaximumLength(ShopPolicies.AddressMaxLength);
        RuleFor(r => r.OpeningHours).MaximumLength(ShopPolicies.OpeningHoursMaxLength);
    }
}

/// <summary>
/// <c>/api/v1/ai/policies</c> (ADR 0020): every member sees the shop's policies; Owner and Admin
/// change them, because the AI tells customers what they say.
/// </summary>
internal static class PolicyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var ai = endpoints.MapGroup("/api/v1/ai").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();

        ai.MapGet("/policies", async (AiDbContext db, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(ToResponse(await db.Policies.AsNoTracking().SingleOrDefaultAsync(ct) ?? ShopPolicies.Empty(tenant.TenantId))));

        ai.MapPut("/policies", async (PoliciesRequest request, AiDbContext db, ITenantContext tenant, IAuditTrail audit, CancellationToken ct) =>
            {
                var policies = await db.Policies.SingleOrDefaultAsync(ct);
                if (policies is null)
                {
                    policies = ShopPolicies.Empty(tenant.TenantId);
                    db.Policies.Add(policies);
                }

                policies.Update(
                    request.ShippingNotes, request.ReturnDays, request.ReturnPolicy, request.PaymentNotes,
                    request.ContactPhone, request.ContactEmail, request.ContactAddress, request.OpeningHours);
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("ai.policies_updated", ct);
                return Results.Ok(ToResponse(policies));
            })
            .AddEndpointFilter(new RequireTeamRole("owner", "admin"))
            .AddEndpointFilter<ValidationFilter<PoliciesRequest>>();
    }

    private static PoliciesResponse ToResponse(ShopPolicies p) => new(
        p.ShippingNotes, p.ReturnDays, p.ReturnPolicy, ShopPolicies.PaymentMethods, p.PaymentNotes,
        p.ContactPhone, p.ContactEmail, p.ContactAddress, p.OpeningHours);
}
