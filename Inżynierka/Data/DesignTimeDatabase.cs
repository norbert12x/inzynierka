namespace Inżynierka.Data;

// Wspólna część fabryk dla narzędzi EF (Add-Migration, Update-Database, Script-Migration).
// Czyta tę samą konfigurację co aplikacja w Development: appsettings, .local/supabase.json, zmienne środowiskowe.
internal static class DesignTimeDatabase
{
    public static string GetConnectionString()
    {
        var projectDirectory = FindProjectDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile(Path.Combine(projectDirectory, "..", ".local", "supabase.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Bez konfiguracji Add-Migration nadal działa (nie łączy się z bazą); Update-Database zgłosi błąd połączenia.
        return CatalogDatabaseConfiguration.GetConnectionString(configuration, projectDirectory)
            ?? "Host=localhost;Database=postgres;Username=postgres;Password=brak-konfiguracji";
    }

    // Katalog z Inżynierka.csproj – narzędzia EF mogą startować z różnych katalogów roboczych.
    private static string FindProjectDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inżynierka.csproj")))
                    return directory.FullName;
                var nested = Path.Combine(directory.FullName, "Inżynierka");
                if (File.Exists(Path.Combine(nested, "Inżynierka.csproj")))
                    return nested;
            }
        }

        throw new InvalidOperationException("Nie znaleziono katalogu projektu Inżynierka.csproj.");
    }
}
