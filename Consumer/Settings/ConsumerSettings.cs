using System.ComponentModel.DataAnnotations;

namespace Consumer.Settings; 

public class ConsumerSettings
{
    [Required]
    public string RabbitMqHost { get; set; } = "localhost";

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    public string QueueName { get; set; } = "work_queue";
}