using System.Text;
using System.Text.Json;
using Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LoggerService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IValidator<LogEntry> _validator;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider, IValidator<LogEntry> validator)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _validator = validator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
        await InitDatabaseAsync(stoppingToken);
        await InitRabbitMqAsync(stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(_channel);
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

            if (logEntry == null || !logEntry.IsValid())
            {
                _logger.LogWarning($"Failed to parse log entry: {json}");
                return;
            }

            var validationResult = await _validator.ValidateAsync(logEntry);
            
            if (!validationResult.IsValid)
            {
                // Собираем все ошибки в одну строку для удобства
                var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                _logger.LogWarning($"Log entry validation failed: {errors}. Raw JSON: {json}");
                return;
            }

            // Если всё ок — сохраняем
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Logs.Add(logEntry);
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation($"Log entry saved: {logEntry.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing log message: {ex.Message}");
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
                await dbContext.Database.EnsureCreatedAsync(token);
                _logger.LogInformation("Database connected and ensured created.");
                return;
            }
            catch
            {
                _logger.LogWarning("Database could not be created.");
                await Task.Delay(3000, token);
            }
        }
    }
    private async Task InitRabbitMqAsync(CancellationToken token)
    {
        var factory = new ConnectionFactory { 
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost" 
        };

        // Используем переданный 'token' для отмены ожидания
        while (_connection == null && !token.IsCancellationRequested)
        {
            try 
            {
                // Передаем токен в CreateConnectionAsync
                _connection = await factory.CreateConnectionAsync(token);
            }
            catch 
            {
                _logger.LogWarning("RabbitMQ недоступен. Жду 3 сек...");
                await Task.Delay(3000, token); // Передаем токен в Delay
            }
        }

        // Передаем токен в CreateChannelAsync (параметр называется cancellationToken)
        _channel = await _connection!.CreateChannelAsync(cancellationToken: token);

        // Объявляем Exchange
        // Важно: в v7 лучше явно передавать cancellationToken
        await _channel.ExchangeDeclareAsync(
            exchange: "logs_exchange", 
            type: ExchangeType.Topic, 
            durable: true, 
            autoDelete: false, 
            arguments: null, 
            cancellationToken: token);
        
        // Объявляем очередь
        var queueName = "all_logs_queue";
        await _channel.QueueDeclareAsync(
            queue: queueName, 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            arguments: null, 
            cancellationToken: token);

        // Биндинг
        await _channel.QueueBindAsync(
            queue: queueName, 
            exchange: "logs_exchange", 
            routingKey: "#", 
            arguments: null, 
            cancellationToken: token);
    }
    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}