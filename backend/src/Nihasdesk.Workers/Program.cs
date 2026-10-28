using Nihasdesk.Workers;

// Host for background work: email outbox delivery now; webhook processing, AI jobs, broadcasts
// and summaries later (docs/architecture.md). Every module contributes through AddWorkerServices.
var builder = Host.CreateApplicationBuilder(args);

foreach (var module in ModuleCatalog.All)
{
    module.AddWorkerServices(builder.Services, builder.Configuration);
}

var host = builder.Build();

await host.RunAsync();
