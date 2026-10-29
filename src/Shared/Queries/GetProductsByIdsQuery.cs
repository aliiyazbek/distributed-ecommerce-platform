using MediatR;

namespace Shared.Queries;

public record ProductSummary(Guid Id, string Name, decimal Price);

public record GetProductsByIdsQuery(IReadOnlyCollection<Guid> ProductIds) : IRequest<List<ProductSummary>>;
