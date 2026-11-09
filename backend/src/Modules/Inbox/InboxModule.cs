using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Features.Media;
using Nihasdesk.Modules.Inbox.Features.Notes;
using Nihasdesk.Modules.Inbox.Features.Products;
using Nihasdesk.Modules.Inbox.Features.SavedReplies;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;

namespace Nihasdesk.Modules.Inbox;

public sealed class InboxModule : IModule
{
    public string Name => InboxDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<InboxDbContext>(configuration, InboxDbContext.Schema);
        // Single API instance for now; a Redis backplane comes with scale-out (ADR 0016).
        services.AddSignalR();
        services.AddSingleton<InboxNotifier>();
        services.AddScoped<IInboxIngest, InboxIngest>();
        services.AddScoped<IOutboundMessages, OutboundMessages>();
        services.AddScoped<RichRenderer>();
        services.AddScoped<IAiConversations, AiConversations>();
        services.AddScoped<IAiMessageReader, AiMessageReader>();
        services.AddScoped<IOrderConversations, OrderConversations>();
        services.AddScoped<IOrderNotifications, OrderNotifications>();
        services.AddScoped<ConversationsHandler>();
        services.AddScoped<IValidator<ListConversationsQuery>, ListConversationsValidator>();
        services.AddScoped<IValidator<ListMessagesQuery>, ListMessagesValidator>();
        services.AddScoped<IValidator<SendMessageRequest>, SendMessageValidator>();
        services.AddScoped<IValidator<UpdateConversationRequest>, UpdateConversationValidator>();
        services.AddScoped<NotesHandler>();
        services.AddScoped<IValidator<AddNoteRequest>, AddNoteValidator>();
        services.AddScoped<SavedRepliesHandler>();
        services.AddScoped<IValidator<SavedReplyRequest>, SavedReplyValidator>();
        services.AddScoped<ProductMessagesHandler>();
        services.AddScoped<SendImageHandler>();
        services.AddScoped<IValidator<SendProductsRequest>, SendProductsValidator>();
        AddMedia(services, configuration);
    }

    /// <summary>Workers: the AI's summary job reads messages, and media is downloaded here. Replies and SignalR stay in the API.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<InboxDbContext>(configuration, InboxDbContext.Schema);
        services.AddScoped<IAiMessageReader, AiMessageReader>();
        AddMedia(services, configuration);
        services.AddHostedService(sp => sp.GetRequiredService<MediaDownloadProcessor>());
        services.AddHostedService(sp => sp.GetRequiredService<MediaRetention>());
    }

    /// <summary>In the API too, without running: tests run the job step by step, and the API serves the files.</summary>
    private static void AddMedia(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InboxMediaOptions>().Bind(configuration.GetSection(InboxMediaOptions.SectionName));
        services.AddFileStore(configuration);
        services.AddScoped<MediaDownloader>();
        services.AddSingleton<MediaDownloadProcessor>();
        services.AddSingleton<MediaRetention>();
        services.AddScoped<IMediaErasure, MediaErasure>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ConversationEndpoints.Map(endpoints);
        MediaEndpoints.Map(endpoints);
        SendImageEndpoints.Map(endpoints);
        NoteEndpoints.Map(endpoints);
        SavedReplyEndpoints.Map(endpoints);
        ProductMessageEndpoints.Map(endpoints);
        endpoints.MapHub<InboxHub>(InboxHub.Path);
    }
}
