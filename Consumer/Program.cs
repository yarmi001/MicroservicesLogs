using Consumer;
using Consumer.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<ConsumerSettings>()
    .Bind(builder.Configuration.GetSection("ConsumerSettings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();