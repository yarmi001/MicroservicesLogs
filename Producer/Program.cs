using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Producer;
using Producer.Settings;
using Serilog;

// 1. Настраиваем Serilog (Логирование) до создания билдера
// Это позволяет ловить ошибки самого старта приложения
var seqApiKey = Environment.GetEnvironmentVariable("SEQ_API_KEY");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console() // Пишем в консоль Docker
    .WriteTo.Seq("http://seq:5341", apiKey: seqApiKey) // Пишем в Seq с ключом
    .CreateLogger();

try
{
    Log.Information("Starting Producer Host...");

    var builder = Host.CreateApplicationBuilder(args);

    // 2. Подключаем Serilog в DI контейнер
    builder.Services.AddSerilog();

    // 3. Регистрируем настройки (Options Pattern)
    builder.Services.AddOptions<ProducerSettings>()
        .Bind(builder.Configuration.GetSection("ProducerSettings")) // Ищем секцию в конфиге
        .ValidateDataAnnotations() // Проверяем [Required] атрибуты
        .ValidateOnStart();        // Падаем сразу, если конфиг битый

    // 4. Регистрируем Воркер (Фоновая задача)
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Producer terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}