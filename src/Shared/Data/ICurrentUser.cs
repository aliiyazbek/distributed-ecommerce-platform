namespace Shared.Data;

// The signed-in user of the current request, without data code depending on HttpContext.
public interface ICurrentUser
{
    Guid? UserId { get; }
}

// Names of "consistency scopes": a write is remembered under one or more scopes, and a read
// that must see those writes asks for the same scopes.
public static class ConsistencyScopes
{
    // Every write is remembered for the user who made it ("read your own writes").
    public static string User(Guid userId) => $"user:{userId:N}";
}
