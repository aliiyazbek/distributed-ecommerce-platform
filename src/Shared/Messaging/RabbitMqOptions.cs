using RabbitMQ.Client;

namespace Shared.Messaging;

// Requirement 4: RabbitMQ connection settings, bound from the "RabbitMq" config section.
// Shared by the API (outbox publisher) and the email worker.
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    public ConnectionFactory CreateConnectionFactory(string clientName) => new()
    {
        HostName = HostName,
        Port = Port,
        UserName = UserName,
        Password = Password,
        VirtualHost = VirtualHost,
        ClientProvidedName = clientName,   // shown in the RabbitMQ dashboard under Connections
        AutomaticRecoveryEnabled = true,   // reconnect by itself after RabbitMQ restarts
        TopologyRecoveryEnabled = true
    };
}