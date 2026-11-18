using EmailWorker.Email;
using EmailWorker.Messaging;
using EmailWorker.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Messaging;

// Requirement 4: independent email worker. A separate process from the API: it can be stopped,
// restarted or crash without affecting orders; messages wait in RabbitMQ meanwhile.
var builder = Host.CreateApplicationBuilder(args);

// Settings come from appsettings.json (Mailpit defaults), then user-secrets in Development
// (Gmail address + app password, never committed), then environment variables (e.g. Email__SimulateFailure=true).
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(options => options.IsValid(out _), "Invalid Email settings (see Email:Host, Port, Security, FromAddress, UserName/Password).")
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<EmailWorkerDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<OrderCreatedConsumer>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

EmailOptions email;
try
{
    email = host.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
}
catch (OptionsValidationException)
{
    var raw = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
    raw.IsValid(out var error);
    logger.LogCritical("Invalid Email settings: {Error} The worker will not start.", error);
    return 1;
}

// Show which mail server is in use (never the password), so the demo makes it obvious.
logger.LogInformation("Sending emails via {Host}:{Port} ({Security}) as {From}{Simulate}",
    email.Host, email.Port, email.Security, email.FromAddress,
    email.SimulateFailure ? " - SIMULATED FAILURES ON" : string.Empty);

await host.RunAsync();
return 0;
