using Common.Validators;
using FluentValidation;
using LoggerService;
using LoggerService.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;


// 1. Настраиваем глобальный логгер ДО создания хоста.
// Это позволяет поймать ошибки конфигурации и падения при старте.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console() // Вывод в черную консоль Docker
    .WriteTo.Seq("http://seq:5341") // Отправка в сервис Seq (порт 5341 - внутренний для контейнеров)
    .CreateLogger();

try
{
    Log.Information("Starting LoggerService host...");

    var builder = Host.CreateApplicationBuilder(args);

    // 2. Подключаем Serilog вместо стандартного логгера .NET
    builder.Services.AddSerilog();

    // 3. Настройка Конфигурации (Options Pattern)
    // Связываем секцию "LoggerSettings" из переменных окружения с классом
    builder.Services.AddOptions<LoggerSettings>()
        .Bind(builder.Configuration.GetSection("LoggerSettings"))
        .ValidateDataAnnotations() // Проверяет атрибуты [Required]
        .ValidateOnStart();        // Приложение упадет сразу, если настройки кривые

    // 4. Регистрация Валидатора (FluentValidation)
    // Ищет все валидаторы в сборке, где живет LogEntryValidator
    builder.Services.AddValidatorsFromAssemblyContaining<LogEntryValidator>();

    // 5. Регистрация Базы Данных (PostgreSQL)
    // Используем лямбду с serviceProvider, чтобы достать настройки из DI
    builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    {
        // Получаем уже валидированные настройки
        var settings = serviceProvider.GetRequiredService<IOptions<LoggerSettings>>().Value;
        
        // Используем строку подключения
        options.UseNpgsql(settings.ConnectionString);
    });

    // 6. Регистрация основного Воркера
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    
    // Запуск приложения
    host.Run();
}
catch (Exception ex)
{
    // Этот блок сработает, если приложение упадет при старте (например, нет конфига)
    Log.Fatal(ex, "LoggerService terminated unexpectedly!");
}
finally
{
    // Обязательно сбрасываем логи перед выходом
    Log.CloseAndFlush();
}