using Microsoft.EntityFrameworkCore;
using Modules.Users.Entities;
using Modules.Users.Interfaces;

namespace Modules.Users.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly UsersDbContext _dbContext;

    public UserRepository(UsersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // The repository encapsulates the EF Core specific rules, like .Include()
        return await _dbContext.Users
            .Include(u => u.Role)
            .Include(u => u.CustomerProfile)
            .Include(u => u.AdminProfile)
            .SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken);
    }

    // Add this implementation
    public async Task LoadRoleAsync(User user, CancellationToken cancellationToken = default)
    {
        await _dbContext.Entry(user).Reference(u => u.Role).LoadAsync(cancellationToken);
    }

    public void Add(User user)
    {
        _dbContext.Users.Add(user);
    }
}