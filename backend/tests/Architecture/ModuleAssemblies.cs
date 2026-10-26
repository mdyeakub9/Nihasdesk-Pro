using System.Reflection;
using Nihasdesk.Modules.AiAgent;
using Nihasdesk.Modules.Billing;
using Nihasdesk.Modules.Channels;
using Nihasdesk.Modules.Commerce;
using Nihasdesk.Modules.Contacts;
using Nihasdesk.Modules.Identity;
using Nihasdesk.Modules.Inbox;
using Nihasdesk.Modules.Shipping;

namespace Nihasdesk.ArchitectureTests;

internal static class ModuleAssemblies
{
    public static readonly IReadOnlyList<Assembly> All =
    [
        typeof(IdentityModule).Assembly,
        typeof(ContactsModule).Assembly,
        typeof(InboxModule).Assembly,
        typeof(ChannelsModule).Assembly,
        typeof(AiAgentModule).Assembly,
        typeof(CommerceModule).Assembly,
        typeof(ShippingModule).Assembly,
        typeof(BillingModule).Assembly,
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var assembly in All)
        {
            data.Add(assembly.GetName().Name!);
        }

        return data;
    }

    public static Assembly ByName(string name) => All.Single(a => a.GetName().Name == name);
}
