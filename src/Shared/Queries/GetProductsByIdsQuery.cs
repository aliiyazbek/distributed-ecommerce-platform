using MediatR;

namespace Shared.Queries;

public record ProductSummary(Guid Id, string Name, decimal Price, int AvailableQuantity);

public record GetProductsByIdsQuery(IReadOnlyCollection<Guid> ProductIds) : IRequest<List<ProductSummary>>;
