using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Features;
using Nihasdesk.Modules.Identity.Features.ForgotPassword;
using Nihasdesk.Modules.Identity.Features.Login;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.Modules.Identity.Features.ResendVerification;
using Nihasdesk.Modules.Identity.Features.ResetPassword;
using Nihasdesk.Modules.Identity.Features.Signup;
using Nihasdesk.Modules.Identity.Features.VerifyEmail;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
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
        services.AddModuleDbContext<IdentityModuleDbContext>(configuration, IdentityModuleDbContext.Schema);
        services.AddOptions<DashboardOptions>().Bind(configuration.GetSection(DashboardOptions.SectionName));

        // One key ring for all API instances, so sessions and tokens survive restarts and scale-out.
        services.AddDataProtection()
            .SetApplicationName("Nihasdesk")
            .PersistKeysToDbContext<IdentityModuleDbContext>();

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
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordValidator>();
        services.AddScoped<IValidator<SignupRequest>, SignupValidator>();
        services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailValidator>();
        services.AddScoped<IValidator<ResendVerificationRequest>, ResendVerificationValidator>();
        services.AddScoped<IValidator<LoginRequest>, LoginValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AuthEndpoints.Map(endpoints);
}
