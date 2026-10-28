using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Features;
using Nihasdesk.Modules.Identity.Features.Account;
using Nihasdesk.Modules.Identity.Features.ForgotPassword;
using Nihasdesk.Modules.Identity.Features.Login;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.Modules.Identity.Features.ResendVerification;
using Nihasdesk.Modules.Identity.Features.ResetPassword;
using Nihasdesk.Modules.Identity.Features.Signup;
using Nihasdesk.Modules.Identity.Features.Team;
using Nihasdesk.Modules.Identity.Features.VerifyEmail;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Mfa;
using Nihasdesk.Modules.Identity.Infrastructure.Passwords;
using Nihasdesk.Modules.Identity.Infrastructure.Sessions;

namespace Nihasdesk.Modules.Identity;

public sealed class IdentityModule : IModule
{
    /// <summary>Dashboard session cookie (ADR 0005, SEC-14). <c>__Host-</c>: Secure, host-only, path /.</summary>
    public const string SessionCookieName = "__Host-nd_session";

    public string Name => IdentityModuleDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        services.AddOptions<DashboardOptions>().Bind(configuration.GetSection(DashboardOptions.SectionName));
        services.AddEmailOutbox<IdentityModuleDbContext>();

        services.AddIdentityCore<User>(options =>
            {
                // ADR 0009: length only, no composition rules.
                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                // SEC-26: lockout/backoff after failed logins.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<IdentityModuleDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<BreachedPasswordValidator>();
        // Encrypted authenticator secret and hashed recovery codes (ADR 0015).
        services.AddScoped<IUserStore<User>, ProtectedUserStore>();

        // ADR 0009: HIBP range API, short timeout, fail-open.
        services.AddOptions<BreachedPasswordOptions>().Bind(configuration.GetSection(BreachedPasswordOptions.SectionName));
        services.AddHttpClient(BreachedPasswordValidator.HttpClientName, client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nihasdesk-PasswordCheck"));

        // SEC-26: email and reset tokens expire within an hour.
        // Configurable only so tests can prove expiry; never set above 1 hour.
        var tokenLifespan = configuration.GetValue("Identity:TokenLifespan", TimeSpan.FromHours(1));
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = tokenLifespan <= TimeSpan.FromHours(1) ? tokenLifespan : TimeSpan.FromHours(1));

        services.AddSingleton<DatabaseTicketStore>();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                // Upper bound only; the session store enforces idle and absolute lifetimes (ADR 0010).
                options.ExpireTimeSpan = SessionLifetime.RememberAbsolute;
                options.SlidingExpiration = false;
                // An API answers 401/403 instead of redirecting to a login page.
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<DatabaseTicketStore>((options, store) => options.SessionStore = store);
        services.AddAuthorization();

        services.AddHttpContextAccessor();
        services.AddSingleton<AuditLog>();
        services.AddSingleton<DashboardLinks>();
        services.AddScoped<SessionSignIn>();
        services.AddScoped<SignupHandler>();
        services.AddScoped<VerifyEmailHandler>();
        services.AddScoped<ResendVerificationHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<MeHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<CurrentMember>();
        services.AddScoped<CurrentSession>();
        services.AddSingleton<MfaChallenge>();
        services.AddScoped<AccountHandler>();
        services.AddScoped<IValidator<MfaLoginRequest>, MfaLoginValidator>();
        services.AddScoped<IValidator<MfaCodeRequest>, MfaCodeValidator>();
        services.AddScoped<IValidator<ReauthRequest>, ReauthValidator>();
        services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordValidator>();
        services.AddScoped<TeamHandler>();
        services.AddScoped<AcceptInvitationHandler>();
        services.AddScoped<IValidator<InviteRequest>, InviteValidator>();
        services.AddScoped<IValidator<ChangeRoleRequest>, ChangeRoleValidator>();
        services.AddScoped<IValidator<AcceptInvitationRequest>, AcceptInvitationValidator>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordValidator>();
        services.AddScoped<IValidator<SignupRequest>, SignupValidator>();
        services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailValidator>();
        services.AddScoped<IValidator<ResendVerificationRequest>, ResendVerificationValidator>();
        services.AddScoped<IValidator<LoginRequest>, LoginValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        TeamEndpoints.Map(endpoints);
        AccountEndpoints.Map(endpoints);
    }

    /// <summary>Workers: delivers the Identity email outbox (ADR 0013). No auth or HTTP services.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        services.AddEmailOutboxDispatcher<IdentityModuleDbContext>(configuration);
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityModuleDbContext>(configuration, IdentityModuleDbContext.Schema);

        // One key ring for the API and Workers: sessions, tokens and outbox emails survive restarts
        // and scale-out, and Workers can read what the API encrypted.
        services.AddDataProtection()
            .SetApplicationName("Nihasdesk")
            .PersistKeysToDbContext<IdentityModuleDbContext>();
    }
}
