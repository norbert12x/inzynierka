# API — endpointy i testy w Swaggerze

Swagger: `http://localhost:5201/swagger` (tylko Development). Błędy to `ProblemDetails` z polskim opisem. Niedostępna baza → **503 „Baza danych jest chwilowo niedostępna”** z każdego endpointu (jedno miejsce obsługi: `Infrastructure/DatabaseExceptionHandler.cs`); błąd usługi zewnętrznej → 502/503/504, nigdy pozornie poprawna pusta lista. `null` = brak danych, nie wartość domyślna (`openingHours=null` to nieznane godziny, nie „całodobowo”).

## Publiczne

### Kategorie — `GET /api/attraction-categories`

Słownik z bazy: `code`, `name`, `defaultVisitMinutes` (np. `museums` 90, `public-art` 10, `active-entertainment` 75); „Pozostałe atrakcje” na końcu. Atrakcja może mieć kilka kategorii.

### Obszary — `GET /api/regions`

Z `appsettings.json` (`Catalog:Regions`: kod, nazwa, środek, promień, `isDefault`). Teraz `rzeszow`, 50.037, 22.004, 30 km. Błędna konfiguracja (np. `RadiusKm: 0`, dwa domyślne) zatrzymuje start aplikacji – testy `CatalogOptionsValidatorTests`.

### Lista atrakcji — `GET /api/attractions`

Tylko aktywne atrakcje (bez usuniętych, murali czekających na zdjęcie i elementów obiektów).

| Parametr | Działanie |
|---|---|
| `page`, `pageSize` | strona od 1; 1–100 (domyślnie 20) |
| `search` | fragment nazwy (do 100 znaków), bez wielkości liter; `%` i `_` jako zwykłe znaki |
| `category` | kod z kategorii; nieznany → 400 |
| `city` | zgodność pola miasta; nie łączyć z promieniem |
| `latitude`, `longitude`, `radiusKm` | promień od punktu (wszystkie trzy, 0,1–100 km), odległość w linii prostej |
| `requirePhoto` | tylko z zatwierdzonym zdjęciem miejsca |

Element: id, nazwa, miasto, adres, współrzędne, kategorie, godziny (tekst źródłowy), `visitMinutes` + `visitMinutesSource` (`place` = czas atrakcji, `category_default` = najdłuższy czas jej kategorii; logika `Planning/VisitDuration.cs`), `distanceKm` (tylko w promieniu), `hasPhoto`, `pendingPhotoCount`. Kolejność: w promieniu odległość → nazwa; inaczej nazwa.

Wybór miejsca: miasto (`?city=`), miasto i okolice albo hotel – `GET /api/locations/search` → współrzędne propozycji + `radiusKm`.

Testy (stan 5.10.2026): bez parametrów `totalCount=152`; `?category=museums`; `?search=zamek`; `?latitude=50.037&longitude=22.004&radiusKm=10` → 74; `?city=Rzeszów` → 65; `?page=1000` → pusta lista; 400 dla `?page=0`, `?pageSize=101`, `?category=nieznana`, samego `?latitude=50`, `city` razem z promieniem.

### Szczegóły atrakcji — `GET /api/attractions/{id}`

Aktywna atrakcja: opis, adres, współrzędne, WWW, kategorie, godziny z weryfikacją, czas wizyty, `components` (elementy obiektu), zatwierdzone zdjęcia osobno `photos` (miejsce, pierwsze = główne) i `exhibits` (wnętrza/eksponaty z `caption`), `attribution` (Geoapify/OSM albo Overture z licencją rekordu). Usunięta, mural, element obiektu, nieistniejąca → 404.

Przykłady: Muzeum – Zamek w Łańcucie `4402cf21-12ab-5f1c-9802-f07ab332feb7` (200, elementy, zdjęcia); Skwer im. K. Górskiego `3ac53e57-a866-58c0-ba9c-2eb1f1c2e63e` (404 – usunięty); mural Irena Sendler `c7bd67ab-a017-5ad2-9012-0acd29a87e7d` (404).

### Zdjęcia — `GET /api/attractions/{id}/photos` i `.../photos/{photoId}/image`

Zatwierdzone zdjęcia (autor, licencja, źródło, `imageUrl`, `kind`, `caption`) w kolejności galerii, oraz plik obrazu z `.local/photo-cache` (brak pliku → 404). W Development `?includePending=true` pokazuje też propozycje i murale.

### Wyszukiwanie lokalizacji — `GET /api/locations/search`

Geoapify, do 5 propozycji w Polsce. `text` 2–200 znaków, `type` = `address` (domyślnie) albo `city`. `confidence` to ocena dostawcy, nie potwierdzenie adresu. Każde wywołanie zużywa limit Geoapify; klucz nie trafia do odpowiedzi ani logów. Błąd dostawcy 503, zła odpowiedź 502, przekroczony czas 504.

### Planer — `POST /api/plans/generate`

Plan wyjazdu bez zapisu, działa dla gościa. Czasy przejazdów z macierzy atrakcji w bazie (`routing.travel_times`) i szacunku – **żadna usługa zewnętrzna nie jest wywoływana**, współrzędne noclegu trafiają tylko do linków Google Maps. Godziny jako `"HH:mm"`, daty `"RRRR-MM-DD"` (czas lokalny). Wartości domyślne i progi: sekcja `Planner` w `appsettings.json` (walidacja przy starcie – `PlannerOptionsValidatorTests`).

| Pole żądania | Działanie |
|---|---|
| `lodging` | nocleg wszystkich nocy: `latitude`, `longitude`, opcjonalnie `name` (etykieta) |
| `days[]` | 1–14 dni, daty rosnąco: `date`; `start`, `end` (domyślnie 09:00–18:00, `end` = powrót do noclegu); `lodging` – nocleg tej nocy, gdy inny (następny dzień startuje stąd); `meal` – brak = 60 min ze startem 13:30–15:00, `{"enabled":false}` = bez przerwy, `minutes` / `earliestStart` / `latestStart` (stała godzina – obie takie same) |
| `attractions[]` | 1–60 atrakcji z katalogu: `id`, `mandatory` (domyślnie `true`), `visitMinutes` (5–600, brak = czas z katalogu) |
| `interests` | kody kategorii do propozycji (pusta lista – każda kategoria) |
| `fillDays` | domyślnie `true` – pierwszy plan: planer dopełnia każdy dzień atrakcjami z katalogu (`addedByPlanner: true`); `false` – tylko wybrane |
| `includeSuggestions` | `false` – bez propozycji |

Odpowiedź: `days[]` (start/koniec, wizyty z `addedByPlanner` (dołożona przez planer, do usunięcia), `travelBefore`, `arrival`, `waitMinutes` (czekanie na otwarcie, do 60 min), `departure`, `visitMinutesSource` = `place` / `category_default` / `user`; `meal` z `afterVisits`; `returnLeg`, `returnArrival`, `overrunMinutes`, `travelMinutes`, `warnings`, `googleMapsUrls` – cały dzień autem, kilka linków przy > 9 wizytach), `skipped` (opcjonalne bez miejsca w żadnym dniu), `warnings` (całego wyjazdu, `date = null`), `suggestions` (do akceptacji – **nie są w planie**: dzień, `insertAt`, godziny, `extraTravelMinutes`, `freeMinutesLeft`, `matchedInterests`, ostrzeżenia), `travelMinutes`, `travelTimesAttribution` (podpis ORS, gdy któryś odcinek jest z macierzy). Odcinek: `mode` (`walking` / `driving`), `minutes` (z parkowaniem), `source` (`openrouteservice` / `estimate`), `isApproximate` („czas przybliżony”), `googleMapsUrl` i `googleMapsTransitUrl` (komunikacja publiczna).

Zasady planera: `docs/opis-aplikacji.md` („Ustalenia planera”). Kody ostrzeżeń: `day_overrun` (powrót po końcu dnia – do 30 min nic nie usuwamy), `optional_skipped`, `opening_hours_unknown` (tylko obiekty z wejściem), `opening_hours_unreadable`, `maybe_closed`.

Testy (20.11.2026, baza z 153 atrakcjami):
- 1 dzień (środa 2027-05-19, nocleg 50.0375, 22.0049): Zamek Lubomirskich `22ad8fa6-ad43-530b-aa56-8f65c9782c87`, Muzeum Okręgowe `2132e6f2-e2e9-5621-8891-f2d732dcf13c`, Muzeum Etnograficzne `41f02db7-7ab3-5642-b04a-be599c3f228e` z `"visitMinutes":45`, opcjonalne Muzeum Dobranocek `4e4602ff-7bfb-5ce5-8568-2a958af8e90f` i Papugarnia `32437c37-99c2-5cb7-90e5-0d2303baee6c` , `"interests":["museums"]` → 200, 5 wizyt, posiłek 14:07–15:07, powrót 16:53, odcinki pieszo i autem z ORS, bez ostrzeżeń; planer nic nie dołożył (wolne 67 min, każde pozostałe muzeum trwa 90 min).
- 3 dni (20–22.05.2027, dzień 1 od 10:00, dzień 2 przeprowadzka do Łańcuta 50.0687, 22.2317, dzień 3 do 16:00 bez posiłku): Zamek w Łańcucie `4402cf21-12ab-5f1c-9802-f07ab332feb7` (180 min), Skansen w Markowej `4599b13e-2d89-5d81-9dd8-7933e670a229`, Centrum Nauki w Jasionce `6e8fbd45-8c30-5072-a405-ff61a11dc487`, Zamek Lubomirskich + opcjonalne Muzeum Okręgowe, Muzeum Historii Miasta `2a07f551-6546-5f64-a51a-def5cf48e19b`, Papugarnia, bez zainteresowań → 200 w ok. 0,6 s; wybrane rozłożone na 3 dni (zamek w Łańcucie w dniu 3, Skansen i Jasionka w dniu przeprowadzki), dni dopełnione przez planer (`addedByPlanner`: m.in. Muzeum Etnograficzne, Planty, Kasyno w Łańcucie, Powozownia, Park Zamkowy; małe – Fortepian, ławeczka, pomnik – tylko po drodze): powrót 18:00 / 17:26 / 15:56; ostrzeżenia tylko `opening_hours_unknown` (Jasionka, Kasyno). `"fillDays": false` – tylko wybrane atrakcje.
- 400: `attractions: []`, `days: []`, daty malejące, `"date":"2027-02-30"`, `"start":"25:00"`, `end` przed `start`, odwrócone okno posiłku, nieznana / usunięta atrakcja (np. Skwer `3ac53e57-a866-58c0-ba9c-2eb1f1c2e63e`), ta sama atrakcja dwa razy, nieznane zainteresowanie, brak `lodging`, `latitude: 95`, `visitMinutes: 1`.

## Konta — `/api/auth`

ASP.NET Core Identity, sesja w ciasteczku `podkarpacie.auth` (HttpOnly, SameSite=Lax, 14 dni przy `rememberMe`, przedłużane). Swagger w przeglądarce wysyła ciasteczko sam. Endpointy z `[Authorize]` dla gościa zwracają **401**, dla zalogowanego bez roli **403** (ProblemDetails, bez przekierowania na stronę logowania). Hasło: 8–128 znaków, cyfra, mała i wielka litera. Po 5 nieudanych logowaniach konto blokuje się na 15 minut.

| Endpoint | Działanie |
|---|---|
| `POST /api/auth/register` | `{"email","password"}` → **201** `{id, email, roles:["User"], createdAt}`; nie loguje. Złe dane → **400** (pola `Email`/`Password`), zajęty adres → **409** |
| `POST /api/auth/login` | `{"email","password","rememberMe"}` → **200** jak wyżej + ciasteczko. Zły e-mail lub hasło → **401** (ten sam komunikat), blokada → **429** |
| `POST /api/auth/logout` | usuwa ciasteczko → **204** (także dla gościa) |
| `GET /api/auth/me` | zalogowany → **200**; gość → **401** |

Scenariusz w Swaggerze:
1. `register` z `{"email":"test@example.com","password":"Haslo1234"}` → 201; drugi raz → 409; `"password":"krotkie"` → 400.
2. `me` → 401.
3. `login` złym hasłem → 401; dobrym → 200.
4. `me` → 200 z rolą `User`.
5. `logout` → 204; `me` → 401.
6. (opcjonalnie) 5 × złe hasło → 429 przy kolejnej próbie, także z dobrym hasłem.

### Rola Admin i `GET /api/admin/users`

Lista kont (id, e-mail, role, `createdAt`, `lockoutEnd`) – tylko `[Authorize(Roles = "Admin")]`: gość **401**, zwykły użytkownik **403**, admin **200**. Hash hasła i inne pola Identity nie trafiają do odpowiedzi.

Rolę nadaje się w Supabase (SQL Editor) – w API nie ma endpointu, który by ją nadawał:

```sql
insert into identity.user_roles (user_id, role_id)
select u.id, r.id
from identity.users u
join identity.roles r on r.normalized_name = 'ADMIN'
where u.normalized_email = upper('admin@example.com')
on conflict do nothing;
```

Odebranie: `delete from identity.user_roles where role_id = (select id from identity.roles where normalized_name = 'ADMIN') and user_id = (select id from identity.users where normalized_email = upper('admin@example.com'));`

Role są zapisane w ciasteczku przy logowaniu – **po zmianie roli wyloguj się i zaloguj ponownie** (inaczej zmiana zadziała dopiero po odświeżeniu sesji przez Identity, do 30 min).

Scenariusz: zarejestruj `admin@example.com` i `test@example.com` → zaloguj `test` → `GET /api/admin/users` **403** → wyloguj → `GET` **401** → nadaj rolę SQL-em → zaloguj `admin` → `me` pokazuje `["Admin","User"]` → `GET /api/admin/users` **200**.

## Narzędzia redakcyjne (tylko Development; poza nim 404 – atrybut `[DevelopmentOnly]`)

### `GET /api/database/status`

`{"connected":true,"schema":"catalog","placesCount":…,"categoriesCount":…,"accountsSchema":"identity","usersCount":…,"roles":["Admin","User"]}` – szybki test połączenia (liczy wszystkie rekordy, także usunięte i elementy). Przed migracją kont (`Identity`) zwraca 503 – brak tabel `identity.*`.

### Szacunek czasu przejazdu — `GET /api/dev/routing/estimate`

`?fromLatitude=&fromLongitude=&toLatitude=&toLongitude=` (wszystkie wymagane, brak lub poza zakresem → 400). Zwraca `straightLineMeters`, `chosen` (tryb, który wybrałby planer), `walking` i `driving` (oba tryby: `mode`, `minutes` z parkowaniem, `distanceMeters` = linia prosta × współczynnik, `source: "estimate"`, `isApproximate: true`) oraz użyte parametry `Routing:Estimate`. Nie wysyła zapytań na zewnątrz. Do ręcznego porównania z Google Maps, np.:
- Rynek Rzeszów → Zamek Łańcut: `50.0375, 22.0049 → 50.0687, 22.2317` (ok. 16,5 km w linii prostej; samochodem),
- Rynek → Zamek Lubomirskich: `50.0375, 22.0049 → 50.0333, 22.0018` (kilkaset metrów; pieszo).

### Macierz atrakcji z ORS — `POST /api/dev/routing/matrix`

`?profile=driving-car|foot-walking&dryRun=true&maxRequests=10` (1–100). Bierze aktywne atrakcje, pomija pary już zapisane w `routing.travel_times` (także „brak trasy”) i dalsze niż próg w linii prostej (`Routing:Matrix`: samochód 80 km – w MVP wszystkie pary, pieszo 3 km), dzieli resztę na zapytania ≤ 3500 par. `dryRun=true` tylko liczy (`missingPairs`, `requestsNeeded`), bez wysyłania do ORS i bez klucza. `dryRun=false` wysyła najwyżej `maxRequests` zapytań co 2 s i zapisuje po każdym; `remainingRequests` > 0 → wywołaj ponownie. Każde zapytanie zużywa 1 z 500 dziennie (plan Standard). Odpowiedź zawiera podpis i licencję ORS (CC-BY-SA 4.0). Klucz: `.local/ors-key.txt` (`OpenRouteService:ApiKey`), nie trafia do odpowiedzi ani logów; wysyłamy tylko współrzędne atrakcji.

Błędy: zły profil / `maxRequests` → 400; brak klucza → 503; limit ORS (429), odrzucony klucz (401/403), odrzucone zapytanie (400 od ORS) albo niedostępność → 503 z treścią błędu ORS; zła odpowiedź → 502; przekroczony czas → 504. W `detail` – ile par zapisano przed błędem.

Zmiana współrzędnych atrakcji nie odświeża jej czasów – usuń jej wiersze (`delete from routing.travel_times where from_place_id = … or to_place_id = …`) i wywołaj ponownie.

Scenariusz: `profile=driving-car&dryRun=true` → np. 185 atrakcji, ok. 34 tys. par, kilkanaście–kilkadziesiąt zapytań; `dryRun=false&maxRequests=1` → `pairsSaved` > 0; potem `maxRequests=100` do `remainingRequests = 0`; ponowne `dryRun=true` → `missingPairs = 0`. To samo dla `foot-walking` (dużo mniej par).

### Kalibracja szacunku — `GET /api/dev/routing/calibration`

Tylko odczyt: bierze pary z `routing.travel_times` (z trasą), liczy odległość w linii prostej i porównuje szacunek z czasem ORS (bez parkowania – ORS go nie zawiera). Pomija pary bliższe niż 200 m. Zwraca:
- `current` – obecne `Routing:Estimate`, `suggested` – dopasowane: współczynnik objazdu = mediana (trasa ORS ÷ linia prosta); jazda – regresja liniowa czasu względem szacowanej trasy (`drivingOverheadMinutes` = wyraz wolny, `drivingSpeedKmh` z nachylenia); pieszo – regresja przez zero. `parkingMinutes` i `maxWalkingMinutes` bez zmian;
- `errors` – błąd przed i po dla przedziałów (auto: do 2 km, 2–10, powyżej 10; pieszo: do 1 km, 1–3) i łącznie: mediana i 90. percentyl błędu bezwzględnego w minutach, mediana błędu w %, średni błąd ze znakiem (> 0 – szacunek zawyża).

Za mało par (< 20) dla trybu → jego parametry bez zmian. Nowe wartości trzeba przepisać ręcznie do `appsettings.json` i uruchomić aplikację ponownie.

Czasy dla planera (`Services/TravelTimeService` → `Planning/TravelTimeMatrix`): para atrakcji z bazy – pieszo z ORS, jeśli marsz ≤ `maxWalkingMinutes`, inaczej autem z ORS + parkowanie; nocleg, para bez trasy lub spoza macierzy – szacunek (`source: "estimate"`, czas przybliżony). Endpoint planera – E4.6.

### Strona `/dev/zdjecia` (`DevTools/zdjecia.html`)

| Endpoint | Działanie |
|---|---|
| `GET /api/dev/photos/queue` | propozycje do przeglądu + wszystkie atrakcje z liczbą zdjęć |
| `POST /api/dev/attractions/{id}/photo-candidates?limit=5&radiusMeters=50&kind=place\|exhibit` | propozycje z Wikimedia Commons, zapis jako `pending_review` |
| `GET /api/dev/attractions/{id}/photos` | wszystkie zdjęcia atrakcji (każdy status) |
| `POST /api/dev/photos/{photoId}/review` | `{"status": "approved"\|"rejected"\|"pending_review", "kind": "place"\|"exhibit"}` (jedno lub oba) |
| `POST /api/dev/photos/{photoId}/primary` | zdjęcie główne / pierwsze w galerii (tylko zatwierdzone) |
| `POST /api/dev/photos/repair?dryRun=true&limit=50` | brakujące pliki Wikimedia pobiera ponownie z `thumbnail_url`; bez zmian w bazie |

Wyszukiwanie propozycji (`WikimediaCommonsService.FindCandidatesAsync`): zdjęcia miejsca – kategoria Commons obiektu z Wikidata (P373; `subject:wikidata` – osoba/temat pomnika – pomijany) i do 3 podkategorii, potem po nazwie, potem w promieniu; wnętrza/eksponaty – tylko podkategorie kolekcji, wnętrz i wystaw. Filtr licencji: CC BY / CC BY-SA 1.0–4.0 (z 2.5 i wersjami krajowymi), CC0, domena publiczna, z autorem, bez ograniczeń, JPEG/PNG/WebP. Pliki już znane (także odrzucone) są pomijane. Pominięte zwracane do podglądu (`skipped`).

Strona: zakładki **Propozycje** (klawisze A zatwierdź, R odrzuć, S/→ pomiń, ← poprzednie, U cofnij), **Miejsca** (filtry, „Szukaj zdjęć”, „Szukaj wnętrz i eksponatów”), **Atrakcje – wszystkie zdjęcia** (zmiana każdej decyzji, zdjęcie główne – zielona ramka); pod spodem miniatury pominiętych plików.

### Strona `/dev/atrakcje` (`DevTools/atrakcje.html`)

| Endpoint | Działanie |
|---|---|
| `GET /api/dev/places/overture-candidates` | kandydaci z `data/overture/<wydanie>/candidates.json` (`scripts/fetch_overture_places.py`) z oceną duplikatów; brak wyciągu → 404 z instrukcją |
| `POST /api/dev/places/duplicates` | `name`, `latitude`, `longitude`, opcjonalnie `overtureId` → możliwe duplikaty |
| `POST /api/dev/places` | nowa aktywna atrakcja (z Overture: tylko `overtureId` + kategoria/czas – dane i licencję serwer bierze z wyciągu; ręcznie: wszystkie pola) → 201, 409 przy duplikacie, 400 przy nieznanej kategorii |

Zakładki: **Z Overture** (filtry: tekst, „ukryj już w katalogu”, „pewność ≥ 0,8”, kategoria) i **Dodaj ręcznie** (adres przez Geoapify → współrzędne). Porównanie źródeł: `docs/zrodla-danych.md`.

Ochrona przed duplikatami (`Services/PlaceDuplicates.cs`, testy `PlaceDuplicatesTests`), porównanie z całym katalogiem (także usuniętymi):

| Poziom | Kiedy | Skutek |
|---|---|---|
| `same_source` | ten sam obiekt w źródle: OSM z importu Geoapify (`osm_id`) albo Overture (`overture:<id>`) | blokada (409) |
| `likely` | do 150 m i podobna nazwa albo do 1 km i ta sama nazwa (bez wielkości liter, polskich znaków i słów ogólnych) | 409, dodanie po potwierdzeniu (`confirmDuplicate: true`) |
| `nearby` | do 25 m, inna nazwa | tylko informacja |
