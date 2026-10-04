# Atrakcje Podkarpacia

Aktualny etap bazy: model EF Core, schemat i import SQL oraz endpoint `GET /api/database/status`. Instrukcja i stan potwierdzenia wykonania w `database/README.md`. Słownik kategorii ma obecnie dziewięć wpisów. Wcześniejsze instrukcje etapu 1 opisują pierwotny słownik sześciu kategorii.

Aplikacja do odkrywania atrakcji i planowania jedno- oraz wielodniowych wyjazdów na Podkarpaciu. Pracujemy małymi etapami: po każdym uruchomienie, przegląd kodu i poprawki użytkownika.

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

## Etap 2 — podgląd danych OSM dla Rzeszowa

Kod przygotowany do Twojego testu. Zgodnie z ustaleniem nie uruchamiano builda ani aplikacji.

1. Uruchom projekt w Visual Studio i otwórz Swagger.
2. Rozwiń `GET /api/attractions/preview/rzeszow` w sekcji `Attractions`.
3. Kliknij **Try it out → Execute**. Nie ma parametrów ani klucza API.
4. Przy HTTP 200 sprawdź `count` i listę `attractions`. Liczba wyników zależy od bieżących danych OSM.
5. Otwórz kilka `sourceUrl` i porównaj nazwy oraz lokalizacje ze znanymi Ci miejscami.

Pobieramy wybrane obiekty turystyczne i historyczne wokół punktu 50.037, 22.004, w promieniu 3000 m. To próba jakości danych, nie pełna lista atrakcji i nie granice administracyjne Rzeszowa. Zapytanie używa filtra [around z dokumentacji Overpass](https://dev.overpass-api.de/overpass-doc/en/full_data/polygon.html). Dla rozległych obiektów filtr może uwzględnić fragment geometrii znajdujący się w promieniu, choć zwrócony środek leży dalej.

- `osmType` + `osmId`: wspólny identyfikator źródłowy (sam numer nie wystarczy).
- `categoryCodes`: kategorie zgodne ze słownikiem API. Pusta lista oznacza, że brak podstaw do przypisania kategorii. Nie wyszukujemy jeszcze niezależnie wszystkich obiektów przyrodniczych i sakralnych.
- `latitude`, `longitude`: współrzędne; `isApproximateLocation=true` dla środka obszaru lub relacji. To nie jest lokalizacja wejścia ani parkingu.
- `openingHours`: surowy zapis OSM, bez interpretacji i weryfikacji aktualności. `null` oznacza brak danych, nie całodobową dostępność.
- `rawElementCount`, `skippedElementCount`: liczba pobranych i pominiętych rekordów. Pomijamy brak nazwy, niepoprawną lokalizację/identyfikator i powtórzony identyfikator. Różne identyfikatory tego samego miejsca nie są scalane.
- `fetchedAtUtc`: czas pobrania, nie data weryfikacji atrakcji.
- `attribution`, `licenseUrl`: oznaczenie źródła danych.

Każde Execute wysyła jedno zapytanie do publicznego Overpass. Nie ma automatycznych ponowień ani zapisu w bazie. Poczekaj na odpowiedź przed kolejną próbą. HTTP 503 oznacza problem połączenia lub odrzucenie przez dostawcę, 504 przekroczenie czasu oczekiwania, a 502 niepoprawną albo niepełną odpowiedź. W tych przypadkach API zwraca opis błędu zamiast pozornie poprawnej pustej listy.

Kod do przejrzenia: `Services/OverpassAttractionService.cs` pobiera i mapuje dane, `Models/AttractionPreview.cs` opisuje wynik, a `Controllers/AttractionsController.cs` udostępnia endpoint i obsługuje błędy. Rejestracja klienta HTTP znajduje się w `Program.cs`.

Diagnostyka błędów połączenia: odpowiedź zawiera `upstreamStatusCode` (kod HTTP dostawcy) lub `connectionError`, jeśli nie otrzymano odpowiedzi HTTP. Kod 504 dostawcy jest zwracany jako 504 naszego API, a limit 429 jako 503 z opisem ograniczenia. Podczas osobnej próby HTTP 4 października 2026 Overpass zwrócił 504 z informacją o prawdopodobnym przeciążeniu. Nie uruchamiano przy tym aplikacji ani builda. W przypadku 504 odczekaj około minuty przed ponowieniem; poprawa komunikatu nie usuwa przeciążenia zewnętrznego serwera.

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
