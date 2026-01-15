using LoggerService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Common.Validators;
using FluentValidation;
using LoggerService.Settings;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddValidatorsFromAssemblyContaining<LogEntryValidator>();

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? "Host=localhost;Database=logs_db;Username=postgres;Password=admin";// Получение строки подключения из переменных окружения или использование значения по умолчанию

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));// Регистрация контекста данных с использованием PostgreSQL
    
builder.Services.AddHostedService<Worker>();

builder.Services.AddOptions<LoggerSettings>()
    .Bind(builder.Configuration.GetSection("LoggerSettings"))
    .ValidateDataAnnotations() // Включает проверку атрибутов [Required]
    .ValidateOnStart();        // Приложение упадет ПРИ СТАРТЕ, если конфиг кривой

var host =  builder.Build();
host.Run();