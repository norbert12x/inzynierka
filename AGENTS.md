# Instrukcje dla asystentów AI (Claude Code, Codex itp.)

Praca inżynierska: **Atrakcje Podkarpacia** – katalog atrakcji i planer jedno- oraz wielodniowych wyjazdów (na start Rzeszów i okolice). Opis i wymagania: `docs/opis-aplikacji.md`. Plan prac (epiki, kolejność, „Gotowe, gdy”): `docs/plan-prac.md`. Przed zadaniem przeczytaj odpowiedni punkt planu.

Rozmawiaj po polsku, prostym językiem, zwięźle. Użytkownik zna C#. Termin MVP: **31 stycznia 2027**.

## Stos (ustalony)

- Backend: ASP.NET Core 9 (kontrolery API + Swagger w Development), projekt `Inżynierka/`, rozwiązanie `Inżynierka.sln`.
- Baza: Supabase (PostgreSQL + PostGIS), EF Core + Npgsql + NetTopologySuite. Schemat katalogu: `catalog`.
- Konta: ASP.NET Core Identity (e-mail + hasło, ciasteczko), role `User` i `Admin`.
- Routing (czasy przejazdów): OpenRouteService API (profile samochód i pieszo).
- Geokodowanie: Geoapify. Zdjęcia: Wikimedia Commons + własne. **Bez API Google** (decyzja użytkownika) – Google Maps tylko jako linki nawigacji.
- E-mail: SMTP + MailKit (lokalnie skrzynka testowa, np. Mailpit w Dockerze).
- Frontend: React + Vite (TypeScript) w katalogu `frontend/`, mapa Leaflet + kafelki OSM. Dev server Vite przekazuje `/api` do backendu (proxy), więc ciasteczko logowania działa bez CORS.
- Testy: xUnit dla logiki planera (projekt testowy obok `Inżynierka/`).
- Skrypty danych: Python w `scripts/` (pobieranie, przegląd, import).

Nie dodawaj nowych pakietów NuGet/npm ani usług zewnętrznych bez zgody użytkownika.

## Struktura i gdzie co dodawać

- Endpoint: `Inżynierka/Controllers/<Nazwa>Controller.cs`, kontrakty żądań/odpowiedzi w `Inżynierka/Models/`, encje i mapowanie w `Inżynierka/Data/` (`CatalogDbContext` lub nowy kontekst dla nowego schematu).
- Ustawienia aplikacji (bez sekretów): klasa opcji + walidator `IValidateOptions` w `Inżynierka/Configuration/`, rejestracja z `ValidateOnStart()` w `Program.cs`. Obszary katalogu: sekcja `Catalog:Regions` (`CatalogOptions`).
- Integracje zewnętrzne: `Inżynierka/Services/` jako typowany `HttpClient` rejestrowany w `Program.cs` (timeout, bez logowania URL z kluczem – wzór: `GeoapifyGeocodingService`).
- Logika planera: `Inżynierka/Planning/` – czyste klasy bez bazy i HTTP (dane wejściowe → plan), żeby dało się je testować xUnit.
- Zmiany bazy: **tylko nowy plik** `database/NNN_opis.sql` (kolejny numer). Nie edytuj istniejących plików SQL. Nie używamy migracji EF, `EnsureCreated()` ani `Database.Migrate()`. Opisz plik w `database/README.md`.
- Ekrany: `frontend/src/features/<moduł>/`, wywołania API w `api.ts` modułu.
- Dokumentacja etapu: opis endpointu i przypadki testowe w Swaggerze (czego oczekiwać, błędy 400/404) w `docs/api.md`. README to strona projektu na GitHubie – aktualizuj tylko tabelę stanu prac i listę endpointów, bez instrukcji etapów.

## Zasady

- Konfiguracja przez `IConfiguration` (sekcje, np. `Database`, `Geoapify`, `OpenRouteService`, `Smtp`). Sekrety tylko w `.local/` (ignorowane przez Git) lub zmiennych środowiskowych (`Sekcja__Klucz`). Nigdy w kodzie ani `appsettings*.json`. Klucze nie trafiają do odpowiedzi API ani logów.
- Współrzędne: `geography(point, 4326)`, w NTS `X` = długość, `Y` = szerokość. Odległości liczy PostGIS w metrach.
- Czas: znaczniki w UTC (`timestamptz`, `DateTimeOffset`). Plan wyjazdu w czasie lokalnym `Europe/Warsaw` (`DateOnly`/`TimeOnly`).
- Brak danych ≠ wartość domyślna: nieznane godziny otwarcia to nie „całodobowo”, brak zdjęcia nie jest zastępowany innym. Nie zmyślamy godzin, opinii ani czasów zwiedzania.
- Godziny otwarcia: tylko ostrzeżenie w planie, bez wpływu na kolejność (MVP).
- Dodatkowe atrakcje i przeplanowanie wymagają akceptacji użytkownika – nie zmieniaj planu za niego.
- Region nie może być stałą w kodzie: obszar (środek, promień, nazwa) z konfiguracji/bazy, żeby dało się dodać kolejne części Podkarpacia.
- Błędy API jako `ProblemDetails` z polskim opisem; błąd dostawcy zewnętrznego → 502/503/504 zamiast pustej listy.
- Zachowuj oznaczenia źródeł (OpenStreetMap, Geoapify, OpenRouteService, autor i licencja zdjęcia).
- UI, komunikaty i dokumentacja po polsku; nazwy w kodzie po angielsku.
- Pracujemy małymi etapami: po każdym użytkownik kompiluje, testuje w Swaggerze i przegląda kod.

## Czego nie robić

- **Nie uruchamiaj `dotnet build`, `dotnet run`, `dotnet test` ani aplikacji**, chyba że użytkownik wprost poprosi. Napisz, czego nie sprawdzono.
- **Nie commituj, nie pushuj, nie otwieraj PR** – użytkownik robi to sam.
- Nie wykonuj zapisów w Supabase (skrypty z `--apply`, SQL zmieniający dane) bez zgody użytkownika.
- Nie zużywaj limitów API (Geoapify, ORS) w pętlach ani testach automatycznych – w testach używaj atrap.

## Kontrola przed zakończeniem (gdy użytkownik o to poprosi)

```powershell
dotnet build Inżynierka.sln
dotnet test Inżynierka.sln
cd frontend; npm run lint; npm run build
```

Inaczej: wypisz, co użytkownik ma uruchomić i sprawdzić w Swaggerze.
