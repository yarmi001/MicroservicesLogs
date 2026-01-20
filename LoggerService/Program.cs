using LoggerService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Common.Validators;
using FluentValidation;
using LoggerService.Settings;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// 1. Регистрируем настройки и валидацию
builder.Services.AddOptions<LoggerSettings>()
    .Bind(builder.Configuration.GetSection("LoggerSettings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// 2. Регистрируем валидаторы
builder.Services.AddValidatorsFromAssemblyContaining<LogEntryValidator>();

// 3. Регистрируем БД, используя IOptions (САМЫЙ ВАЖНЫЙ МОМЕНТ)
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    // Получаем настройки из DI контейнера
    var settings = serviceProvider.GetRequiredService<IOptions<LoggerSettings>>().Value;
    
    // Используем строку из настроек
    options.UseNpgsql(settings.ConnectionString);
});
    
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();