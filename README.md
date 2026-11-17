# Atrakcje Podkarpacia

Opis i wymagania: [docs/opis-aplikacji.md](docs/opis-aplikacji.md). Plan prac: [docs/plan-prac.md](docs/plan-prac.md) (zastępuje sekcję „Plan dalszych małych etapów” poniżej). Zasady dla asystentów AI: [AGENTS.md](AGENTS.md).

## Szczegóły atrakcji — test w Swaggerze

`GET /api/attractions/{id}` zwraca jedną aktywną atrakcję: nazwę, opis, adres, współrzędne, stronę WWW, kategorie, godziny z informacją o weryfikacji, `estimatedVisitMinutes`, elementy obiektu (`components`), zatwierdzone zdjęcia z autorem i licencją oraz `attribution` (źródło danych). Pola `null` oznaczają brak danych: `openingHours=null` to nieznane godziny, nie „całodobowo”. Opisy i czasy wizyt są na razie puste (zadania 1.2 i 1.5).

| Przypadek | `id` | Oczekiwany wynik |
|---|---|---|
| Aktywna, ze zdjęciem i elementem | `4402cf21-12ab-5f1c-9802-f07ab332feb7` (Muzeum – Zamek w Łańcucie) | 200, `photos` z 1 zdjęciem, `components` niepuste |
| Aktywna, wiele elementów | `35218f72-3197-51c7-a458-2b77cc3da29e` (Park Zamkowy) | 200, lista `components` |
| Aktywna, bez zdjęć | `41f02db7-7ab3-5642-b04a-be599c3f228e` (Muzeum Etnograficzne) | 200, `photos=[]` |
| Usunięta w selekcji | `3ac53e57-a866-58c0-ba9c-2eb1f1c2e63e` (Skwer im. K. Górskiego) | 404 |
| Mural czekający na zdjęcie | `c7bd67ab-a017-5ad2-9012-0acd29a87e7d` (Irena Sendler) | 404 |
| Element obiektu | `460717b8-9b5a-5224-8b93-17e9187d466c` (Taniec Życia) | 404 |
| Nieistniejąca | `00000000-0000-0000-0000-000000000000` | 404 |
| Nie-GUID | `/api/attractions/abc` | 404 (trasa nie pasuje) |

Kod: `Controllers/AttractionsController.cs` (metoda `Details`), `Models/AttractionDetails.cs`. Plik zatwierdzonego zdjęcia Zamku w Łańcucie jest obecnie brakujący w `.local/photo-cache` (zadanie 1.4), więc `imageUrl` zwróci 404 — sam wpis zdjęcia w odpowiedzi jest poprawny.

## Obszary katalogu — test w Swaggerze

Obszary (nazwa, środek, promień) są w `Inżynierka/appsettings.json`, sekcja `Catalog:Regions`, a nie w kodzie. Na start jeden: `rzeszow`, „Rzeszów i okolice”, środek 50.037, 22.004, promień 30 km. Kolejny obszar = nowy wpis w tablicy. Dokładnie jeden wpis ma `IsDefault: true`.

1. `GET /api/regions` → HTTP 200, `items` z jednym obszarem `rzeszow`, `isDefault: true`. Nie wymaga bazy ani klucza API.
2. Środek i promień obszaru można podać w `GET /api/attractions?latitude=50.037&longitude=22.004&radiusKm=30`.
3. Błędna konfiguracja zatrzymuje start aplikacji z komunikatem `Catalog:Regions...`. Sprawdzenie: zmień tymczasowo `RadiusKm` na `0` → aplikacja nie wystartuje; przywróć `30`.

Reguły walidacji sprawdzają testy `Inżynierka.Tests/CatalogOptionsValidatorTests.cs` (także poprawność prawdziwego `appsettings.json`).

Automatyczne pobieranie propozycji zdjęć: `scripts/fetch_place_photos.py --limit 20`. Opis, ograniczenia i instrukcja w `data/photos/README.md`; lista propozycji w `data/photos/DO_PRZEJRZENIA.md`. Zdjęcia wymagają przeglądu przed publikacją.

Obsługa zdjęć w API: `/api/attractions/{id}/photos` i lokalny podgląd propozycji w Development przez `?includePending=true`. Parametr `requirePhoto=true` na liście atrakcji wybiera tylko miejsca z zatwierdzonym zdjęciem. Pełny stan pokrycia i braki: `data/photos/POKRYCIE.md`. Google nie jest używane, zgodnie z decyzją użytkownika.

Aktualny etap: lista atrakcji z filtrem miasta lub promienia oraz wyszukiwanie adresu/miasta przez Geoapify. Kod przygotowany do testu w Visual Studio. Instrukcja bazy i potwierdzenie importu w `database/README.md`. Słownik kategorii ma obecnie dziewięć wpisów. Wcześniejsze instrukcje etapu 1 opisują pierwotny słownik sześciu kategorii.

Aplikacja do odkrywania atrakcji i planowania jedno- oraz wielodniowych wyjazdów na Podkarpaciu. Pracujemy małymi etapami: po każdym uruchomienie, przegląd kodu i poprawki użytkownika.

## Miasto, miasto i okolice, adres hotelu

Trzy opcje korzystają z dwóch endpointów; backend nie wybiera automatycznie lokalizacji za użytkownika:

| Opcja | Sposób pobrania atrakcji |
|---|---|
| Miasto | `GET /api/attractions?city=Rzeszów` — zgodność pola `City`, bez rozróżniania wielkości liter i spacji na końcach. Bez geokodowania i granic administracyjnych. |
| Miasto i okolice | Wyszukaj miasto, wybierz propozycję, następnie podaj jej współrzędne i promień w `GET /api/attractions`. |
| Hotel / adres | Wyszukaj pełny adres hotelu, wybierz propozycję, następnie podaj jej współrzędne i promień w `GET /api/attractions`. |

### Wyszukiwanie lokalizacji

`GET /api/locations/search?text=Rzeszów&type=city` zwraca do pięciu propozycji w Polsce. Dla adresu: `GET /api/locations/search?text=Rynek%201,%20Rzeszów&type=address`. `type` domyślnie wynosi `address`; dopuszczalne wartości to `address` i `city`. Tekst ma od 2 do 200 znaków, po usunięciu spacji na końcach musi mieć co najmniej dwa znaki.

Odpowiedź zawiera `items`, `provider` i `attribution`. Każda propozycja ma `placeId`, `formattedAddress`, `city`, `resultType`, `latitude`, `longitude`, `confidence`. `confidence` to ocena dostawcy, nie potwierdzenie poprawności adresu. Wynik może wskazywać ulicę lub miasto zamiast budynku — trzeba przejrzeć pełny adres i `resultType`. Wyszukiwanie po samej nazwie hotelu nie gwarantuje znalezienia hotelu; podaj ulicę, numer i miejscowość. Pusta lista oznacza brak dopasowań. Błąd dostawcy daje 503, niepoprawna odpowiedź 502, timeout 504.

W Development aplikacja korzysta z istniejącego, ignorowanego przez Git pliku `.local/geoapify-key.txt`. Alternatywnie ustaw `Geoapify:ApiKey` w konfiguracji albo zmienną środowiskową `Geoapify__ApiKey`. Klucz nie trafia do odpowiedzi API; logowanie adresów żądań tego klienta HTTP jest wyłączone. Każde wyszukiwanie wysyła zapytanie do Geoapify, korzystając z limitu konta. Lista atrakcji czyta wyłącznie Supabase.

### Promień od wybranej lokalizacji

Przykład: `GET /api/attractions?latitude=50.037&longitude=22.004&radiusKm=10`.

- Podaj wszystkie trzy parametry: `latitude` (-90 do 90), `longitude` (-180 do 180) i `radiusKm` (0,1 do 100 km). W URL używaj kropki jako separatora dziesiętnego.
- Nie łącz `city` z parametrami promienia: tryb „miasto i okolice” ma uwzględniać również inne miejscowości.
- Filtry nazwy, kategorii i strony można łączyć z każdym trybem.
- Obliczenia wykonuje PostGIS na typie `geography`, w metrach. Wynik `distanceKm` jest odległością od wybranego punktu w kilometrach; bez wyszukiwania w promieniu jest `null`.
- W promieniu kolejność to odległość, nazwa, identyfikator; w pozostałych trybach nazwa, identyfikator. Odległość jest geograficzna, nie po drogach i nie określa czasu dojazdu.
- Dla miasta brak/błędna nazwa miejscowości w danych może pominąć obiekt. Nie zmieniamy automatycznie danych podczas wyszukiwania.

### Test tego etapu

Nie uruchamiano builda ani aplikacji .NET. 5 października 2026 sprawdzono osobno dostawcę i bazę, bez zmian danych: Geoapify zwrócił 1 propozycję dla „Rzeszów” oraz 3 dla „Rynek 1, Rzeszów”. SQL potwierdził 142 aktywne atrakcje z polem miasta Rzeszów i 172 w promieniu 10 km od 50.037, 22.004. To wyniki sprzed selekcji turystycznej; po selekcji SQL potwierdził odpowiednio 65 i 74 aktywne miejsca (raport `data/tourism-review-2026-10-05/verification.json`). Wszystkie sprawdzone odległości mieściły się w promieniu, identyfikatory były unikalne, a pierwsze dwie strony nie nakładały się.

Po swojej kompilacji sprawdź w Swaggerze oba endpointy i powyższe przykłady. Następnie sprawdź błędy 400: samo `latitude`, `radiusKm=0`, `city=Rzeszów` razem z kompletem promienia i `type=hotel`. Test Swaggera potwierdzi również tłumaczenie zapytań EF Core, którego bez uruchomienia aplikacji nie sprawdzono.

Dokumentacja dostawców: [Geoapify Geocoding](https://apidocs.geoapify.com/docs/geocoding/forward-geocoding/) oraz [Npgsql/PostGIS](https://www.npgsql.org/efcore/mapping/nts.html).

## Lista atrakcji z Supabase — test w Swaggerze

Kod tego etapu nie był kompilowany ani uruchamiany przez agenta. Zatrzymaj poprzednią aplikację w Visual Studio, skompiluj i uruchom ponownie. W sekcji **Attractions** wybierz **GET /api/attractions → Try it out → Execute**.

| Parametr | Działanie |
|---|---|
| `page` | Numer strony od 1; domyślnie 1, maksymalnie 1 000 000. |
| `pageSize` | Liczba wyników od 1 do 100; domyślnie 20. |
| `search` | Fragment nazwy, do 100 znaków, bez rozróżniania wielkości liter. Polskie znaki pozostają istotne. |
| `category` | Jeden kod kategorii z `GET /api/attraction-categories`, np. `museums` lub `nature`. |

Wyszukiwanie i kategorię można łączyć. Puste filtry są pomijane; spacje na początku i końcu są usuwane. Wyszukiwanie traktuje `%` i `_` jako zwykłe znaki. Nieznana kategoria i nieprawidłowe parametry zwracają HTTP 400.

Odpowiedź HTTP 200 zawiera `items`, `totalCount`, `page`, `pageSize` i `totalPages`. `totalCount` liczy wszystkie aktywne atrakcje pasujące do filtrów. `items` zawiera tylko bieżącą stronę, w kolejności nazwa, identyfikator. Zmiany katalogu między żądaniami mogą zmienić skład stron. Brak wyników lub strona poza zakresem oznacza pustą listę, nie błąd.

Każdy element ma identyfikator, nazwę, miasto, adres, współrzędne (`latitude`, `longitude`), listę kategorii z kodami i nazwami oraz `openingHours`, `openingHoursVerification`, `openingHoursConfirmedOn`. Brak godzin (`null`) nie oznacza dostępności całodobowej. Zapis godzin jest tekstem źródłowym; na tym etapie nie interpretujemy go ani nie obliczamy „otwarte teraz”.

Przykłady do sprawdzenia:

1. `/api/attractions` — po selekcji turystycznej z 5 października 2026 `totalCount=152`, `items` ma 20 elementów i `totalPages=8`.
2. `/api/attractions?page=2&pageSize=20` — kolejna strona, bez identyfikatorów ze strony 1 przy niezmienionym katalogu.
3. `/api/attractions?category=museums` — każda atrakcja ma kategorię `museums`.
4. `/api/attractions?search=zamek` — każda nazwa zawiera „zamek”, bez rozróżniania wielkości liter.
5. `/api/attractions?category=museums&search=zamek` — oba warunki jednocześnie.
6. `/api/attractions?page=0`, `?pageSize=101` lub `?category=nieznana` — HTTP 400.
7. `/api/attractions?page=1000` — HTTP 200, `items=[]`, liczba wszystkich wyników pozostaje 152.

API pomija wpisy usunięte, murale oczekujące na zdjęcia i elementy obiektów (np. tablice). Nie pobiera nowych danych z Geoapify ani Overpass. Brak konfiguracji lub problem z bazą zwraca HTTP 503 zamiast pustej listy. Parametry połączenia i dane źródłowe dostawcy nie trafiają do odpowiedzi.

Kod do przejrzenia: `Inżynierka/Controllers/AttractionsController.cs` i `Inżynierka/Models/AttractionList.cs`.

## Etap 1 — API i kategorie

Istniejący projekt ASP.NET Core Razor Pages (.NET 9) został rozszerzony o kontrolery API i Swagger. Strony szablonu nadal działają. Swagger jest dostępny tylko w środowisku Development.

1. Otwórz `Inżynierka.sln` w Visual Studio.
2. Wybierz profil `http` i uruchom przez F5 lub Ctrl+F5.
3. Otworzy się `http://localhost:5201/swagger`.
4. Rozwiń `GET /api/attraction-categories`, kliknij **Try it out**, a potem **Execute**.
5. Oczekiwany wynik: HTTP 200 i sześć kategorii z polami `code` i `name`.

Profil `https` otwiera Swagger pod `https://localhost:7191/swagger` i wymaga zaufanego lokalnego certyfikatu deweloperskiego.

Alternatywnie z katalogu rozwiązania:

```powershell
dotnet run --project '.\Inżynierka\Inżynierka.csproj' --launch-profile http
```

### Co przejrzeć

- `Program.cs`: rejestracja kontrolerów, dokumentacji i tras.
- `Controllers/AttractionCategoriesController.cs`: obsługa żądania GET i wstępny słownik kategorii.
- `Models/AttractionCategory.cs`: kształt jednej kategorii w odpowiedzi JSON.
- `Properties/launchSettings.json`: otwarcie Swaggera przy uruchomieniu.

Kategorie są propozycją do przetestowania. Nie są jeszcze importem OSM ani listą atrakcji. Nie potrzebujemy na tym etapie kont, kluczy API ani bazy. Jedna atrakcja w przyszłości może mieć kilka kategorii; np. muzeum w zamku. Wybór kategorii nie oznacza potwierdzonej przydatności miejsca dla dzieci.

Konfiguracja Swaggera opiera się na [dokumentacji Microsoft](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-8.0) i pakiecie [Swashbuckle.AspNetCore 9.0.6](https://www.nuget.org/packages/Swashbuckle.AspNetCore/9.0.6).

## Etap 2 — podgląd danych OSM (usunięty)

Próbny endpoint `GET /api/attractions/preview/rzeszow` (zapytania na żywo do publicznego Overpass, październik 2026) posłużył do oceny jakości danych OSM. Usunięty 17 listopada 2026 w zadaniu 1.3: katalog czyta wyłącznie Supabase, a dane pobierają skrypty z `scripts/` (Geoapify). Kod jest w historii Git.

## Plan dalszych małych etapów

Każdy punkt może zostać rozbity na mniejsze kroki. Etap 1 jest gotowy; kod etapu 2 jest przygotowany do testów użytkownika.

2. Próbne pobranie atrakcji z OSM/Overpass dla jednej okolicy, podgląd przez API, ocena kompletności i duplikatów.
3. Zapis katalogu w Supabase/PostgreSQL: źródło, identyfikator zewnętrzny, data importu; aktualizacje zachowujące ręczne poprawki.
4. Wyszukiwanie atrakcji w okolicy i według kategorii, potem mapa i lista.
5. Wybór miejsc obowiązkowych i opcjonalnych oraz akceptacja dodatkowych propozycji.
6. Prosty plan dnia z czasami wizyt i przemieszczania, potem wiele dni, noclegi i posiłki.
7. Ręczna edycja kolejności i czasów, konflikty godzin otwarcia, przeplanowanie do akceptacji.
8. Konta i zapis wyjazdów, potem opinie, zgłoszenia i moderacja oraz wysyłka kopii planu e-mailem.

## Najważniejsze ustalenia z czatu „Przeanalizuj plan projektu”

- Najpierw backend w C# testowany przez Swagger, później interfejs z mapą.
- Supabase jest wybraną bazą; OSM/Overpass to zaproponowane źródło początkowego katalogu po udanej próbie w czacie planistycznym.
- Użytkownik wybiera miasto, daty i zainteresowania; może tworzyć plan bez konta. Konto potrzebne do zapisania planu w aplikacji.
- Dodatkowe atrakcje i przeplanowanie wymagają akceptacji użytkownika.
- Optymalizujemy czas przemieszczania, uwzględniając wizyty, godziny otwarcia i miejsca obowiązkowe. Nawigacja między punktami ma prowadzić do Google Maps.
- Nieznane godziny otwarcia to inny stan niż potwierdzony brak ograniczeń godzinowych. Szacunki czasu wizyty są edytowalne.
- Różne noclegi i godziny aktywności są możliwe dla różnych dni. Docelowo ręczna zmiana kolejności i przenoszenie atrakcji między dniami.
- Nasze oceny i opinie Google pozostają oddzielnymi źródłami. Integracje Google i restauracji wymagają osobnego sprawdzenia.
- E-mail ma zawierać prosty harmonogram z linkami. Obsługa ograniczeń poruszania się jest poza podstawową wersją.

## Ryzyka do rozwiązania przed planerem

Jakość danych jest ważniejsza niż rozbudowany algorytm: import nie gwarantuje aktualnych godzin, opisów czy unikalności miejsc. Przekierowanie do Google Maps rozwiązuje nawigację, ale nie dostarcza samo w sobie czasów przejazdów do harmonogramu. Przy łączeniu samochodu i spaceru trzeba uwzględnić powrót do auta. Te kwestie sprawdzimy w osobnych etapach.

## Selekcja turystyczna beta — 5 października 2026

Przejrzano wszystkie 515 samodzielnych miejsc: pozostawiono 152 aktywne atrakcje oraz 33 murale warunkowe. 29 rekordów połączono jako elementy większych miejsc, a 301 wyłączono z API. Zmiany zapisano w Supabase i kopii lokalnej, zachowując wszystkie 600 źródłowych identyfikatorów oraz historię. [Pełne decyzje i uzasadnienia](data/tourism-review-2026-10-05/PRZEGLAD.md). Ocena przydatności do beta nie potwierdza aktualnych godzin ani dostępności wszystkich miejsc. Nie uruchamiano builda.
