using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inżynierka.Data;

// Używane tylko przez narzędzia EF (Add-Migration -Context AccountsDbContext, Update-Database -Context AccountsDbContext).
public sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        options.UseAccountsDatabase(DesignTimeDatabase.GetConnectionString());
        return new AccountsDbContext(options.Options);
    }
}
