# Atrakcje Podkarpacia

Aplikacja webowa do odkrywania atrakcji turystycznych i planowania jedno- oraz wielodniowych wyjazdów na Podkarpaciu. Projekt powstaje w ramach pracy inżynierskiej; pierwsza wersja obejmuje Rzeszów i okolice, a kolejne obszary można dodawać bez zmian w kodzie.

## Czym jest aplikacja

Użytkownik wskazuje, gdzie będzie — miasto, okolice albo adres noclegu — oraz daty wyjazdu i swoje zainteresowania. Aplikacja proponuje atrakcje i układa plan każdego dnia: kolejność zwiedzania, godziny wizyt, czasy przejazdów i przerwę na posiłek. Plan można poprawiać ręcznie, a każdy odcinek otworzyć w Google Maps do nawigacji.

Najważniejsze funkcje:

- **Katalog atrakcji** — lista i mapa z filtrami: miasto lub promień od wybranego punktu, kategoria, nazwa; szczegóły atrakcji ze zdjęciami, godzinami otwarcia i oceną użytkowników.
- **Planer wyjazdu** — jeden lub wiele dni, inny nocleg i godziny aktywności każdego dnia, atrakcje obowiązkowe i opcjonalne; kolejność optymalizowana pod czas przemieszczania (samochód i spacer). Dodatkowe propozycje trafiają do planu dopiero po akceptacji użytkownika.
- **Edycja planu** — zmiana kolejności, przenoszenie atrakcji między dniami, zmiana czasu wizyty; aplikacja przelicza godziny i ostrzega o możliwych konfliktach z godzinami otwarcia.
- **Konta** — plan można przygotować bez logowania; konto pozwala zapisywać plany, wysłać harmonogram e-mailem, oceniać atrakcje i zgłaszać błędy w danych.
- **Panel administratora** — edycja katalogu, zatwierdzanie zdjęć, moderacja opinii i obsługa zgłoszeń.

Aplikacja nie zgaduje brakujących danych: nieznane godziny otwarcia są pokazywane jako nieznane, a brakujące zdjęcie nie jest zastępowane zdjęciem innego miejsca.

## Stos technologiczny

| Warstwa | Technologie |
|---|---|
| Backend | ASP.NET Core 9 (Web API, Swagger), ASP.NET Core Identity |
| Baza danych | PostgreSQL + PostGIS w Supabase, EF Core + Npgsql + NetTopologySuite |
| Frontend | React + Vite (TypeScript), Leaflet z mapą OpenStreetMap |
| Usługi zewnętrzne | Geoapify (wyszukiwanie adresów, dane o miejscach), OpenRouteService (czasy przejazdów), Wikimedia Commons (zdjęcia), SMTP + MailKit (e-mail) |
| Dane o atrakcjach | OpenStreetMap (przez Geoapify) i Overture Maps Places, z ochroną przed duplikatami |
| Testy | xUnit |
| Przygotowanie danych | Skrypty Python (DuckDB) |

## Uruchomienie

Wymagania: .NET 9 SDK (lub nowszy z runtime .NET 9), Visual Studio 2022 albo `dotnet` CLI oraz projekt Supabase ze schematem z katalogu `database/`.

1. **Konfiguracja lokalna.** W katalogu głównym repozytorium utwórz folder `.local/` (jest ignorowany przez Git) i umieść w nim:
   - `supabase.json` — sekcja `Database` z polami `Host`, `Port`, `Name`, `Username`, `Password`, `RootCertificate`,
   - `supabase-ca.crt` — certyfikat SSL Supabase,
   - `geoapify-key.txt` — klucz API Geoapify,
   - `photo-cache/` — pliki zdjęć atrakcji.

   Zamiast plików można użyć zmiennych środowiskowych, np. `Database__Password` albo `Geoapify__ApiKey`. Bez konfiguracji bazy aplikacja nie wystartuje. Szczegóły połączenia z bazą: [database/README.md](database/README.md).

2. **Backend.** Otwórz `Inżynierka.sln` w Visual Studio, wybierz profil `http` i uruchom (F5), albo z wiersza poleceń:

   ```powershell
   dotnet run --project .\Inżynierka\Inżynierka.csproj --launch-profile http
   ```

   Swagger: `http://localhost:5201/swagger`. Połączenie z bazą sprawdza `GET /api/database/status`.

3. **Testy:**

   ```powershell
   dotnet test
   ```

Darmowy projekt Supabase usypia się po około tygodniu bez użycia — wtedy przywróć go w panelu Supabase (**Restore project**).

## Dokumentacja

Opis wymagań, plan prac i opis API znajdują się w katalogu [docs/](docs/).

## Źródła danych

Dane o miejscach © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright) (licencja ODbL), udostępnione przez [Geoapify](https://www.geoapify.com/), oraz z [Overture Maps Foundation](https://docs.overturemaps.org/attribution/) (m.in. dane od Meta i Microsoft na licencji CDLA Permissive 2.0, Foursquare na Apache 2.0) — źródło i licencja są zapisane przy każdym miejscu. Zdjęcia pochodzą z [Wikimedia Commons](https://commons.wikimedia.org/) — autor i licencja są podane przy każdym zdjęciu.
