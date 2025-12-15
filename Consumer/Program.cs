using System.Text;
using Common;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

Console.WriteLine("Consumer Service is starting...");// Вывод сообщения о запуске сервиса

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
await channel.QueueDeclareAsync("work_queue", false, false, false);// Объявление очереди для работы

var errorSender = new ErrorSender(channel, "Consumer Service");// Инициализация отправителя ошибок

var consumer = new AsyncEventingBasicConsumer(channel);// Создание асинхронного потребителя сообщений
consumer.ReceivedAsync += async (model, ea) => // Обработка полученных сообщений
{
    try
    {
        var msg = Encoding.UTF8.GetString(ea.Body.ToArray());// Преобразование массива байтов в строку
        Console.WriteLine($"[Consumer] Received: {msg}");// Вывод полученного сообщения
        // Имитация ошибки (если в тексте есть цифра 0)
        if (msg.Contains("0"))
        {
            throw new InvalidOperationException("error detected in message content");// Генерация ошибки
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Ошибка при обработке сообщения: {e.Message}");// Вывод информации об ошибке при обработке сообщения
        await errorSender.SendErrorAsync(e);
    }  
};

await channel.BasicConsumeAsync("work_queue", true, consumer:consumer);// Начало потребления сообщений из очереди
await Task.Delay(Timeout.Infinite);
