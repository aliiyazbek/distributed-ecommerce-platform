using MediatR;

namespace Shared.Commands;

// Dictionary maps ProductId -> Quantity to deduct
public record ReserveStockCommand(Dictionary<Guid, int> Items) : IRequest<(bool Success, string? Error)>;