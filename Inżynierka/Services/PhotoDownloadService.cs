namespace Inżynierka.Services;

// Pobiera plik zdjęcia z Wikimedia na lokalny dysk – te same zasady co pierwszy import (scripts/archiwum/fetch_place_photos.py):
// tylko hosty Wikimedia, maks. 8 MB, JPEG/PNG/WebP, ponowienie przy 429/5xx, zapis przez plik tymczasowy.
public sealed class PhotoDownloadService(HttpClient client)
{
    public const long MaxBytes = 8 * 1024 * 1024;

    private static readonly HashSet<string> AllowedHosts =
        new(["upload.wikimedia.org", "commons.wikimedia.org", "thumb.wikimedia.org"], StringComparer.OrdinalIgnoreCase);

    // Zwraca null przy sukcesie albo polski opis problemu.
    public async Task<string?> DownloadAsync(string url, string destination, string expectedContentType,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host))
            return "Adres spoza Wikimedia – pomijam.";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is 429 or 502 or 503 or 504 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                return $"Wikimedia zwróciło HTTP {(int)response.StatusCode}.";

            var finalHost = response.RequestMessage?.RequestUri?.Host;
            if (finalHost is null || !AllowedHosts.Contains(finalHost))
                return "Przekierowanie poza Wikimedia – pomijam.";

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != expectedContentType)
                return $"Nieoczekiwany format ({contentType ?? "brak"}), oczekiwano {expectedContentType}.";
            if (response.Content.Headers.ContentLength > MaxBytes)
                return "Plik większy niż 8 MB.";

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".tmp";
            try
            {
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var target = File.Create(temporary))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > MaxBytes)
                            return "Plik większy niż 8 MB.";
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                File.Move(temporary, destination, overwrite: false);
                return null;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        return "Wikimedia ogranicza zapytania – spróbuj później.";
    }
}
