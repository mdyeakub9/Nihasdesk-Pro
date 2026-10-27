using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure;
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
    IEmailSender email,
    IOptions<DashboardOptions> dashboard)
{
    public async Task<Result> HandleAsync(SignupRequest request, CancellationToken cancellationToken)
    {
        var language = request.Language ?? "en";
        var address = request.Email.Trim();

        if (await users.FindByEmailAsync(address) is not null)
        {
            await email.SendAsync(AuthEmails.AccountExists(address, language, Link("/login")), cancellationToken);
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
                await email.SendAsync(AuthEmails.AccountExists(address, language, Link("/login")), cancellationToken);
                return Result.Success();
            }

            return Result.Failure(AuthErrors.WeakPassword);
        }

        db.AddRange(organization, workspace, Membership.Create(workspace.Id, user.Id, Role.Owner));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(AuthEmails.VerifyEmail(address, language, user.DisplayName, VerifyLink(user.Id, token)), cancellationToken);
        return Result.Success();
    }

    private string VerifyLink(Guid userId, string token) =>
        Link($"/verify-email?user={userId}&token={Uri.EscapeDataString(token)}");

    private string Link(string path) => dashboard.Value.DashboardUrl.TrimEnd('/') + path;
}
