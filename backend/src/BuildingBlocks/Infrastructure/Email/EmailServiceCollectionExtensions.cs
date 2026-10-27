using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nihasdesk.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>SMTP sender configured from <c>Email:Smtp</c>; credentials come from the secret store (SEC-13).</summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.SectionName));
        services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
