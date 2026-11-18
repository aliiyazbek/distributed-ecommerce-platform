namespace Shared.Messaging;

// Published on every status change. UserId lets the real-time side send it only to the order's owner.
public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    Guid UserId,
    string OldStatus,
    string NewStatus,
    DateTimeOffset ChangedAt);