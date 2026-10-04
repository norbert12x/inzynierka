using System.Text.Json;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/attractions")]
public class AttractionsController(
    OverpassAttractionService service,
    ILogger<AttractionsController> logger) : ControllerBase
{
    [HttpGet("preview/rzeszow")]
    [ProducesResponseType(typeof(AttractionPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<AttractionPreviewResponse>> PreviewRzeszow(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetRzeszowAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Przekroczono czas oczekiwania na dane atrakcji.",
                detail: "Serwis OpenStreetMap/Overpass nie odpowiedział w ciągu 60 sekund. Spróbuj ponownie później.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Nie udało się pobrać atrakcji z Overpass.");
            var upstreamStatus = (int?)exception.StatusCode;
            var (status, title, detail) = upstreamStatus switch
            {
                504 => (StatusCodes.Status504GatewayTimeout,
                    "Serwer Overpass przekroczył czas obsługi zapytania.",
                    "Dostawca zwrócił HTTP 504. Może być przeciążony. Odczekaj około minuty i ponów próbę."),
                429 => (StatusCodes.Status503ServiceUnavailable,
                    "Serwer Overpass ograniczył liczbę zapytań.",
                    "Dostawca zwrócił HTTP 429. Odczekaj przed kolejną próbą; nie wysyłaj kilku zapytań jednocześnie."),
                502 or 503 => (StatusCodes.Status503ServiceUnavailable,
                    "Serwer Overpass jest chwilowo niedostępny.",
                    $"Dostawca zwrócił HTTP {upstreamStatus}. Spróbuj ponownie później."),
                null => (StatusCodes.Status503ServiceUnavailable,
                    "Nie udało się połączyć z serwerem Overpass.",
                    "Nie otrzymano odpowiedzi HTTP. Sprawdź połączenie z internetem; szczegóły błędu są w logach aplikacji."),
                _ => (StatusCodes.Status502BadGateway,
                    "Serwer Overpass odrzucił zapytanie.",
                    $"Dostawca zwrócił HTTP {upstreamStatus}. Szczegóły błędu są w logach aplikacji.")
            };

            return Problem(statusCode: status, title: title, detail: detail,
                extensions: new Dictionary<string, object?>
                {
                    ["upstreamStatusCode"] = upstreamStatus,
                    ["connectionError"] = upstreamStatus is null ? exception.HttpRequestError.ToString() : null
                });
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "Niepoprawna odpowiedź Overpass.");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Źródło danych zwróciło niepoprawną lub niepełną odpowiedź.",
                detail: "Nie prezentujemy częściowych wyników jako kompletnego pobrania. Spróbuj ponownie później.");
        }
    }
}
