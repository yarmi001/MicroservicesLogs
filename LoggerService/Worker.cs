using System.Text;
using System.Text.Json;
using Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using LoggerService.Settings;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace LoggerService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IValidator<LogEntry> _validator;
    private readonly LoggerSettings _settings; // 1. Добавляем поле для настроек
    
    private IConnection? _connection;
    private IChannel? _channel;

    // 2. Внедряем IOptions<LoggerSettings> в конструктор
    public Worker(
        ILogger<Worker> logger, 
        IServiceProvider serviceProvider, 
        IValidator<LogEntry> validator,
        IOptions<LoggerSettings> settings) 
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _validator = validator;
        _settings = settings.Value; // Достаем само значение настроек
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
        await InitDatabaseAsync(stoppingToken);
        await InitRabbitMqAsync(stoppingToken);
        
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            await ProcessMessagesAsync(ea);
        };
        
        await _channel!.BasicConsumeAsync("all_logs_queue", true, consumer: consumer);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessagesAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            LogEntry? logEntry = null;
            
            try
            {
                logEntry = JsonSerializer.Deserialize<LogEntry>(json);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError($"Failed to deserialize log entry: {jsonEx.Message}");
                return;
            }

            if (logEntry == null) return;

            var validationResult = await _validator.ValidateAsync(logEntry);
            
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                _logger.LogWarning($"Log validation failed: {errors}");
                return;
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Logs.Add(logEntry);
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation($"Log saved: {logEntry.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing log: {ex.Message}");
        }
    }

    private async Task InitDatabaseAsync(CancellationToken token)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        while (!token.IsCancellationRequested)
        {
            try
            {
                await dbContext.Database.MigrateAsync(token);
                _logger.LogInformation("Database connected and ensured created.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database could not be created. Retrying...");
                await Task.Delay(3000, token);
            }
        }
    }

    private async Task InitRabbitMqAsync(CancellationToken token)
    {
        // 3. ИСПОЛЬЗУЕМ НАСТРОЙКИ ВМЕСТО ENVIRONMENT
        // Было: Environment.GetEnvironmentVariable(...)
        // Стало: _settings.RabbitMqHost
        
        var factory = new ConnectionFactory { 
            HostName = _settings.RabbitMqHost, 
            UserName = _settings.UserName,
            Password = _settings.Password
        };

        while (_connection == null && !token.IsCancellationRequested)
        {
            try 
            {
                _connection = await factory.CreateConnectionAsync(token);
                _logger.LogInformation("RabbitMQ Connected!");
            }
            catch 
            {
                _logger.LogWarning("RabbitMQ недоступен ({Host}). Жду 3 сек...", _settings.RabbitMqHost);
                await Task.Delay(3000, token);
            }
        }

        _channel = await _connection!.CreateChannelAsync(cancellationToken: token);

        await _channel.ExchangeDeclareAsync("logs_exchange", ExchangeType.Topic, durable: true, cancellationToken: token);
        
        var queueName = "all_logs_queue";
        await _channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);

        await _channel.QueueBindAsync(queueName, "logs_exchange", "#", cancellationToken: token);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}