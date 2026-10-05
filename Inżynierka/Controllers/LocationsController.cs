using System.Text.Json;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/locations")]
public sealed class LocationsController(GeoapifyGeocodingService service, ILogger<LocationsController> logger) : ControllerBase
{
    [HttpGet("search")]
    [ProducesResponseType(typeof(LocationSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<LocationSearchResponse>> Search(
        [FromQuery] LocationSearchQuery parameters, CancellationToken cancellationToken)
    {
        var text = parameters.Text.Trim();
        if (text.Length < 2)
        {
            ModelState.AddModelError(nameof(parameters.Text), "Wpisz przynajmniej dwa znaki adresu lub miasta.");
            return ValidationProblem(ModelState);
        }
        if (!service.IsConfigured)
            return Problem(statusCode: 503, title: "Brak konfiguracji Geoapify.",
                detail: "Ustaw klucz Geoapify i uruchom aplikację ponownie.");

        try
        {
            return Ok(await service.SearchAsync(text, parameters.Type, cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: 504, title: "Przekroczono czas wyszukiwania lokalizacji.");
        }
        catch (HttpRequestException exception)
        {
            // Nie logujemy adresu zapytania, klucza ani treści wyjątku dostawcy.
            logger.LogWarning("Geokodowanie nie powiodło się (HTTP={StatusCode}).", (int?)exception.StatusCode);
            return Problem(statusCode: 503, title: "Wyszukiwanie lokalizacji jest chwilowo niedostępne.",
                detail: "Sprawdź dostępność Geoapify, klucz API i jego limit zapytań.");
        }
        catch (JsonException)
        {
            logger.LogWarning("Geoapify zwrócił nieprawidłową odpowiedź geokodowania.");
            return Problem(statusCode: 502, title: "Dostawca zwrócił nieprawidłowe dane lokalizacji.");
        }
    }
}
