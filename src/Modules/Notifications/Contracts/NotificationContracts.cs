namespace Modules.Notifications.Contracts;

public record SendEmailRequest(string? ToEmail, string Subject, string Message);