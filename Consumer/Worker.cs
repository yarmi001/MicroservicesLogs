using System.Text;
using Common;
using Consumer.Settings;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;

namespace Consumer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ConsumerSettings _settings;
    private IConnection? _connection;
    private IChannel? _channel;
    private RabbitMqLogger? _mqLogger;

    public Worker(ILogger<Worker> logger, IOptions<ConsumerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitRabbitMqAsync(stoppingToken);
        await StartConsumingAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task StartConsumingAsync(CancellationToken token)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            await ProcessMessageAsync(ea);
        };

        // AutoAck = false (подтверждаем вручную)
        await _channel!.BasicConsumeAsync(_settings.QueueName, autoAck: false, consumer: consumer, cancellationToken: token);
    }

    private async Task ProcessMessageAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var msg = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("Received: {Msg}", msg);

            // Бизнес-проверка
            if (msg.Contains("0")) throw new InvalidOperationException("Invalid message content (contains 0)");

            // Успех
            await _channel!.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError("Processing failed. Moving to DLQ.");
            
            if (_mqLogger != null)
                await _mqLogger.LogAsync("Consumer Processing Error", LogType.Error, ex);

            // Отправляем в DLQ (requeue: false)
            await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
        }
    }

    private async Task InitRabbitMqAsync(CancellationToken token)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.RabbitMqHost,
            UserName = _settings.UserName, //  ЛОГИН ИЗ КОНФИГА
            Password = _settings.Password  //  ПАРОЛЬ ИЗ КОНФИГА
        };

        // 1. СОЗДАЕМ ПОЛИТИКУ (Правила повторов)
        var retryPolicy = Policy
            .Handle<Exception>() // Ловим любые ошибки (сеть, авторизация и т.д.)
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 30)), 
                (exception, timeSpan) =>
                {
                    _logger.LogWarning("RabbitMQ недоступен. Повтор через {Time} сек. Ошибка: {Error}", 
                        timeSpan.TotalSeconds, exception.Message);
                }
            );

        // 2. ВЫПОЛНЯЕМ ПОДКЛЮЧЕНИЕ (Внутри "пузыря" Polly)
        await retryPolicy.ExecuteAsync(async (ct) =>
        {
            // Пытаемся подключиться. Если упадет - Polly поймает и повторит.
            _connection = await factory.CreateConnectionAsync(ct);
        }, token);

        // 3. ЭТОТ КОД ВЫПОЛНИТСЯ ТОЛЬКО ПОСЛЕ УСПЕШНОГО ПОДКЛЮЧЕНИЯ
        _logger.LogInformation("Успешное подключение к RabbitMQ через Polly!");

        _channel = await _connection!.CreateChannelAsync(cancellationToken: token);

        // Настройка DLQ
        await _channel.ExchangeDeclareAsync("dlx_exchange", ExchangeType.Direct, durable: true, cancellationToken: token);
        await _channel.QueueDeclareAsync("dead_letter_queue", durable: true, false, false, cancellationToken: token);
        await _channel.QueueBindAsync("dead_letter_queue", "dlx_exchange", "dlq_key", cancellationToken: token);

        var args = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", "dlx_exchange" },
            { "x-dead-letter-routing-key", "dlq_key" }
        };

        await _channel.QueueDeclareAsync(_settings.QueueName, false, false, false, args, cancellationToken: token);

        _mqLogger = new RabbitMqLogger(_channel, "ConsumerService");
        await _mqLogger.InitializeAsync();
    }
}