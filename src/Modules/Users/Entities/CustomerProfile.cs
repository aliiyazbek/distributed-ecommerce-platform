namespace Modules.Users.Entities;

public class CustomerProfile
{
    public Guid UserId { get; private set; } // Acts as both Primary Key and Foreign Key
    
    // Made nullable because users usually don't provide shipping addresses at the exact moment of registration
    public string? ShippingAddress { get; private set; } 
    
    public User User { get; private set; } = null!;

    protected CustomerProfile() { } // Required by EF Core

    public CustomerProfile(Guid userId)
    {
        UserId = userId;
    }

    // Domain Method to mutate state safely
    public void UpdateShippingAddress(string address)
    {
        ShippingAddress = address;
    }
}