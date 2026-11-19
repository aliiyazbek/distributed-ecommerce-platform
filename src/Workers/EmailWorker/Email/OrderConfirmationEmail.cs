using System.Globalization;
using System.Net;
using System.Text;
using MimeKit;
using Shared.Messaging;

namespace EmailWorker.Email;

// Builds the order confirmation email (HTML + plain-text alternative) from an OrderCreated event.
public static class OrderConfirmationEmail
{
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    public static MimeMessage Create(OrderCreatedEvent order, EmailOptions options)
    {
        var orderNumber = OrderNumber(order.OrderId);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        message.To.Add(MailboxAddress.Parse(order.CustomerEmail)); // throws ParseException if malformed
        message.Subject = $"Order {orderNumber} confirmed";

        var body = new BodyBuilder
        {
            TextBody = PlainText(order, orderNumber),
            HtmlBody = Html(order, orderNumber)
        };
        message.Body = body.ToMessageBody();
        return message;
    }

    // Same short order number the store shows: first 8 hex characters of the id, upper case.
    private static string OrderNumber(Guid orderId) => orderId.ToString("N")[..8].ToUpperInvariant();

    private static string PlainText(OrderCreatedEvent order, string orderNumber)
    {
        var text = new StringBuilder()
            .AppendLine($"Thank you for your order {orderNumber}.")
            .AppendLine($"Placed on {order.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC.")
            .AppendLine();

        foreach (var item in order.Items)
            text.AppendLine($"{item.Quantity} x {item.ProductName}  {(item.UnitPrice * item.Quantity).ToString("C", Money)}");

        return text
            .AppendLine()
            .AppendLine($"Total: {order.TotalPrice.ToString("C", Money)}")
            .AppendLine()
            .AppendLine("We will email you again when your order ships.")
            .ToString();
    }

    private static string Html(OrderCreatedEvent order, string orderNumber)
    {
        var rows = new StringBuilder();
        foreach (var item in order.Items)
        {
            rows.Append("<tr>")
                .Append($"<td style=\"padding:6px 12px\">{WebUtility.HtmlEncode(item.ProductName)}</td>")
                .Append($"<td style=\"padding:6px 12px;text-align:center\">{item.Quantity}</td>")
                .Append($"<td style=\"padding:6px 12px;text-align:right\">{(item.UnitPrice * item.Quantity).ToString("C", Money)}</td>")
                .Append("</tr>");
        }

        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:560px;color:#1f2328">
              <h2 style="margin-bottom:4px">Thank you for your order</h2>
              <p style="color:#555;margin-top:0">Order <b>{orderNumber}</b> &middot; {order.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC</p>
              <table style="border-collapse:collapse;width:100%;border:1px solid #ddd">
                <tr style="background:#f3f4f6">
                  <th style="padding:6px 12px;text-align:left">Product</th>
                  <th style="padding:6px 12px">Qty</th>
                  <th style="padding:6px 12px;text-align:right">Amount</th>
                </tr>
                {rows}
                <tr style="border-top:1px solid #ddd">
                  <td colspan="2" style="padding:6px 12px;text-align:right"><b>Total</b></td>
                  <td style="padding:6px 12px;text-align:right"><b>{order.TotalPrice.ToString("C", Money)}</b></td>
                </tr>
              </table>
              <p>We will email you again when your order ships.</p>
            </div>
            """;
    }
}
