using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Contacts.Domain;
using Nihasdesk.Modules.Contacts.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Contacts.Features.ContactDetails;

internal sealed record ContactResponse(
    Guid Id,
    string? Name,
    string? Phone,
    string? Email,
    string? Language,
    string? Notes,
    IReadOnlyList<string> Channels,
    DateTimeOffset CreatedAt);

/// <summary>Replaces the fields the team can edit. Channel identities and IDs are never taken from the client (SEC-22).</summary>
internal sealed record UpdateContactRequest(string? Name, string? Phone, string? Email, string? Language, string? Notes);

internal sealed class UpdateContactValidator : AbstractValidator<UpdateContactRequest>
{
    public UpdateContactValidator()
    {
        RuleFor(r => r.Name).MaximumLength(Contact.NameMaxLength);
        RuleFor(r => r.Phone).MaximumLength(Contact.PhoneMaxLength).Matches(@"^[0-9+()\-\s.]*$").When(r => r.Phone is not null);
        RuleFor(r => r.Email).MaximumLength(Contact.EmailMaxLength).EmailAddress().When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r.Language).MaximumLength(Contact.LanguageMaxLength).Matches("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})?$")
            .When(r => !string.IsNullOrWhiteSpace(r.Language));
        RuleFor(r => r.Notes).MaximumLength(Contact.NotesMaxLength);
    }
}

internal static class ContactErrors
{
    /// <summary>Also for other workspaces' IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("contact.not_found", "Contact not found.");
}

/// <summary><c>/api/v1/contacts/{id}</c>: every member may view and edit contacts (docs/standards/permissions.md).</summary>
internal static class ContactEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var contacts = endpoints.MapGroup("/api/v1/contacts")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();

        contacts.MapGet("/{id:guid}", async (Guid id, ContactsDbContext db, CancellationToken ct) =>
        {
            var contact = await LoadAsync(db, id, ct);
            return contact is null ? ContactErrors.NotFound.ToProblem() : Results.Ok(contact);
        });

        contacts.MapPut("/{id:guid}", async (Guid id, UpdateContactRequest request, ContactsDbContext db, CancellationToken ct) =>
            {
                var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == id, ct);
                if (contact is null)
                {
                    return ContactErrors.NotFound.ToProblem();
                }

                contact.Update(request.Name, request.Phone, request.Email, request.Language, request.Notes);
                await db.SaveChangesAsync(ct);
                return Results.Ok(await LoadAsync(db, id, ct));
            })
            .AddEndpointFilter<ValidationFilter<UpdateContactRequest>>();
    }

    private static async Task<ContactResponse?> LoadAsync(ContactsDbContext db, Guid id, CancellationToken ct)
    {
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return null;
        }

        var channels = await db.Identities
            .Where(i => i.ContactId == id)
            .Select(i => i.Channel)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return new ContactResponse(
            contact.Id, contact.Name, contact.Phone, contact.Email, contact.Language, contact.Notes, channels, contact.CreatedAt);
    }
}
