using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Producer;
using Producer.Settings;

var builder = Host.CreateApplicationBuilder(args);

// Регистрируем настройки с валидацией
builder.Services.AddOptions<ProducerSettings>()
    .Bind(builder.Configuration.GetSection("ProducerSettings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Регистрируем воркер
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();