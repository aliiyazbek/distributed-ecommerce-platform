namespace Modules.Users.Entities;

public class AdminProfile
{
    // In Composition, the Primary Key is strictly shared with the Parent's Id
    public Guid UserId { get; private set; }
    public string DepartmentName { get; private set; }
    
    // Navigation property back to the Aggregate Root
    public User User { get; private set; } = null!;

    // Required by Entity Framework Core for reflection-based materialization
    protected AdminProfile() { } 

    public AdminProfile(Guid userId, string departmentName)
    {
        UserId = userId;
        DepartmentName = departmentName;
    }
}