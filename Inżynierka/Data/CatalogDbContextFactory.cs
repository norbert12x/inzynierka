using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inżynierka.Data;

// Używane tylko przez narzędzia EF (Add-Migration -Context CatalogDbContext, Update-Database -Context CatalogDbContext).
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogDatabase(DesignTimeDatabase.GetConnectionString());
        return new CatalogDbContext(options.Options);
    }
}
