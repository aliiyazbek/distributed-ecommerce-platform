using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Products.Persistence;
using Shared.Commands;

namespace Modules.Products.Handlers;

public class ReserveStockCommandHandler : IRequestHandler<ReserveStockCommand, (bool Success, string? Error)>
{
    private readonly ProductsDbContext _dbContext;

    public ReserveStockCommandHandler(ProductsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(bool Success, string? Error)> Handle(ReserveStockCommand request, CancellationToken cancellationToken)
    {
        // 1. Open an explicit database transaction
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var item in request.Items)
            {
                var productId = item.Key;
                var requestedQuantity = item.Value;

                // 2. Fetch and LOCK the specific row in PostgreSQL
                var product = await _dbContext.Products
                    .FromSqlRaw("SELECT * FROM products.\"Products\" WHERE \"Id\" = {0} FOR UPDATE", productId)
                    .SingleOrDefaultAsync(cancellationToken);

                if (product == null)
                    return (false, $"Product {productId} not found.");

                // 3. Validate against the locked state
                if (product.AvailableQuantity < requestedQuantity)
                    return (false, $"Concurrency constraint: Product {product.Name} has insufficient stock. Requested: {requestedQuantity}, Available: {product.AvailableQuantity}.");

                // 4. Mutate
                product.UpdateQuantity(product.AvailableQuantity - requestedQuantity);
            }

            // 5. Save changes and release the locks
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            return (true, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (false, $"A database error occurred during stock reservation: {ex.Message}");
        }
    }
}