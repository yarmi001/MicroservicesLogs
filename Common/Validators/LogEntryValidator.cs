using FluentValidation;

namespace Common.Validators;

public class LogEntryValidator : AbstractValidator<LogEntry>
{
    public LogEntryValidator()
    {
        // Проверка: ID не должен быть пустым
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Log ID is required");

        // Проверка: Имя сервиса обязательно и не длиннее 100 символов
        RuleFor(x => x.ServiceName)
            .NotEmpty().WithMessage("ServiceName cannot be empty")
            .MaximumLength(100).WithMessage("ServiceName is too long");

        // Проверка: Сообщение обязательно
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Log message is missing");

        // Проверка: Тип лога должен быть из Enum
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid LogType");

        // Проверка: Дата не может быть из далекого будущего (защита от багов времени)
        RuleFor(x => x.Timestamp)
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5))
            .WithMessage("Timestamp cannot be in the future");
    }
}