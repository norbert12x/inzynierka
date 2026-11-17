# Atrakcje Podkarpacia

Aplikacja webowa do odkrywania atrakcji turystycznych i planowania jedno- oraz wielodniowych wyjazdów na Podkarpaciu. Projekt powstaje w ramach pracy inżynierskiej; pierwsza wersja obejmuje **Rzeszów i okolice**.

Użytkownik wskazuje miasto lub adres noclegu, daty i zainteresowania, a aplikacja proponuje atrakcje i układa plan dni: kolejność zwiedzania, czasy wizyt, przejazdy i przerwy na posiłek, z linkami do nawigacji w Google Maps.

## Stan prac

| Obszar | Stan |
|---|---|
| Katalog atrakcji (lista, filtry, promień, szczegóły, kategorie) | ✅ |
| Wyszukiwanie miasta / adresu (Geoapify) | ✅ |
| Zdjęcia z autorem i licencją (Wikimedia Commons) | ✅ API, 🔶 przegląd zdjęć |
| Obszary katalogu w konfiguracji | ✅ |
| Konta i role (gość, użytkownik, administrator) | ⬜ |
| Czasy przejazdów (OpenRouteService) | ⬜ |
| Planer jedno- i wielodniowy | ⬜ |
| Zapis i edycja planów | ⬜ |
| Interfejs z mapą (React) | ⬜ |
| Opinie, zgłoszenia, wysyłka planu e-mailem | ⬜ |

Szczegóły i harmonogram do 31 stycznia 2027: [docs/plan-prac.md](docs/plan-prac.md).

## Technologie

- **Backend:** ASP.NET Core 9, kontrolery API, Swagger
- **Baza:** Supabase (PostgreSQL + PostGIS), EF Core + Npgsql + NetTopologySuite
- **Dane:** OpenStreetMap przez Geoapify, zdjęcia z Wikimedia Commons, skrypty Python
- **Planowane:** ASP.NET Core Identity, OpenRouteService, MailKit, React + Vite + Leaflet
- **Testy:** xUnit

## Struktura repozytorium

| Ścieżka | Zawartość |
|---|---|
| `Inżynierka/` | Backend: `Controllers/`, `Models/`, `Data/` (EF Core), `Services/` (integracje), `Configuration/`, `Planning/` (logika planera) |
| `Inżynierka.Tests/` | Testy xUnit |
| `database/` | Schemat i import bazy jako kolejne pliki SQL — [opis](database/README.md) |
| `scripts/` | Pobieranie, selekcja i import danych, zdjęcia |
| `data/` | Pobrane dane, raporty jakości i decyzje selekcji — [opis](data/README.md) |
| `docs/` | Dokumentacja projektu |

## Uruchomienie

Wymagania: .NET 9 SDK (lub nowszy) z runtime .NET 9, Visual Studio 2022 albo `dotnet` CLI, projekt Supabase z wykonanymi skryptami z `database/`.

1. **Konfiguracja lokalna** — w katalogu głównym repozytorium utwórz folder `.local/` (ignorowany przez Git):
   - `supabase.json` — sekcja `Database`: `Host`, `Port`, `Name`, `Username`, `Password`, `RootCertificate` (szczegóły w [database/README.md](database/README.md)),
   - `supabase-ca.crt` — certyfikat Supabase,
   - `geoapify-key.txt` — klucz Geoapify (wyszukiwanie adresów),
   - `photo-cache/` — pliki zdjęć.

   Zamiast plików można użyć zmiennych środowiskowych, np. `Database__Password`, `Geoapify__ApiKey`.
2. **Uruchomienie** — otwórz `Inżynierka.sln`, wybierz profil `http` i naciśnij F5, albo:

   ```powershell
   dotnet run --project .\Inżynierka\Inżynierka.csproj --launch-profile http
   ```

3. **Swagger** — `http://localhost:5201/swagger`. Szybki test bazy: `GET /api/database/status` → `connected: true`.
4. **Testy:**

   ```powershell
   dotnet test
   ```

Darmowy projekt Supabase usypia się po około tygodniu bez ruchu — wtedy w panelu Supabase kliknij **Restore project**.

## API

| Endpoint | Opis |
|---|---|
| `GET /api/attractions` | Lista aktywnych atrakcji: filtr miasta lub promienia, nazwy, kategorii, zdjęcia; stronicowanie |
| `GET /api/attractions/{id}` | Szczegóły atrakcji: godziny, kategorie, elementy obiektu, zdjęcia, źródło danych |
| `GET /api/attractions/{id}/photos` | Zatwierdzone zdjęcia z autorem i licencją |
| `GET /api/attraction-categories` | Słownik 9 kategorii |
| `GET /api/locations/search` | Wyszukiwanie miasta lub adresu (Geoapify) |
| `GET /api/regions` | Obszary katalogu |

Parametry, przykłady i przypadki testowe: [docs/api.md](docs/api.md).

## Dane

Katalog powstał z 600 miejsc pobranych z Geoapify (OpenStreetMap) w promieniu 30 km od Rzeszowa. Po ręcznej selekcji turystycznej z 5 października 2026 zostały **152 aktywne atrakcje** i 33 murale czekające na zdjęcie. Decyzje i uzasadnienia: [data/tourism-review-2026-10-05/PRZEGLAD.md](data/tourism-review-2026-10-05/PRZEGLAD.md).

Ograniczenia danych:

- Godziny otwarcia ma niewiele miejsc i nie są zweryfikowane — aplikacja tylko ostrzega, nie zgaduje.
- Brak danych nie jest zastępowany wartością domyślną ani zdjęciem innego miejsca.
- Odległości w katalogu są w linii prostej; czasy przejazdów dojdą z OpenRouteService.

Źródła: dane © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright) (ODbL), Powered by [Geoapify](https://www.geoapify.com/); zdjęcia z [Wikimedia Commons](https://commons.wikimedia.org/) według licencji podanej przy każdym zdjęciu. Google API nie jest używane — Google Maps służy wyłącznie jako link do nawigacji.

## Dokumentacja

- [docs/opis-aplikacji.md](docs/opis-aplikacji.md) — cel, role, scenariusze, zakres MVP
- [docs/plan-prac.md](docs/plan-prac.md) — epiki, zadania i harmonogram
- [docs/api.md](docs/api.md) — endpointy i testy w Swaggerze
- [database/README.md](database/README.md) — schemat bazy i import
- [data/photos/README.md](data/photos/README.md) — pozyskiwanie i przegląd zdjęć
- [AGENTS.md](AGENTS.md) — zasady pracy dla asystentów AI
