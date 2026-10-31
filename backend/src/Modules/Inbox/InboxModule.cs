using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Features.Notes;
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
        services.AddScoped<IAiConversations, AiConversations>();
        services.AddScoped<ConversationsHandler>();
        services.AddScoped<IValidator<ListConversationsQuery>, ListConversationsValidator>();
        services.AddScoped<IValidator<ListMessagesQuery>, ListMessagesValidator>();
        services.AddScoped<IValidator<SendMessageRequest>, SendMessageValidator>();
        services.AddScoped<IValidator<UpdateConversationRequest>, UpdateConversationValidator>();
        services.AddScoped<NotesHandler>();
        services.AddScoped<IValidator<AddNoteRequest>, AddNoteValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ConversationEndpoints.Map(endpoints);
        NoteEndpoints.Map(endpoints);
        endpoints.MapHub<InboxHub>(InboxHub.Path);
    }
}
