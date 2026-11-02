using Modules.Users.Entities;

namespace Modules.Users.Interfaces;

public interface IUserRepository
{
    // Notice we do NOT return IQueryable. We return concrete Tasks.
    // Returning IQueryable is a Leaky Abstraction because it forces the caller to execute the database query.
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    // Add this to the interface
    Task LoadRoleAsync(User user, CancellationToken cancellationToken = default);
    void Add(User user);
    
    // We don't include SaveChanges here. Saving is the responsibility of the Unit of Work, not the Repository.
}