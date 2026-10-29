using MediatR;

namespace Shared.Events;

public record OrderStatusChangedIntegrationEvent(Guid OrderId, Guid UserId, string Status) : INotification;
