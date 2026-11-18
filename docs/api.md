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
