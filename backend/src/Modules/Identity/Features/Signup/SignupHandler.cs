using Microsoft.AspNetCore.Identity;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Email;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Signup;

/// <summary>
/// Creates user, organization, workspace and Owner membership in one transaction, then sends the
/// verification email. An already registered email gets a notice instead, and the response is the
/// same either way, so signup never reveals who has an account (SEC-26).
/// </summary>
internal sealed class SignupHandler(
    UserManager<User> users,
    IdentityModuleDbContext db,
    TenantContext tenantContext,
    IEmailOutbox email,
    DashboardLinks links,
    AuditLog audit)
{
    public async Task<Result> HandleAsync(SignupRequest request, CancellationToken cancellationToken)
    {
        var language = request.Language ?? "en";
        var address = request.Email.Trim();

        // Password policy (incl. breached check) first, for every email: otherwise a rejected
        // password would only be reported for unregistered emails and reveal the others.
        var passwordError = await ValidatePasswordAsync(request.Password);
        if (passwordError is not null)
        {
            return Result.Failure(passwordError);
        }

        if (await users.FindByEmailAsync(address) is { } existing)
        {
            await email.EnqueueAsync(AuthEmails.AccountExists(address, language, links.Login), cancellationToken);
            await audit.WriteAsync(AuditActions.SignupExistingEmail, existing.Id, tenantId: null, cancellationToken);
            return Result.Success();
        }

        var organization = Organization.Create(request.BusinessName);
        var workspace = Workspace.Create(organization.Id, request.BusinessName);
        var user = new User { UserName = address, Email = address, DisplayName = request.Name.Trim(), Language = language };

        // The new workspace is this request's tenant; set before the first connection opens (RLS).
        tenantContext.SetTenant(workspace.Id);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                // Lost a race with a parallel signup for the same email: same answer as above.
                await email.EnqueueAsync(AuthEmails.AccountExists(address, language, links.Login), cancellationToken);
                return Result.Success();
            }

            return Result.Failure(AuthErrors.ForPassword(created.Errors));
        }

        db.AddRange(organization, workspace, Membership.Create(workspace.Id, user.Id, Role.Owner));
        await db.SaveChangesAsync(cancellationToken);

        // Queued in the same transaction: no account without its email, no email without its account.
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.EnqueueAsync(
            AuthEmails.VerifyEmail(address, language, user.DisplayName, links.VerifyEmail(user.Id, token)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await audit.WriteAsync(AuditActions.Signup, user.Id, workspace.Id, cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidatePasswordAsync(string password)
    {
        var candidate = new User();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, candidate, password);
            if (!result.Succeeded)
            {
                return AuthErrors.ForPassword(result.Errors);
            }
        }

        return null;
    }
}
