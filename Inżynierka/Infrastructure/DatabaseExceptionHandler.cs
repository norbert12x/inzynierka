using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Infrastructure;

// Jedno miejsce obsługi błędów bazy dla całego API: zamiast try/catch w każdym endpoincie
// niedostępna baza, przekroczony czas zapytania albo nieudany zapis dają 503 z polskim opisem.
// Treść wyjątku i parametry połączenia nie trafiają do odpowiedzi.
public sealed class DatabaseExceptionHandler(IProblemDetailsService problemDetails, ILogger<DatabaseExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Klient przerwał żądanie – nie ma komu odpowiadać.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return true;
        if (!IsDatabaseFailure(exception))
            return false;

        logger.LogWarning("Błąd bazy przy {Path} ({ExceptionType}, SQLSTATE={SqlState}).", httpContext.Request.Path,
            exception.GetType().Name, (exception.GetBaseException() as Npgsql.PostgresException)?.SqlState);
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Baza danych jest chwilowo niedostępna.",
                Detail = "Sprawdź połączenie z Supabase (projekt mógł zostać uśpiony) i spróbuj ponownie."
            }
        });
    }

    public static bool IsDatabaseFailure(Exception exception)
        => exception is Npgsql.NpgsqlException or TimeoutException or DbUpdateException
            || exception is InvalidOperationException { InnerException: Npgsql.NpgsqlException or TimeoutException }
            // Przekroczony czas polecenia w EF/Npgsql bywa zgłaszany jako anulowanie bez prośby klienta.
            || exception is OperationCanceledException { InnerException: TimeoutException or Npgsql.NpgsqlException };
}
