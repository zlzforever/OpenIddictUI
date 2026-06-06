using Microsoft.AspNetCore.Identity;

namespace OpenIddictUI.Identity;

public class User : IdentityUser
{
    public bool IsDeleted { get; set; }
}
