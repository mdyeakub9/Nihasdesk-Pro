using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Modules.AiAgent;
using Nihasdesk.Modules.Billing;
using Nihasdesk.Modules.Channels;
using Nihasdesk.Modules.Commerce;
using Nihasdesk.Modules.Contacts;
using Nihasdesk.Modules.Identity;
using Nihasdesk.Modules.Inbox;
using Nihasdesk.Modules.Shipping;

namespace Nihasdesk.Api;

/// <summary>All modules hosted by the API, registered explicitly (no assembly scanning).</summary>
internal static class ModuleCatalog
{
    public static readonly IReadOnlyList<IModule> All =
    [
        new IdentityModule(),
        new ContactsModule(),
        new InboxModule(),
        new ChannelsModule(),
        new AiAgentModule(),
        new CommerceModule(),
        new ShippingModule(),
        new BillingModule(),
    ];
}
