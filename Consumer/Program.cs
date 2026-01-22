using Consumer;
using Consumer.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// 1. Настройка Serilog
var seqApiKey = Environment.GetEnvironmentVariable("SEQ_API_KEY");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.Seq("http://seq:5341", apiKey: seqApiKey)
    .CreateLogger();

try
{
    Log.Information("Starting Consumer Host...");

    var builder = Host.CreateApplicationBuilder(args);

    // 2. Serilog в DI
    builder.Services.AddSerilog();

    // 3. Настройки ConsumerSettings
    builder.Services.AddOptions<ConsumerSettings>()
        .Bind(builder.Configuration.GetSection("ConsumerSettings"))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // 4. Воркер
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Consumer terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}