using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inżynierka.Data;

public static class CatalogDatabaseConfiguration
{
    // Tabela historii migracji EF w schemacie catalog (nie public – public jest wystawiony przez Data API Supabase).
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string MigrationsHistorySchema = "catalog";

    // Wspólne ustawienia dla aplikacji i narzędzi EF (Add-Migration, Update-Database).
    public static DbContextOptionsBuilder UseCatalogDatabase(this DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, postgres => postgres
            .UseNetTopologySuite()
            .MigrationsHistoryTable(MigrationsHistoryTable, MigrationsHistorySchema));

    public static string? GetConnectionString(IConfiguration configuration, string contentRootPath)
    {
        var section = configuration.GetSection("Database");
        var host = section["Host"];
        var username = section["Username"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return null;

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = section.GetValue("Port", 5432),
            Database = section["Name"] ?? "postgres",
            Username = username,
            Password = password,
            SslMode = SslMode.VerifyFull,
            Timeout = 15,
            CommandTimeout = 30,
            MaxPoolSize = 5,
            IncludeErrorDetail = false,
            ApplicationName = "PodkarpacieThesis",
            SearchPath = "catalog,extensions,public"
        };
        var certificatePath = section["RootCertificate"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
            connection.RootCertificate = Path.GetFullPath(Path.Combine(contentRootPath, certificatePath));
        return connection.ConnectionString;
    }
}
