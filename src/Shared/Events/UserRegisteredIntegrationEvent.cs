using System;
using MediatR;

namespace Shared.Events;

// INotification is the MediatR interface that marks this as a broadcast event
public record UserRegisteredIntegrationEvent(Guid UserId, string Email) : INotification;