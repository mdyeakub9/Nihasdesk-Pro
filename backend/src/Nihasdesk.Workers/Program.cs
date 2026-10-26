// Host for webhook processing, AI jobs, broadcasts and summaries. Consumers are added per module
// once the message bus is chosen (see docs/architecture.md).
var builder = Host.CreateApplicationBuilder(args);

var host = builder.Build();

await host.RunAsync();
