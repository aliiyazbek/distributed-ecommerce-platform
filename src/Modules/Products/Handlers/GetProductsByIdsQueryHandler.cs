using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Products.Persistence;
using Shared.Queries;

namespace Modules.Products.Handlers;

public class GetProductsByIdsQueryHandler : IRequestHandler<GetProductsByIdsQuery, List<ProductSummary>>
{
    private readonly ProductsDbContext _context;

    public GetProductsByIdsQueryHandler(ProductsDbContext context)
    {
        _context = context;
    }

    public async Task<List<ProductSummary>> Handle(GetProductsByIdsQuery request, CancellationToken cancellationToken)
    {
        return await _context.Products
            .AsNoTracking()
            .Where(p => request.ProductIds.Contains(p.Id))
            .Select(p => new ProductSummary(p.Id, p.Name, p.Price))
            .ToListAsync(cancellationToken);
    }
}
