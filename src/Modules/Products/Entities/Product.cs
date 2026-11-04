namespace Modules.Products.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty; // For storing the uploaded image path
    public int AvailableQuantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    public void UpdateQuantity(int newQuantity)
    {
        if (newQuantity < 0)
            throw new InvalidOperationException("Stock quantity cannot be negative.");
            
        AvailableQuantity = newQuantity;
    }
}