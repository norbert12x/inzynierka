using Inżynierka.Data;
using Microsoft.EntityFrameworkCore;

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
                // Zmienne środowiskowe mają pierwszeństwo przed lokalnym plikiem.
                builder.Configuration.AddEnvironmentVariables();
            }

            var databaseConnection = CatalogDatabaseConfiguration.GetConnectionString(
                builder.Configuration, builder.Environment.ContentRootPath);
            if (databaseConnection is not null)
            {
                builder.Services.AddDbContext<CatalogDbContext>(options =>
                    options.UseNpgsql(databaseConnection, postgres => postgres.UseNetTopologySuite()));
            }

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpClient<Services.OverpassAttractionService>(client =>
            {
                client.BaseAddress = new Uri("https://overpass-api.de/api/");
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PodkarpacieThesis/0.1");
            });
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new()
                {
                    Title = "Atrakcje Podkarpacia — API",
                    Version = "v1",
                    Description = "Kategorie atrakcji i próbne pobieranie danych OSM dla Rzeszowa."
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
            }

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            app.Run();
        }
    }
}
