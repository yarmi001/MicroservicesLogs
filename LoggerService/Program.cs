using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Common;
using RabbitMQ.Client.Events;
using LoggerService;

Console.WriteLine("Logger Service is starting...");// Вывод сообщения о запуске сервиса

using (var dbContext = new AppDbContext())//Инициализация Бд
{
    bool dbConnected = false; // Флаг подключения к базе данных
    while (!dbConnected)// Цикл до успешного подключения к базе данных
    {
        try
        {
            await dbContext.Database.EnsureCreatedAsync(); // Попытка создать базу данных, если она не существует
            dbConnected = true;
            Console.WriteLine(
                "Connected to the database successfully."); // Вывод сообщения об успешном подключении к базе данных
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ожидание подключения к базе данных: {ex.Message}"); // Вывод сообщения об ошибке подключения к базе данных
        }
    }
}

var hostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";// Получение имени хоста RabbitMQ из переменных окружения или использование значения по умолчанию
var factory = new ConnectionFactory() { HostName = hostName };// Создание фабрики
IConnection connection = null; // Инициализация подключения

while (connection == null)
{
    try
    {
        connection = await factory.CreateConnectionAsync();// Попытка установить подключение к RabbitMQ
        Console.WriteLine("RabbitMq подключен");// Вывод сообщения об успешном подключении к RabbitMQ
    }
    catch (Exception ex)
    {
        Console.WriteLine("Ожидание подключения к RabbitMq: " + ex.Message);// Вывод сообщения об ошибке подключения к RabbitMQ
        await Task.Delay(5000);// Ожидание перед повторной попыткой подключения
    }
}

using var channel = await connection.CreateChannelAsync();// Создание канала для взаимодействия с RabbitMQ
await channel.QueueDeclareAsync("error_queue", durable: true, exclusive: false, autoDelete: false);// Объявление очереди для ошибок

var consumer = new AsyncEventingBasicConsumer(channel);// Создание асинхронного потребителя сообщений
consumer.ReceivedAsync += async (model, ea) => // Обработка полученных сообщений
{
    var body = ea.Body.ToArray();
    var json = Encoding.UTF8.GetString(body);// Преобразование массива байтов в строку JSON
    var logEntry = JsonSerializer.Deserialize<LogEntry>(json);// Десериализация JSON в объект LogEntry

    if (logEntry == null)
    {
        using var dbContext = new AppDbContext();
        await dbContext.Logs.AddAsync(logEntry!);// Добавление записи лога в базу данных
        await dbContext.SaveChangesAsync();// Сохранение изменений в базе данных
        Console.WriteLine($"[LoggerService] Logged error from {logEntry.ServiceName}: {logEntry.ErrorMessage}");// Вывод информации о сохраненном логе
    }
};

await channel.BasicConsumeAsync("error_queue", autoAck: true, consumer: consumer);// Начало потребления сообщений из очереди

Console.WriteLine("Logger запущен");// Вывод сообщения о запуске логгера
await Task.Delay(Timeout.Infinite);// Бесконечное ожидание для поддержания работы сервиса