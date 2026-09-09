using Microsoft.EntityFrameworkCore.Design;

namespace OpenIddictUI.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var scope = Program.CreateWebApplication(args).Services;
        return scope.GetRequiredService<AppDbContext>();
    }
}