using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Common;

public class ErrorSender
{
    private readonly IChannel _channel;// RabbitMQ канал
    private readonly string _serviceName;//Название сервиса
    // Конструктор класса ErrorSender
    public ErrorSender(IChannel channel, string serviceName)
    {
        _channel = channel;
        _serviceName = serviceName;
    }
    // Метод для отправки ошибки в очередь
    public async Task SendErrorAsync(Exception ex)
    {
        try
        {
            var log = new LogEntry// Создание объекта LogEntry с информацией об ошибке
            {
                Id = Guid.NewGuid(),
                ServiceName = _serviceName,
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace ?? "No stack trace",
                Timestamp = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(log);// Сериализация объекта LogEntry в JSON
            var body = Encoding.UTF8.GetBytes(json);// Преобразование JSON в массив байтов

            await _channel.QueueDeclareAsync("error_queue", durable: true, exclusive: false, autoDelete: false);// Объявление очереди для ошибок
            await _channel.BasicPublishAsync("", "error_queue", false, body);// Отправка сообщения в очередь
            
            Console.WriteLine($"[ErrorSender] Sent error log: {ex.Message}");// Вывод информации об отправленном сообщении
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ErrorSender] Не удалось отправить лог: {e.Message}");// Вывод информации об ошибке при отправке сообщения
        }
    }
}