namespace Nihasdesk.Modules.Identity.Features.Me;

/// <summary>The signed-in user, their workspace and role. Returned by login, email verification and <c>/me</c>.</summary>
internal sealed record MeResponse(MeUser User, MeWorkspace Workspace, string Role);

internal sealed record MeUser(Guid Id, string Name, string Email, string Language, bool EmailVerified, bool MfaEnabled);

internal sealed record MeWorkspace(Guid Id, string Name);
