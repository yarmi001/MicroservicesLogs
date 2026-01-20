using System.Text;
using Common;
using Microsoft.Extensions.Options;
using Producer.Settings;
using RabbitMQ.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Producer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ProducerSettings _settings;
    private IConnection? _connection;
    private IChannel? _channel;
    private RabbitMqLogger? _mqLogger;

    public Worker(ILogger<Worker> logger, IOptions<ProducerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Producer Worker стартует...");
        await InitRabbitMqAsync(stoppingToken);

        int i = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            i++;
            try
            {
                string msg = $"Task #{i}";
                var body = Encoding.UTF8.GetBytes(msg);

                // Отправка задачи в Work Queue
                await _channel!.BasicPublishAsync("", _settings.QueueName, false, body, cancellationToken: stoppingToken);
                _logger.LogInformation("Sent: {Msg}", msg);

                // Отправка Info лога в LoggerService
                if (_mqLogger != null)
                    await _mqLogger.LogAsync($"Task #{i} created", LogType.Info);

                // Имитация ошибки (каждое 10-е сообщение)
                if (i % 10 == 0) throw new Exception($"Simulated error at #{i}");

                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки");
                if (_mqLogger != null)
                    await _mqLogger.LogAsync("Producer Error", LogType.Error, ex);
                
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task InitRabbitMqAsync(CancellationToken token)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.RabbitMqHost,
            UserName = _settings.UserName, // <--- ВАЖНО: Используем логин из конфига
            Password = _settings.Password  // <--- ВАЖНО: Используем пароль из конфига
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
                _logger.LogWarning("RabbitMQ недоступен. Жду 3 сек...");
                await Task.Delay(3000, token);
            }
        }

        _channel = await _connection!.CreateChannelAsync(cancellationToken: token);

        // Настройка DLQ (Dead Letter Queue) - чтобы совпадало с Consumer
        await _channel.ExchangeDeclareAsync("dlx_exchange", ExchangeType.Direct, durable: true, cancellationToken: token);
        await _channel.QueueDeclareAsync("dead_letter_queue", durable: true, false, false, cancellationToken: token);
        await _channel.QueueBindAsync("dead_letter_queue", "dlx_exchange", "dlq_key", cancellationToken: token);

        var args = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", "dlx_exchange" },
            { "x-dead-letter-routing-key", "dlq_key" }
        };

        // Объявляем рабочую очередь
        await _channel.QueueDeclareAsync(_settings.QueueName, false, false, false, args, cancellationToken: token);

        // Инициализируем логгер
        _mqLogger = new RabbitMqLogger(_channel, "ProducerService");
        await _mqLogger.InitializeAsync();
    }
}