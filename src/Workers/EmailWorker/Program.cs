using EmailWorker.Email;
using EmailWorker.Messaging;
using EmailWorker.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging;

// Requirement 4: independent email worker. A separate process from the API: it can be stopped,
// restarted or crash without affecting orders; messages wait in RabbitMQ meanwhile.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<EmailWorkerDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<OrderCreatedConsumer>();

var host = builder.Build();
host.Run();
