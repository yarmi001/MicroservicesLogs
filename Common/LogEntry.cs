namespace Common;

public class LogEntry
{
    public Guid Id { get; set; }// Уникальный идентификатор лога
    public string ServiceName { get; set; } = string.Empty;// Название сервиса, в котором произошла ошибка
    public string ErrorMessage { get; set; } = string.Empty;// Сообщение об ошибке
    public string StackTrace { get; set; } = string.Empty;// Стек вызовов при ошибке
    public DateTime Timestamp { get; set; }// Временная метка ошибки
}
