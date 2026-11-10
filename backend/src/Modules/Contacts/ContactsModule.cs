using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Contacts.Features.ContactDetails;
using Nihasdesk.Modules.Contacts.Infrastructure;

namespace Nihasdesk.Modules.Contacts;

public sealed class ContactsModule : IModule
{
    public string Name => ContactsDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContactsDbContext>(configuration, ContactsDbContext.Schema);
        services.AddScoped<IContactDirectory, ContactDirectory>();
        services.AddScoped<IContactProfile, ContactProfiles>();
        services.AddScoped<IContactInterest, ContactInterests>();
        services.AddScoped<IValidator<UpdateContactRequest>, UpdateContactValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ContactEndpoints.Map(endpoints);
    }
}
