using Inżynierka.Configuration;
using Inżynierka.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inżynierka
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            if (builder.Environment.IsDevelopment())
            {
                var localSettings = Path.GetFullPath(Path.Combine(
                    builder.Environment.ContentRootPath, "..", ".local", "supabase.json"));
                builder.Configuration.AddJsonFile(localSettings, optional: true, reloadOnChange: false);
                var geoapifyKeyFile = Path.GetFullPath(Path.Combine(
                    builder.Environment.ContentRootPath, "..", ".local", "geoapify-key.txt"));
                if (string.IsNullOrWhiteSpace(builder.Configuration["Geoapify:ApiKey"]) && File.Exists(geoapifyKeyFile))
                    builder.Configuration["Geoapify:ApiKey"] = File.ReadAllText(geoapifyKeyFile).Trim();
                // Zmienne środowiskowe mają pierwszeństwo przed lokalnym plikiem.
                builder.Configuration.AddEnvironmentVariables();
            }

            var databaseConnection = CatalogDatabaseConfiguration.GetConnectionString(
                builder.Configuration, builder.Environment.ContentRootPath);
            if (databaseConnection is not null)
            {
                builder.Services.AddDbContext<CatalogDbContext>(options =>
                    options.UseCatalogDatabase(databaseConnection));
            }

            // Obszary katalogu z konfiguracji; błędna sekcja zatrzymuje start aplikacji.
            builder.Services.AddOptions<CatalogOptions>()
                .Bind(builder.Configuration.GetSection(CatalogOptions.SectionName))
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<CatalogOptions>, CatalogOptionsValidator>();

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpClient<Services.GeoapifyGeocodingService>(client =>
            {
                client.BaseAddress = new Uri("https://api.geoapify.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
            }).RemoveAllLoggers(); // URL Geoapify zawiera klucz API i adres użytkownika.
            // Wikimedia (tylko narzędzia Development /api/dev/...): pobieranie plików i wyszukiwanie w Commons.
            // Wikimedia wymaga opisowego User-Agent z kontaktem do projektu.
            const string wikimediaUserAgent = "InzynierkaPhotoBot/0.1 (https://github.com/norbert12x/inzynierka; catalog photo research)";
            builder.Services.AddHttpClient<Services.PhotoDownloadService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(wikimediaUserAgent);
            });
            builder.Services.AddHttpClient<Services.WikimediaCommonsService>(client =>
            {
                client.BaseAddress = new Uri("https://commons.wikimedia.org/");
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(wikimediaUserAgent);
            });
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new()
                {
                    Title = "Atrakcje Podkarpacia — API",
                    Version = "v1",
                    Description = "Katalog atrakcji z Supabase, wyszukiwanie miasta lub adresu, filtry miasta i promienia, szczegóły atrakcji, zdjęcia i obszary."
                });
            });

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllers();

            // Endpoint diagnostyczny wyłącznie do lokalnego sprawdzenia w Swaggerze.
            if (app.Environment.IsDevelopment())
            {
                app.MapGet("/api/database/status", async Task<IResult> (IServiceProvider services, CancellationToken cancellationToken) =>
                {
                    if (databaseConnection is null)
                        return Results.Problem(statusCode: 503, title: "Brak konfiguracji Supabase.",
                            detail: "Uzupełnij .local/supabase.json i uruchom aplikację ponownie.");

                    try
                    {
                        var database = services.GetRequiredService<CatalogDbContext>();
                        var placesCount = await database.Places.CountAsync(cancellationToken);
                        var categoriesCount = await database.Categories.CountAsync(cancellationToken);
                        return Results.Ok(new { connected = true, schema = "catalog", placesCount, categoriesCount });
                    }
                    catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException)
                    {
                        // Nie zwracamy treści wyjątku ani parametrów połączenia do Swaggera.
                        app.Logger.LogWarning("Sprawdzenie bazy nie powiodło się ({ExceptionType}, SQLSTATE={SqlState}).",
                            exception.GetType().Name, (exception as Npgsql.PostgresException)?.SqlState);
                        return Results.Problem(statusCode: 503, title: "Nie udało się odczytać katalogu z Supabase.",
                            detail: "Sprawdź konfigurację, dostępność bazy i wykonanie skryptu 001_catalog_schema.sql.");
                    }
                }).WithTags("Database")
                  .Produces(StatusCodes.Status200OK)
                  .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

                // Strona przeglądu zdjęć (DevTools/zdjecia.html). Celowo poza wwwroot: MapStaticAssets podaje HTML
                // skompresowany gzipem, a skrypt odświeżania Visual Studio doklejony do takiej odpowiedzi ją psuje.
                app.MapGet("/dev/zdjecia", (IWebHostEnvironment environment) =>
                        Results.File(Path.Combine(environment.ContentRootPath, "DevTools", "zdjecia.html"), "text/html; charset=utf-8"))
                    .ExcludeFromDescription();
            }

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            app.Run();
        }
    }
}
