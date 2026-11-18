# Plan prac – MVP do 31 stycznia 2027

Epiki w kolejności realizacji. Każde podzadanie to mały etap: kod → użytkownik kompiluje i testuje w Swaggerze/przeglądarce → poprawki. Oznaczenia: ✅ zrobione, 🔶 częściowo, ⬜ do zrobienia. Zasady techniczne: `AGENTS.md`, wymagania: `docs/opis-aplikacji.md`.

## Harmonogram (orientacyjny)

| Tydzień | Daty | Epiki |
|---|---|---|
| 1 | 16–22.11 | E0 Fundamenty, E1 Katalog – domknięcie (1.4 i 1.5 w tle, do końca grudnia) |
| 2 | 23–29.11 | E2 Konta i role |
| 3 | 30.11–6.12 | E3 Czasy przejazdów, E6.1–6.2 szkielet frontendu i katalog |
| 4–5 | 7–20.12 | E4 Planer |
| 6 | 21–23.12 | E5 Zapis i edycja planów |
| — | 24.12–1.01 | Bufor (święta) |
| 7–8 | 2–17.01 | E6.3–6.6 frontend planera, konta i panelu admina |
| 9 | 18–24.01 | E7 Opinie i zgłoszenia, E8 E-mail |
| 10 | 25–31.01 | E9 Przygotowanie do obrony |

Jeśli zabraknie czasu, w tej kolejności tniemy: E7 (zgłoszenia, potem opinie) → E8 → panel admina (edycja przez SQL/skrypty).

---

## E0 Fundamenty

- ✅ **0.1 Dokumenty dla AI i opis** (README jako strona projektu, szczegóły API w `docs/api.md`) – `AGENTS.md`, `CLAUDE.md`, `docs/opis-aplikacji.md`, ten plan. README skrócone do uruchomienia i odsyłaczy.
  Gotowe, gdy: użytkownik zaakceptował dokumenty.
- ✅ **0.2 Projekt testów** – `Inżynierka.Tests` (xUnit) w rozwiązaniu, katalog `Inżynierka/Planning/` na logikę planera, jeden przykładowy test.
  Gotowe, gdy: `dotnet test` przechodzi u użytkownika.
- ✅ **0.3 Konfiguracja obszaru** – sekcja `Catalog:Regions` w `appsettings.json` (kod, nazwa, środek, promień, domyślny) z walidacją przy starcie; endpoint `GET /api/regions`. Rzeszów i okolice (30 km) jako pierwszy wpis.
  Gotowe, gdy: endpoint zwraca obszar, testy walidacji przechodzą.

- ✅ **0.4 Migracje EF Core** – pakiet `Microsoft.EntityFrameworkCore.Tools`, wspólna konfiguracja `UseCatalogDatabase` (historia w `catalog.__ef_migrations_history`), `CatalogDbContextFactory` dla narzędzi, migracja startowa `InitialCatalog` odwzorowująca `database/001`–`005` (bez tworzenia tabel), nowe zasady w `AGENTS.md`.
  Gotowe, gdy: `Update-Database` wykonał `InitialCatalog`, dane bez zmian, kolejne `Add-Migration Sprawdzenie` daje pustą migrację (model zgodny ze snapshotem), testy przechodzą.

## E1 Katalog – domknięcie (🔶 lista, filtry, lokalizacje i zdjęcia już są)

- ✅ Kategorie, lista atrakcji z filtrami (miasto, promień, nazwa, kategoria), wyszukiwanie lokalizacji, zdjęcia.
- ✅ **1.1 Szczegóły atrakcji** – `GET /api/attractions/{id}`: opis, adres, godziny + weryfikacja, strona WWW, czas wizyty, kategorie, elementy obiektu, zatwierdzone zdjęcia z autorem i licencją, oznaczenie źródła. 404 dla nieaktywnych.
  Gotowe, gdy: test w Swaggerze dla aktywnej, usuniętej i nieistniejącej atrakcji.
- ✅ **1.2 Domyślne czasy wizyt** – kolumna `catalog.categories.default_visit_minutes` (`database/005_category_visit_minutes.sql`, wykonywany w migracji `InitialCatalog`); `VisitDuration.Resolve` w `Planning/` (własny czas atrakcji → najdłuższy czas kategorii → `other`); `visitMinutes` + `visitMinutesSource` w liście i szczegółach; `GET /api/attraction-categories` czyta z bazy z `defaultVisitMinutes`.
  Gotowe, gdy: każda aktywna atrakcja ma w API czas wizyty z oznaczeniem źródła.
  Później (dane): ręczne `estimated_visit_minutes` dla szczytów i szlaków (np. Berdecha) – dłuższa wycieczka piesza.
- ✅ **1.3 Usunięcie podglądu Overpass** – usunięte: endpoint `preview/rzeszow`, `OverpassAttractionService`, `AttractionPreview`, klient HTTP w `Program.cs`; katalog czyta wyłącznie bazę.
  Gotowe, gdy: API nie wysyła zapytań do Overpass, w kodzie nie ma stałych współrzędnych Rzeszowa.
- ⬜ **1.4 Przegląd zdjęć** (praca użytkownika, nie kod) – 73 propozycje w `data/photos/DO_PRZEJRZENIA.md` do zatwierdzenia/odrzucenia; przy okazji sprawdzić podejrzane wpisy bez zdjęcia (np. „Deloitte”, „Burger”, „Centrum Snu”) – mural czy do usunięcia. Stan 17.11: 1 zatwierdzone, 73 propozycje, 111 bez zdjęcia. **Brakuje części plików**: w `.local/photo-cache` jest 55 plików, z tego tylko 38 pasuje do raportu; zdjęcia Białego Ogrodu i zatwierdzone zdjęcie Zamku w Łańcucie zwracają 404. Najpierw ponowne pobranie brakujących plików: `POST /api/dev/photos/repair` (Development, pobiera z `thumbnail_url`, bez zmian w rekordach) – kod gotowy, czeka na uruchomienie. Przegląd w Swaggerze: kilka propozycji na atrakcję `POST /api/dev/attractions/{id}/photo-candidates` (Commons po nazwie + w promieniu, zapis `pending_review`) i decyzja `POST /api/dev/photos/{photoId}/review` (`approved`/`rejected`) – opis w `docs/api.md`. To pokrywa też punkt 1 z 1.7. **Najwygodniej: strona `http://localhost:5201/dev/zdjecia`** (zdjęcia po kolei, klawisze A/R/S, wyszukiwanie propozycji jednym kliknięciem). Wyszukiwanie korzysta z kategorii Commons obiektu (Wikidata P373, ok. 30 atrakcji), a dla muzeów osobno szuka **eksponatów** (`place_photos.kind = exhibit`, podpis `caption`, osobna lista `exhibits` w szczegółach atrakcji; migracja EF `PhotoKindAndCaption`). Zakładka „Atrakcje – wszystkie zdjęcia”: ponowny przegląd decyzji i wybór zdjęcia głównego (`place_photos.sort_order`, migracja `PhotoSortOrder`).
  Gotowe, gdy: najważniejsze atrakcje (Rzeszów + najbliższe okolice) mają zatwierdzone zdjęcie; raport `POKRYCIE.md` odświeżony.
- ⬜ **1.5 Krótkie opisy** (później) – opis 1–3 zdania dla atrakcji (teraz 0 z 185). **Decyzja użytkownika (18.11): opisy generuje agent AI** (patrz „Agent AI do uzupełniania danych atrakcji” w sekcji „Później”), użytkownik tylko je przegląda i zatwierdza; zapis z ręczną korektą (`manually_edited_fields`).
  Gotowe, gdy: atrakcje używane w scenariuszu obrony mają opis.
- 🔶 **1.6 Porządki w katalogu** – jedno źródło kategorii (w 1.2: endpoint czyta z bazy); w `AttractionPhotosController` przekroczenie czasu zapytania → 503 zamiast 500, `?includePending=true` w linku tylko w Development. (Kod gotowy, czeka na kompilację i test.)
  Gotowe, gdy: zmiany przejrzane, testy przechodzą.
- ⬜ **1.7 Więcej źródeł zdjęć** (po naprawie plików z 1.4) – tylko zdjęcia z jasną licencją, zawsze jako `pending_review` z autorem, licencją i linkiem źródła; bez scrapowania stron i Google Grafika.
  1. Wikimedia Commons – wyszukiwanie plików w promieniu (np. 100–200 m) od współrzędnych atrakcji; bez klucza.
  2. Openverse API (zdjęcia CC z wielu serwisów) – wyszukiwanie po nazwie i miejscowości; bez klucza przy małej liczbie zapytań.
  3. Opcjonalnie, za zgodą użytkownika (nowe usługi): Flickr API z filtrem licencji CC, Mapillary (pomniki, murale).
  4. Uzupełnienie ręczne: zdjęcia własne lub za zgodą właściciela (`user_provided`) – zadanie 1.9.
  Gotowe, gdy: nowe propozycje są na `/dev/zdjecia`, liczba atrakcji ze zdjęciem rośnie, żadna propozycja nie jest publikowana bez przeglądu. (Punkt 1 – Commons po kategorii/nazwie/promieniu – zrobiony.)
- 🔶 **1.8 Nowoczesne atrakcje (aktywna rozrywka)** – kod gotowy (18.11): strona `/dev/atrakcje` z zakładkami **Overture** (główne źródło, ok. 96 kandydatów w 30 km, w tym laser tag i wypożyczalnie kajaków – `scripts/fetch_overture_places.py`, `docs/zrodla-danych.md`) i ręczne dodawanie; ochrona przed duplikatami `PlaceDuplicates` (same_source / likely / nearby), migracja `ActiveEntertainmentCategory`; czeka na migrację i przegląd miejsc przez użytkownika. Foursquare API odrzucone (regulamin zabrania zapisu danych). Pierwszy import z Geoapify pominął escape roomy, laser tag, kręgielnie, parki trampolin, ścianki wspinaczkowe, gokarty, VR, paintball itp. Sprawdzenie OSM (17.11, 30 km od Rzeszowa): ok. 15 miejsc z nazwą (escape roomy Exitmania, Escape Room Rzeszów, WydostanSie; kręgielnie Galaktyka, Aloha; park trampolin Grawitacja; ścianki FlyWall, Peron Baltoro; gokarty ICF, Reskart; strzelnice); **laser taga w OSM brak** – OSM słabo pokrywa firmy rozrywkowe.
  1. Nowa kategoria `active-entertainment` „Aktywna rozrywka” z domyślnym czasem wizyty (ok. 60–90 min) – migracja EF z danymi słownika.
  2. ~~Pobranie z OSM (Overpass)~~ – zrobione, potem usunięte 18.11 (serwery przeciążone, dane OSM mamy z Geoapify, Overture daje więcej). Tagi: `leisure=escape_game|trampoline_park|bowling_alley|water_park|miniature_golf`, `sport=laser_tag|paintball|karting|climbing|bowling|10pin|archery|shooting|vr`; z pominięciem punktów gier hazardowych (np. „Lotto” jako `adult_gaming_centre`). Wynik jako propozycje do przeglądu (status do ustalenia, np. `pending_review` dla miejsc), dopiero po akceptacji `active`.
  3. Ręczne dodawanie atrakcji na stronie narzędzi Development: nazwa, adres → współrzędne z Geoapify (wybór propozycji), kategoria, strona WWW, godziny, czas wizyty; źródło `manual`, pola w `manually_edited_fields`. Do uzupełnienia laser taga, VR itp.; później przejdzie do panelu admina (E6.6).
  4. ~~Foursquare Places API~~ – odrzucone (regulamin zabrania zapisu danych); zamiast tego Overture.
  Ryzyka: firmy częściej się zamykają lub zmieniają – zachować stronę WWW i datę dodania; czas wizyty zależy od rezerwacji (szacunek, edytowalny).
  Gotowe, gdy: katalog zawiera przejrzane miejsca aktywnej rozrywki z kategorią i czasem wizyty, w tym ręcznie dodane brakujące (np. laser tag).
- ⬜ **1.9 Wgrywanie zdjęcia własnego** – na `/dev/zdjecia`: wybór pliku (JPEG/PNG/WebP do 8 MB), autor, licencja (CC0 / CC BY 4.0 / CC BY-SA 4.0 / zgoda właściciela z opisem), zapis do `.local/photo-cache` i `place_photos` jako `user_provided` + `pending_review`. Zastępuje `scripts/archiwum/import_place_photo.py` (wymagał psycopg i Pillow).
  Gotowe, gdy: własne zdjęcie (np. Białego Ogrodu) da się dodać i zatwierdzić bez Pythona.
- ✅ **1.10 Porządki w projekcie** (18.11) – usunięty szablon Razor Pages i `wwwroot` (9,6 MB), Overpass (OSM); jedna obsługa błędów bazy (`Infrastructure/DatabaseExceptionHandler`, 503) zamiast try/catch w endpointach; baza wymagana przy starcie; `[DevelopmentOnly]` dla narzędzi; stałe statusów; wyszukiwanie propozycji zdjęć w `WikimediaCommonsService`; skrypty jednorazowe → `scripts/archiwum/`; usunięte nieaktualne raporty zdjęć; ujednolicone wersje EF (bez ostrzeżeń); dokumentacja odświeżona.

## E2 Konta i role

- ✅ **2.1 Tabele Identity** – migracja EF (`Add-Migration Identity`) w schemacie `identity`, z RLS dla nowych tabel; mapowanie `IdentityDbContext`. Kod gotowy (18.11): pakiet `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 9.0.1 (zgoda użytkownika), osobny `AccountsDbContext` (decyzja: osobny kontekst, migracje w `Migrations/Accounts`, historia `identity.__ef_migrations_history`), `AppUser` z `CreatedAt`, role `User`/`Admin` w `HasData`, tabele `identity.users`, `roles`, `user_roles` itd. (snake_case); `GET /api/database/status` pokazuje liczbę kont i role. Migracje `Identity` i `IdentityRowLevelSecurity` (RLS, brak uprawnień `anon`/`authenticated`) wykonane i sprawdzone 18.11.
  Gotowe, gdy: użytkownik wykonał SQL, aplikacja startuje i widzi tabele.
- ✅ **2.2 Rejestracja, logowanie, wylogowanie, „ja”** – `POST /api/auth/register|login|logout`, `GET /api/auth/me`; ciasteczko HttpOnly; walidacja hasła; polskie komunikaty. Kod gotowy (18.11): `AuthController`, `Models/Auth.cs`, `Infrastructure/PolishIdentityErrorDescriber.cs`, konfiguracja `AddAccounts` w `Program.cs` (hasło 8+ z cyfrą i literami obu wielkości, blokada 5 prób / 15 min, 401/403 jako ProblemDetails); rejestracja nie loguje automatycznie. Scenariusz w Swaggerze przeszedł 18.11.
  Gotowe, gdy: scenariusz w Swaggerze: rejestracja → logowanie → `me` → wylogowanie → `me` = 401.
- ✅ **2.3 Rola Admin** – rola `Admin`, nadawana skryptem/SQL; polityka `[Authorize(Roles = "Admin")]`. Kod gotowy (18.11): `GET /api/admin/users` (`AdminUsersController`, lista kont – zaczątek panelu E6.6), SQL nadania/odebrania roli w `docs/api.md`. Test 401/403/200 przeszedł 18.11.
  Gotowe, gdy: endpoint testowy zwraca 403 dla zwykłego użytkownika i 200 dla admina.

## E3 Czasy przejazdów (OpenRouteService)

- ⬜ **3.1 Klient ORS** – `OpenRouteServiceClient` (macierz czasów i odległości, profile `driving-car` i `foot-walking`), klucz w `.local/ors-key.txt`, timeout, obsługa limitów (429 → 503).
  Gotowe, gdy: endpoint diagnostyczny (Development) zwraca macierz dla 3 atrakcji.
- ⬜ **3.2 Pamięć podręczna** – tabela `routing.travel_times` (para punktów, profil, czas, odległość, data), plik SQL; najpierw baza, potem ORS tylko dla brakujących par.
  Gotowe, gdy: drugie wywołanie dla tych samych punktów nie wysyła zapytania do ORS.
- ⬜ **3.3 Tryb samochód + spacer i przybliżenie** – odcinek pieszo poniżej progu (konfiguracja), inaczej samochód; gdy ORS niedostępny – szacunek z odległości PostGIS oznaczony jako przybliżony.
  Gotowe, gdy: testy xUnit wyboru trybu i przybliżenia; ostrzeżenie w odpowiedzi przy przybliżeniu.

## E4 Planer

- ⬜ **4.1 Model planu** – klasy w `Planning/`: wyjazd, dni (data, nocleg, godziny aktywności, przerwa na posiłek), pozycje (atrakcja, obowiązkowa/opcjonalna, czas wizyty), ostrzeżenia.
  Gotowe, gdy: model przejrzany przez użytkownika.
- ⬜ **4.2 Plan jednego dnia** – kolejność od noclegu do noclegu (najbliższy sąsiad + 2-opt na macierzy czasów), godziny przyjazdu/wyjazdu, wstawienie przerwy na posiłek, atrakcje opcjonalne odrzucane, gdy nie mieszczą się w dniu.
  Gotowe, gdy: testy xUnit: kolejność, mieszczenie się w oknie dnia, posiłek, obowiązkowe zawsze w planie albo ostrzeżenie.
- ⬜ **4.3 Wiele dni** – przydział atrakcji do dni (uwzględnia nocleg każdego dnia i obciążenie), potem 4.2 dla każdego dnia.
  Gotowe, gdy: testy xUnit dla 2–3 dni z różnymi noclegami.
- ⬜ **4.4 Ostrzeżenia o godzinach** – brak godzin → „nieznane godziny”; jeśli zapis da się prosto odczytać i wizyta wypada poza nim → „może być zamknięte”. Bez zmiany kolejności.
  Gotowe, gdy: testy xUnit dla trzech przypadków (brak, otwarte, zamknięte).
- ⬜ **4.5 Propozycje dodatkowych atrakcji** – na podstawie zainteresowań i wolnego czasu; zwracane osobno, do akceptacji.
  Gotowe, gdy: propozycje nie trafiają do planu bez akceptacji.
- ⬜ **4.6 Endpoint** – `POST /api/plans/generate` (bez zapisu, działa dla gościa): wejście → plan + ostrzeżenia + propozycje + linki Google Maps (dzień i odcinki, także tryb komunikacji).
  Gotowe, gdy: test w Swaggerze dla 1 i 3 dni; błędy 400 dla złych dat/pustej listy.

## E5 Zapis i edycja planów

- ⬜ **5.1 Tabele planów** – `trips.plans`, `trips.plan_days`, `trips.plan_items` (plik SQL), właściciel = użytkownik.
  Gotowe, gdy: SQL wykonany, mapowanie EF.
- ⬜ **5.2 CRUD** – `POST/GET/PUT/DELETE /api/plans`, lista „moje plany”; tylko właściciel (inaczej 404).
  Gotowe, gdy: test w Swaggerze na dwóch kontach.
- ⬜ **5.3 Edycja i przeliczenie** – zmiana kolejności w dniu, przeniesienie między dniami, zmiana czasu wizyty → przeliczenie godzin i przejazdów bez zmiany ręcznej kolejności (`POST /api/plans/recalculate` dla gościa i zapis dla konta).
  Gotowe, gdy: testy xUnit przeliczenia + test w Swaggerze.

## E6 Frontend (React + Vite)

- ⬜ **6.1 Szkielet** – `frontend/` (Vite, TypeScript, React Router, Leaflet), proxy `/api`, układ strony, polskie teksty, typy kontraktów (ręcznie lub generowane z OpenAPI – do decyzji).
  Gotowe, gdy: `npm run dev` pokazuje stronę i pobiera kategorie z API.
- ⬜ **6.2 Katalog i mapa** – lista + mapa, filtry, wybór lokalizacji (3 tryby), szczegóły atrakcji ze zdjęciami i oznaczeniami źródeł.
- ⬜ **6.3 Kreator planu** – daty, noclegi, godziny, zainteresowania, obowiązkowe/opcjonalne, akceptacja propozycji; plan gościa w localStorage.
- ⬜ **6.4 Widok i edycja planu** – dni, oś czasu, mapa trasy, ostrzeżenia, przeciąganie, przenoszenie między dniami, zmiana czasu wizyty, linki Google Maps.
- ⬜ **6.5 Konto** – rejestracja, logowanie, moje plany, zapis planu gościa po zalogowaniu.
- ⬜ **6.6 Panel administratora** – edycja atrakcji, zatwierdzanie zdjęć (moderacja i zgłoszenia po E7).
  Gotowe (każde), gdy: scenariusz przeklikany przez użytkownika, `npm run build` bez błędów.

## E7 Opinie i zgłoszenia

- ⬜ **7.1 Oceny i opinie** – tabela, `POST/GET /api/attractions/{id}/reviews`, średnia ocena w liście i szczegółach; jedna opinia na użytkownika i atrakcję.
- ⬜ **7.2 Moderacja** – admin ukrywa/zatwierdza opinie (tryb do ustalenia, patrz otwarte kwestie).
- ⬜ **7.3 Zgłoszenia błędów danych** – użytkownik zgłasza, admin zamyka z komentarzem i poprawia atrakcję.
  Gotowe (każde), gdy: test w Swaggerze dla ról gość/użytkownik/admin + ekran we frontendzie.

## E8 E-mail

- ⬜ **8.1 Wysyłka SMTP** – MailKit, konfiguracja `Smtp`, lokalnie Mailpit; szablony po polsku.
- ⬜ **8.2 Plan e-mailem** – `POST /api/plans/{id}/email`: prosty harmonogram dni z linkami Google Maps; limit wysyłek.
  Gotowe, gdy: wiadomość widoczna w Mailpit.

## E9 Przygotowanie do obrony

- ⬜ **9.1 Dane demo** – konto admina i użytkownika, 2–3 zapisane plany, zatwierdzone zdjęcia dla najważniejszych atrakcji.
- ⬜ **9.2 Instrukcja uruchomienia** – README: wymagania, `.local/`, kolejność SQL, start backendu, frontendu i Mailpit. Skrypty Python uruchamiane zwykłym Pythonem (teraz ścieżka do `codex-runtimes` w README). Osobna rola bazy dla backendu z ograniczonymi prawami zamiast konta administratora.
- ⬜ **9.3 Materiały do pracy** – diagram architektury, ERD, przypadki użycia, opis algorytmu i ograniczeń (powrót do auta, przybliżenia, godziny).
- ⬜ **9.4 Próba generalna** – pełny scenariusz na czystym uruchomieniu.
  Gotowe, gdy: scenariusz obrony przechodzi bez poprawek.

## Później (poza MVP)

Komunikacja publiczna (GTFS), parsowanie godzin i ich wpływ na kolejność, „przeplanuj” z akceptacją, restauracje, kolejne obszary Podkarpacia, wdrożenie online, wielojęzyczność.

### Agent AI do uzupełniania danych atrakcji

Agent (model językowy z wyszukiwaniem w internecie) dla wybranej atrakcji zbiera informacje z sieci i proponuje: szacowany czas wizyty, krótki opis (powiązane z 1.5), godziny otwarcia, stronę WWW, a dla szczytów i szlaków – czas wycieczki pieszej i punkt dojścia.

- Działa jako skrypt uruchamiany partiami (jak `fetch_place_photos.py`), nie przy każdym żądaniu użytkownika – koszt i limity pod kontrolą.
- Wynik to **propozycja do przeglądu**, nie automatyczny zapis: wartość + uzasadnienie + linki do źródeł. Po zatwierdzeniu trafia do `estimated_visit_minutes` / `description` i do `manually_edited_fields`, żeby import jej nie nadpisał. `visitMinutesSource` może wtedy dostać wartość np. `research`.
- Nie zmyśla: brak wiarygodnego źródła = brak propozycji. Opis piszemy własnymi słowami, bez kopiowania tekstów ze stron.
- Wymaga zgody użytkownika na nową usługę i klucz API (koszt zapytań). Przed wdrożeniem: próba na 10 atrakcjach i porównanie z ręczną oceną – może to być ciekawy fragment pracy inżynierskiej.
