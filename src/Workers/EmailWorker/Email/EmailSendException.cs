namespace EmailWorker.Email;

// Raised by the email sender. IsPermanent decides what the consumer does with the message:
//   transient (SMTP down, timeout, 4xx, login problem) -> retry queue, try again later
//   permanent (5xx such as an invalid recipient, malformed address) -> dead-letter queue right away
public sealed class EmailSendException : Exception
{
    public bool IsPermanent { get; }

    public EmailSendException(string message, bool isPermanent, Exception? inner = null)
        : base(message, inner)
    {
        IsPermanent = isPermanent;
    }
}
