using System.ComponentModel.DataAnnotations;

namespace Producer.Settings; 

public class ProducerSettings
{
    [Required(ErrorMessage = "RabbitMqHost is required!")] // Атрибут обязательности
    public string RabbitMqHost { get; set; } = "localhost";

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    public string QueueName { get; set; } = "work_queue";
}