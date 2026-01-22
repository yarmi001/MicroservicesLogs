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
using Polly;
using Polly.Retry;

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
        
        // --- ПОТРЕБИТЕЛЬ 1: Архивариус (Слушает ВСЁ и пишет в БД) ---
        var mainConsumer = new AsyncEventingBasicConsumer(_channel!);
        mainConsumer.ReceivedAsync += async (model, ea) =>
        {
            await ProcessLogForDatabaseAsync(ea);
        };
        await _channel!.BasicConsumeAsync("all_logs_queue", true, mainConsumer);

        // --- ПОТРЕБИТЕЛЬ 2: Алерт (Слушает ТОЛЬКО ОШИБКИ) ---
        // Это демонстрация фильтрации сообщений (Topic Exchange)
        var alertConsumer = new AsyncEventingBasicConsumer(_channel!);
        alertConsumer.ReceivedAsync += async (model, ea) =>
        {
            await ProcessAlertAsync(ea);
        };        
        await _channel!.BasicConsumeAsync("critical_errors_queue", true, consumer: alertConsumer);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessLogForDatabaseAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            LogEntry? logEntry = null;
            
            try { logEntry = JsonSerializer.Deserialize<LogEntry>(json); }
            catch (JsonException) { return; }

            if (logEntry == null) return;

            var validationResult = await _validator.ValidateAsync(logEntry);
            if (!validationResult.IsValid) return;

            try 
            {
                // Пытаемся создать Scope. Если приложение выключается, здесь вылетит ошибка.
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    dbContext.Logs.Add(logEntry);
                    await dbContext.SaveChangesAsync();
                }
                
                _logger.LogInformation($"[DB SAVED] {logEntry.Type}: {logEntry.Message}");
                
                // Подтверждаем обработку только если успешно сохранили
                await _channel!.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (ObjectDisposedException)
            {
                // Игнорируем ошибку, так как приложение выключается.
                // Не делаем ни Ack, ни Nack - пусть сообщение останется в очереди до следующего запуска.
                _logger.LogWarning("Приложение останавливается, сообщение не сохранено.");
                return;
            }
            _logger.LogInformation($"[DB SAVED] {logEntry.Type}: {logEntry.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving log: {ex.Message}");
        }
    }
    
    private async Task ProcessAlertAsync(BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);
        
        _logger.LogWarning($" >>>>> [ALERT SYSTEM] Critical Error Detected! Payload: {json} <<<<<");
        
        await Task.CompletedTask;
    }

    private async Task InitDatabaseAsync(CancellationToken token)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 30)),
                (exception, timeSpan) =>
                {
                    _logger.LogWarning("БД недоступна. Повтор через {TimeSpan} сек. Ошибка: {Error}", timeSpan.TotalSeconds, exception.Message);
                }
            );

        await dbPolicy.ExecuteAsync(async (ct) =>
        {
            await dbContext.Database.MigrateAsync(ct);
        }, token);
        
        _logger.LogInformation("Database migrated successfully via Polly.");
    }

    private async Task InitRabbitMqAsync(CancellationToken token)
    {
        // 3. ИСПОЛЬЗУЕМ НАСТРОЙКИ ВМЕСТО ENVIRONMENT
        
        var factory = new ConnectionFactory { 
            HostName = _settings.RabbitMqHost, 
            UserName = _settings.UserName,
            Password = _settings.Password
        };

        var retryPolicy = Policy
            .Handle<Exception>() // Ловим любые ошибки
            .WaitAndRetryForeverAsync( // Пытаемся вечно (пока не отменим токен)
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 30)), 
                (exception, timeSpan) =>
                {
                    // Этот код сработает, только если произошла ошибка
                    _logger.LogWarning("RabbitMQ недоступен. Повтор через {Time} сек. Ошибка: {Error}", 
                        timeSpan.TotalSeconds, exception.Message);
                }
            );
        
        await retryPolicy.ExecuteAsync(async (ct) =>
        {
            _connection = await factory.CreateConnectionAsync(ct);
        }, token);
        
        _logger.LogInformation("Connected to RabbitMQ");

        _channel = await _connection!.CreateChannelAsync(cancellationToken: token);

        await _channel.ExchangeDeclareAsync("logs_exchange", ExchangeType.Topic, durable: true, cancellationToken: token);
        
        // --- ОЧЕРЕДЬ 1: ВСЕ ЛОГИ (для БД) ---
        var allLogsQueue = "all_logs_queue";
        await _channel.QueueDeclareAsync(allLogsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
        // Подписываемся на "#" (любой Routing Key)
        await _channel.QueueBindAsync(allLogsQueue, "logs_exchange", "#", cancellationToken: token);

        // --- ОЧЕРЕДЬ 2: ТОЛЬКО ОШИБКИ (для Алертов) ---
        var errorQueue = "critical_errors_queue";
        await _channel.QueueDeclareAsync(errorQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
        
        // Подписываемся только на "error.*" и "critical.*"
        // Звездочка (*) заменяет одно слово, решетка (#) - сколько угодно слов.
        // У нас ключи вида "type.ServiceName", поэтому "error.#" сработает.
        await _channel.QueueBindAsync(errorQueue, "logs_exchange", "error.#", cancellationToken: token);
        await _channel.QueueBindAsync(errorQueue, "logs_exchange", "critical.#", cancellationToken: token);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}