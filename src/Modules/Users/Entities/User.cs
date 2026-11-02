using System.ComponentModel;
using Shared;

namespace Modules.Users.Entities;


public class User : BaseEntity
{
    public string Email {get;private set;}
    public string PasswordHash {get;private set;}
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // EF Core constructor 
    private User(){}

public User(string email, string passwordHash, int roleId)
    {
        //Id = Guid.NewGuid(); // Or remove this line if BaseEntity assigns the Id automatically
        Email = email;
        PasswordHash = passwordHash;
        RoleId = roleId;
        CreatedAt = DateTime.UtcNow;
    }
}