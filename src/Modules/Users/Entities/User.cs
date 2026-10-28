using System.ComponentModel;
using Shared;

namespace Modules.Users.Entities;


public class User : BaseEntity
{
    public string Email {get;private set;}
    public string PasswordHash {get;private set;}
    public string Role {get;private set;} //for example customer or admin

    // EF Core constructor 
    private User(){}

    public User(string email, string passwordHash,string role = "Customer")
    {
        Email=email;
        PasswordHash = passwordHash;
        Role=role;
    }

}