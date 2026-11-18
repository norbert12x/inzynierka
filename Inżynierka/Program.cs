using Inżynierka.Configuration;
using Inżynierka.Data;
using Inżynierka.Infrastructure;
using Inżynierka.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inżynierka
{
    public class Program
    {
        // Wikimedia wymaga opisowego User-Agent z kontaktem do projektu.
        private const string WikimediaUserAgent =
            "InzynierkaPhotoBot/0.1 (https://github.com/norbert12x/inzynierka; catalog photo research)";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Sekrety w Development z .local/ (ignorowane przez Git); zmienne środowiskowe mają pierwszeństwo.
            if (builder.Environment.IsDevelopment())
            {
                var local = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".local"));
                builder.Configuration.AddJsonFile(Path.Combine(local, "supabase.json"), optional: true, reloadOnChange: false);
                var geoapifyKeyFile = Path.Combine(local, "geoapify-key.txt");
                if (string.IsNullOrWhiteSpace(builder.Configuration["Geoapify:ApiKey"]) && File.Exists(geoapifyKeyFile))
                    builder.Configuration["Geoapify:ApiKey"] = File.ReadAllText(geoapifyKeyFile).Trim();
                builder.Configuration.AddEnvironmentVariables();
            }

            // Baza jest wymagana – bez konfiguracji aplikacja nie startuje (zamiast 503 w każdym endpoincie).
            var databaseConnection = CatalogDatabaseConfiguration.GetConnectionString(
                    builder.Configuration, builder.Environment.ContentRootPath)
                ?? throw new InvalidOperationException(
                    "Brak konfiguracji bazy: uzupełnij .local/supabase.json (sekcja Database) albo zmienne Database__Host, Database__Username, Database__Password.");
            builder.Services.AddDbContext<CatalogDbContext>(options => options.UseCatalogDatabase(databaseConnection));

            // Obszary katalogu z konfiguracji; błędna sekcja zatrzymuje start aplikacji.
            builder.Services.AddOptions<CatalogOptions>()
                .Bind(builder.Configuration.GetSection(CatalogOptions.SectionName))
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<CatalogOptions>, CatalogOptionsValidator>();

            builder.Services.AddControllers();
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
            {
                Title = "Atrakcje Podkarpacia — API",
                Version = "v1",
                Description = "Katalog atrakcji (Supabase), wyszukiwanie lokalizacji, zdjęcia, obszary oraz narzędzia redakcyjne /api/dev (tylko Development)."
            }));

            // Usługi zewnętrzne.
            builder.Services.AddHttpClient<GeoapifyGeocodingService>(client =>
            {
                client.BaseAddress = new Uri("https://api.geoapify.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
            }).RemoveAllLoggers(); // URL Geoapify zawiera klucz API i adres użytkownika.
            // Wikimedia i Overture – tylko narzędzia Development (/dev/zdjecia, /dev/atrakcje).
            builder.Services.AddHttpClient<PhotoDownloadService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(WikimediaUserAgent);
            });
            builder.Services.AddHttpClient<WikimediaCommonsService>(client =>
            {
                client.BaseAddress = new Uri("https://commons.wikimedia.org/");
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(WikimediaUserAgent);
            });
            builder.Services.AddSingleton<OvertureCandidatesStore>();

            var app = builder.Build();

            app.UseExceptionHandler();
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
            {
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.MapControllers();

            if (app.Environment.IsDevelopment())
                MapDevelopmentTools(app);

            app.Run();
        }

        private static void MapDevelopmentTools(WebApplication app)
        {
            // Szybki test połączenia w Swaggerze.
            app.MapGet("/api/database/status", async (CatalogDbContext database, CancellationToken cancellationToken) =>
                    Results.Ok(new
                    {
                        connected = true,
                        schema = "catalog",
                        placesCount = await database.Places.CountAsync(cancellationToken),
                        categoriesCount = await database.Categories.CountAsync(cancellationToken)
                    }))
                .WithTags("Database");

            // Strony narzędzi redakcyjnych (DevTools/*.html). Celowo poza wwwroot: statyczne pliki byłyby podawane
            // skompresowane, a skrypt odświeżania Visual Studio doklejony do takiej odpowiedzi ją psuje.
            foreach (var page in new[] { "zdjecia", "atrakcje" })
                app.MapGet($"/dev/{page}", (IWebHostEnvironment environment) =>
                        Results.File(Path.Combine(environment.ContentRootPath, "DevTools", $"{page}.html"), "text/html; charset=utf-8"))
                    .ExcludeFromDescription();
        }
    }
}
