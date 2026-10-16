using Microsoft.AspNetCore.Identity;

namespace ReadNest.Infrastructure.Identity;

/// <summary>
/// The ASP.NET Core Identity account. Reading data lives in domain entities keyed by this user's id; profile
/// information shown to other users lives in <see cref="Domain.Entities.UserProfile"/>.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }
}
