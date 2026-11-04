using MediatR;

namespace Shared.Events;

public record ProductCreatedIntegrationEvent(Guid ProductId, string ProductName, string Category, decimal Price) : INotification;