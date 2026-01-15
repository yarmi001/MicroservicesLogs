using System.ComponentModel.DataAnnotations;

namespace LoggerService.Settings;

public class LoggerSettings
{
    [Required(ErrorMessage = "Connection String is missing! Check docker-compose or appsettings.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string RabbitMqHost { get; set; } = string.Empty;

    [Range(1, 10000)]
    public int MaxBatchSize { get; set; } = 10;
}