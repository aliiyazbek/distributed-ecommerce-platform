namespace EmailWorker.Email;

// SMTP settings, bound from the "Email" config section.
// Default (appsettings.json): Mailpit on localhost:1025, a local test inbox at http://localhost:8025.
// Real emails: override with user-secrets (smtp.gmail.com:587, StartTls, Gmail address + app password).
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;

    // None (Mailpit) | StartTls (Gmail on 587) | SslOnConnect (port 465)
    public string Security { get; set; } = "None";

    public string? UserName { get; set; }
    public string? Password { get; set; }

    public string FromAddress { get; set; } = "orders@ecommerce.local";
    public string FromName { get; set; } = "E-Commerce Store";

    public int TimeoutSeconds { get; set; } = 15;
}
