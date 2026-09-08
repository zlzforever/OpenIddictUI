using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddictUI.Identity;
using OpenIddictUI.Options;

namespace OpenIddictUI.Data;

public sealed class MySqlAppDbContext(
    DbContextOptions<MySqlAppDbContext> options,
    IOptions<IdentityExtensionOptions> identityOptions)
    : AppDbContext(options, identityOptions)
{
}
