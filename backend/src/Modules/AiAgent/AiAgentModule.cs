using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Contracts;
using Nihasdesk.Modules.AiAgent.Features.Assistant;
using Nihasdesk.Modules.AiAgent.Features.Knowledge;
using Nihasdesk.Modules.AiAgent.Features.Policies;
using Nihasdesk.Modules.AiAgent.Features.Settings;
using Nihasdesk.Modules.AiAgent.Features.Sources;
using Nihasdesk.Modules.AiAgent.Features.Trace;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Ingestion;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent;

public sealed class AiAgentModule : IModule
{
    public string Name => AiDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AiDbContext>(configuration, AiDbContext.Schema);
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));

        AddSummaries(services);
        services.AddScoped<AgentTools>();
        services.AddScoped<AgentBrain>();
        services.AddScoped<AiSettingsStore>();
        services.AddScoped<TapHandler>();
        services.AddScoped<ReplyAgent>();
        services.AddScoped<SuggestionAgent>();
        services.AddScoped<IValidator<TeachRequest>, TeachValidator>();
        services.AddScoped<IValidator<AnswerRequest>, AnswerValidator>();
        services.AddScoped<IValidator<AiSettingsRequest>, AiSettingsValidator>();
        services.AddScoped<IValidator<TestChatRequest>, TestChatValidator>();
        services.AddScoped<IValidator<PoliciesRequest>, PoliciesValidator>();
        services.AddScoped<IValidator<AssistRequest>, AssistValidator>();
        services.AddScoped<IValidator<FeedbackRequest>, FeedbackValidator>();
        services.AddScoped<IValidator<WebPageRequest>, WebPageValidator>();
        services.AddScoped<IValidator<SiteRequest>, SiteValidator>();
        services.AddScoped<IValidator<SitePagesRequest>, SitePagesValidator>();
        services.AddScoped<IValidator<SectionTextRequest>, SectionTextValidator>();
        services.AddScoped<IValidator<SearchTestRequest>, SearchTestValidator>();
        AddSourceReading(services, configuration);

        services.AddSingleton<TraceRetention>();
        services.AddSingleton<AiReplyProcessor>();
        if (configuration.GetValue($"{AiOptions.SectionName}:{nameof(AiOptions.BackgroundProcessing)}", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<AiReplyProcessor>());
            services.AddHostedService(sp => sp.GetRequiredService<TraceRetention>());
        }
    }

    /// <summary>Workers: reads knowledge sources and writes summaries (ADR 0026 §2, §4). Tests run the jobs step by step.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AiDbContext>(configuration, AiDbContext.Schema);
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));
        AddSourceReading(services, configuration);
        AddSummaries(services);
        if (configuration.GetValue($"{AiOptions.SectionName}:{nameof(AiOptions.BackgroundProcessing)}", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<SourceReadProcessor>());
            services.AddHostedService(sp => sp.GetRequiredService<SiteFindProcessor>());
            services.AddHostedService(sp => sp.GetRequiredService<ConversationSummaryProcessor>());
        }
    }

    private static void AddSummaries(IServiceCollection services)
    {
        services.AddHttpClient<ILlmProvider, OpenAiProvider>(OpenAiProvider.HttpClientName, (sp, client) =>
            {
                var baseUrl = sp.GetRequiredService<IOptions<AiOptions>>().Value.OpenAi.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            // Requests carry the API key header and customer text; never logged (SEC-13, SEC-19).
            .RemoveAllLoggers();
        services.AddScoped<IAiCustomerData, AiCustomerData>();
        services.AddScoped<ConversationSummarizer>();
        services.AddSingleton<ConversationSummaryProcessor>();
    }

    private static void AddSourceReading(IServiceCollection services, IConfiguration configuration)
    {
        services.AddFileStore(configuration);
        services.AddSingleton<SafeHttpClient>();
        services.AddHttpClient<IEmbeddingProvider, OpenAiEmbeddingProvider>(OpenAiEmbeddingProvider.HttpClientName, (sp, client) =>
            {
                var baseUrl = sp.GetRequiredService<IOptions<AiOptions>>().Value.OpenAi.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            // Requests carry the API key header and the shop's text; never logged (SEC-13, SEC-19).
            .RemoveAllLoggers();
        services.AddScoped<SectionIndex>();
        services.AddScoped<KnowledgeSearch>();
        services.AddScoped<SourceReader>();
        services.AddSingleton<SourceReadProcessor>();
        services.AddScoped<SiteFinder>();
        services.AddSingleton<SiteFindProcessor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        KnowledgeEndpoints.Map(endpoints);
        AiSettingsEndpoints.Map(endpoints);
        PolicyEndpoints.Map(endpoints);
        AssistantEndpoints.Map(endpoints);
        TraceEndpoints.Map(endpoints);
        SourceEndpoints.Map(endpoints);
        SiteEndpoints.Map(endpoints);
        ReviewEndpoints.Map(endpoints);
    }
}
