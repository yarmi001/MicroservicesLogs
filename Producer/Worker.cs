using System.Text;
using Common;
using Microsoft.Extensions.Options;
using Producer.Settings;
using RabbitMQ.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

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

        // Первичное подключение
        await InitRabbitMqAsync(stoppingToken);

        int i = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Проверяем: если соединение потеряно или канал закрыт
            if (_connection == null || !_connection.IsOpen || _channel == null || !_channel.IsOpen)
            {
                _logger.LogWarning("Обнаружен разрыв соединения! Запускаю процедуру восстановления...");
                
                // Вызываем метод инициализации, внутри которого живет Polly.
                // Он сам будет "долбиться" до кролика, пока тот не оживет.
                await InitRabbitMqAsync(stoppingToken);
            }
            // ----------------------------------

            i++;
            try
            {
                string msg = $"Work message #{i}";
                var body = Encoding.UTF8.GetBytes(msg);

                await _channel!.BasicPublishAsync("", _settings.QueueName, false, body, cancellationToken: stoppingToken);
                _logger.LogInformation("Sent: {Msg}", msg);

                if (_mqLogger != null)
                    await _mqLogger.LogAsync($"Task #{i} sent", LogType.Info);

                if (i % 10 == 0) throw new Exception($"Simulated error at #{i}");

                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Ошибка в цикле Producer: {Error}", ex.Message);
                
                // ВАЖНО: Если упал сам Кролик, то отправить лог в Кролика тоже не выйдет.
                // Поэтому оборачиваем в try-catch, чтобы не спамить ошибками логирования
                try 
                {
                    if (_mqLogger != null && _connection!.IsOpen)
                        await _mqLogger.LogAsync("Error in Producer loop", LogType.Error, ex);
                }
                catch { /* Игнорируем ошибки логгера при разрыве сети */ }
                
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task InitRabbitMqAsync(CancellationToken token)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.RabbitMqHost,
            UserName = _settings.UserName, // Используем логин из конфига
            Password = _settings.Password  // Используем пароль из конфига
        };

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