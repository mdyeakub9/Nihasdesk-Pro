using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Features.Knowledge;
using Nihasdesk.Modules.AiAgent.Features.Settings;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent;

public sealed class AiAgentModule : IModule
{
    public string Name => AiDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AiDbContext>(configuration, AiDbContext.Schema);
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));

        services.AddHttpClient<ILlmProvider, OpenAiProvider>(OpenAiProvider.HttpClientName, (sp, client) =>
            {
                var baseUrl = sp.GetRequiredService<IOptions<AiOptions>>().Value.OpenAi.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            // Requests carry the API key header and customer text; never logged (SEC-13, SEC-19).
            .RemoveAllLoggers();

        services.AddScoped<AgentTools>();
        services.AddScoped<AgentBrain>();
        services.AddScoped<AiSettingsStore>();
        services.AddScoped<ReplyAgent>();
        services.AddScoped<IValidator<TeachRequest>, TeachValidator>();
        services.AddScoped<IValidator<AnswerRequest>, AnswerValidator>();
        services.AddScoped<IValidator<AiSettingsRequest>, AiSettingsValidator>();
        services.AddScoped<IValidator<TestChatRequest>, TestChatValidator>();

        services.AddSingleton<AiReplyProcessor>();
        if (configuration.GetValue($"{AiOptions.SectionName}:{nameof(AiOptions.BackgroundProcessing)}", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<AiReplyProcessor>());
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        KnowledgeEndpoints.Map(endpoints);
        AiSettingsEndpoints.Map(endpoints);
    }
}
