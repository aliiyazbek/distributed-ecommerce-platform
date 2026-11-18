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

    // Demo switch: every send fails with a temporary error, so a message visibly goes through
    // the retries (10 s apart) and ends in the DLQ. Turn on with: Email__SimulateFailure=true
    public bool SimulateFailure { get; set; }

    public static readonly string[] SecurityValues = ["None", "StartTls", "SslOnConnect"];

    // Checked once at startup (ValidateOnStart): a typo in the settings stops the worker immediately
    // instead of failing on every email later.
    public bool IsValid(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Host)) error = "Email:Host is required.";
        else if (Port is < 1 or > 65535) error = "Email:Port must be between 1 and 65535.";
        else if (!SecurityValues.Contains(Security)) error = $"Email:Security must be one of: {string.Join(", ", SecurityValues)}.";
        else if (string.IsNullOrWhiteSpace(FromAddress)) error = "Email:FromAddress is required.";
        else if (!string.IsNullOrEmpty(UserName) && string.IsNullOrEmpty(Password)) error = "Email:Password is required when Email:UserName is set.";
        return error.Length == 0;
    }
}
