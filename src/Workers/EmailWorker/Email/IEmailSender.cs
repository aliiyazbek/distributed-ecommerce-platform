using Shared.Messaging;

namespace EmailWorker.Email;

public interface IEmailSender
{
    // Throws EmailSendException when the email could not be sent.
    Task SendOrderConfirmationAsync(OrderCreatedEvent order, CancellationToken cancellationToken);
}
