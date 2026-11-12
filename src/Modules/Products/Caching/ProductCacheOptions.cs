namespace Modules.Products.Caching;

// Bound from the "Caching" section of appsettings.json
public sealed class ProductCacheOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; } = true;
    public int ProductTtlSeconds { get; set; } = 300;     // single product: 5 minutes
    public int ProductListTtlSeconds { get; set; } = 60;  // list pages: 1 minute
}
