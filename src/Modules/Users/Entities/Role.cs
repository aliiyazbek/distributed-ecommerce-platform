namespace Modules.Users.Entities;

public class Role
{
    public int Id { get; set; } // int is standard and faster for lookup tables
    public string Name { get; set; } = string.Empty; // e.g., "Admin", "Customer"

    // Navigation property for Entity Framework
    public ICollection<User> Users { get; set; } = new List<User>();
}