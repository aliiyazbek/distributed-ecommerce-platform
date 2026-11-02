using Shared;

namespace Modules.Users.Entities;

public class User : BaseEntity
{
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public int RoleId { get; private set; }
    public Role Role { get; private set; } = null!;

    // 1-to-1 Composition Navigation Properties
    public CustomerProfile? CustomerProfile { get; private set; }
    public AdminProfile? AdminProfile { get; private set; }

    public DateTime CreatedAt { get; private set; }

    protected User() { } 

    public User(string email, string passwordHash, int roleId)  
    {
        // Notice we don't set Id or CreatedAt here anymore. 
        // We let the hidden constructor inside BaseEntity handle that infrastructure logic!
        Email = email;
        PasswordHash = passwordHash;
        RoleId = roleId;
    }

    // DDD Encapsulation: Methods to attach profiles safely
    public void SetCustomerProfile(CustomerProfile profile)
    {
        CustomerProfile = profile;
    }

    public void SetAdminProfile(AdminProfile profile)
    {
        AdminProfile = profile;
    }
}