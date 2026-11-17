# API — opis endpointów i testy w Swaggerze

Swagger: `http://localhost:5201/swagger` (profil `http`, tylko środowisko Development). Wszystkie błędy zwracane są jako `ProblemDetails` z polskim opisem. Błąd bazy lub dostawcy zewnętrznego daje 502/503/504, nigdy pozornie poprawną pustą listę.

Zasada dla wszystkich odpowiedzi: `null` oznacza brak danych, nie wartość domyślną. `openingHours=null` to nieznane godziny, nie „całodobowo”.

## Kategorie — `GET /api/attraction-categories`

Słownik 9 kategorii (`code`, `name`), np. `museums`, `nature`, `public-art`. Jedna atrakcja może mieć kilka kategorii (np. muzeum w zamku).

Test: HTTP 200 i 9 pozycji.

## Obszary — `GET /api/regions`

Obszary katalogu z `Inżynierka/appsettings.json`, sekcja `Catalog:Regions` (kod, nazwa, środek, promień, `isDefault`). Na start jeden: `rzeszow`, „Rzeszów i okolice”, 50.037, 22.004, 30 km. Kolejny obszar = nowy wpis w tablicy; dokładnie jeden ma `IsDefault: true`.

Testy:
1. HTTP 200, `items` z obszarem `rzeszow`, `isDefault: true`. Nie wymaga bazy.
2. Walidacja przy starcie: zmień tymczasowo `RadiusKm` na `0` → aplikacja nie wystartuje, komunikat `Catalog:Regions:0:RadiusKm`. Przywróć `30`.

Reguły walidacji: testy `Inżynierka.Tests/CatalogOptionsValidatorTests.cs`.

## Lista atrakcji — `GET /api/attractions`

Tylko aktywne atrakcje (`kind='attraction'`, `status='active'`); pomija usunięte, murale czekające na zdjęcie i elementy obiektów.

| Parametr | Działanie |
|---|---|
| `page` | Strona od 1; domyślnie 1, maks. 1 000 000. |
| `pageSize` | 1–100; domyślnie 20. |
| `search` | Fragment nazwy (do 100 znaków), bez rozróżniania wielkości liter; `%` i `_` jako zwykłe znaki. |
| `category` | Kod z `GET /api/attraction-categories`; nieznany → 400. |
| `city` | Zgodność pola miasta (bez wielkości liter i spacji na końcach). Bez geokodowania i granic administracyjnych. |
| `latitude`, `longitude`, `radiusKm` | Promień od punktu; wszystkie trzy razem, `radiusKm` 0,1–100. Nie łączyć z `city`. |
| `requirePhoto` | Tylko atrakcje z zatwierdzonym zdjęciem. |

Odpowiedź: `items`, `totalCount`, `page`, `pageSize`, `totalPages`. Element: id, nazwa, miasto, adres, współrzędne, kategorie, godziny (tekst źródłowy, bez interpretacji), `distanceKm` (tylko w promieniu), `hasPhoto`, `pendingPhotoCount`. Kolejność: w promieniu odległość → nazwa → id; inaczej nazwa → id. Odległość PostGIS w linii prostej, nie po drogach.

Trzy sposoby wyboru miejsca (backend nie wybiera lokalizacji za użytkownika):

| Opcja | Jak |
|---|---|
| Miasto | `?city=Rzeszów` |
| Miasto i okolice | `GET /api/locations/search?type=city` → współrzędne propozycji + `radiusKm` |
| Hotel / adres | `GET /api/locations/search?type=address` → współrzędne propozycji + `radiusKm` |

Testy (stan po selekcji z 5.10.2026):
1. `/api/attractions` → `totalCount=152`, 20 elementów, `totalPages=8`.
2. `?page=2` → kolejna strona bez powtórzeń ze strony 1.
3. `?category=museums` → każda pozycja ma kategorię `museums`.
4. `?search=zamek` → każda nazwa zawiera „zamek”.
5. `?latitude=50.037&longitude=22.004&radiusKm=10` → `totalCount=74`, rosnące `distanceKm`.
6. `?city=Rzeszów` → 65 pozycji.
7. `?page=1000` → 200, `items=[]`, `totalCount=152`.
8. Błędy 400: `?page=0`, `?pageSize=101`, `?category=nieznana`, samo `?latitude=50`, `?radiusKm=0` z kompletem współrzędnych, `?city=Rzeszów` razem z promieniem.

## Szczegóły atrakcji — `GET /api/attractions/{id}`

Jedna aktywna atrakcja: nazwa, opis, adres, współrzędne, strona WWW, kategorie, godziny z weryfikacją, `estimatedVisitMinutes`, `components` (elementy obiektu, np. tablice w parku), zatwierdzone zdjęcia z autorem i licencją, `attribution` (źródło danych). Opisy i czasy wizyt są na razie puste (zadania 1.2 i 1.5).

| Przypadek | `id` | Wynik |
|---|---|---|
| Aktywna, ze zdjęciem i elementem | `4402cf21-12ab-5f1c-9802-f07ab332feb7` (Muzeum – Zamek w Łańcucie) | 200, 1 zdjęcie, `components` niepuste |
| Aktywna, wiele elementów | `35218f72-3197-51c7-a458-2b77cc3da29e` (Park Zamkowy) | 200 |
| Aktywna, bez zdjęć | `41f02db7-7ab3-5642-b04a-be599c3f228e` (Muzeum Etnograficzne) | 200, `photos=[]` |
| Usunięta w selekcji | `3ac53e57-a866-58c0-ba9c-2eb1f1c2e63e` (Skwer im. K. Górskiego) | 404 |
| Mural czekający na zdjęcie | `c7bd67ab-a017-5ad2-9012-0acd29a87e7d` (Irena Sendler) | 404 |
| Element obiektu | `460717b8-9b5a-5224-8b93-17e9187d466c` (Taniec Życia) | 404 |
| Nieistniejąca | `00000000-0000-0000-0000-000000000000` | 404 |

## Zdjęcia — `GET /api/attractions/{id}/photos` i `.../photos/{photoId}/image`

Lista zatwierdzonych zdjęć (autor, licencja, link do źródła, `imageUrl`) i sam plik obrazu. W Development `?includePending=true` pokazuje też propozycje do przeglądu. Pliki leżą lokalnie w `.local/photo-cache` — brak pliku daje 404 (część plików wymaga ponownego pobrania, zadanie 1.4).

Automat pobierający propozycje zdjęć z Wikimedia Commons: `scripts/fetch_place_photos.py`, opis w `data/photos/README.md`.

## Wyszukiwanie lokalizacji — `GET /api/locations/search`

Geokodowanie Geoapify, do 5 propozycji w Polsce.

- `text`: 2–200 znaków; `type`: `address` (domyślnie) lub `city`.
- Odpowiedź: `items` (`placeId`, `formattedAddress`, `city`, `resultType`, `latitude`, `longitude`, `confidence`), `provider`, `attribution`.
- `confidence` to ocena dostawcy, nie potwierdzenie adresu; wynik może wskazywać ulicę lub miasto zamiast budynku. Hotel najlepiej szukać po ulicy, numerze i miejscowości.
- Każde wywołanie zużywa limit konta Geoapify. Klucz nie trafia do odpowiedzi ani logów.

Testy: `?text=Rzeszów&type=city` → 1 propozycja; `?text=Rynek 1, Rzeszów` → kilka propozycji; 400 dla `?text=a` i `?type=hotel`; brak klucza → 503.

## Diagnostyka — `GET /api/database/status` (tylko Development)

Sprawdza połączenie z Supabase. Oczekiwane: `{"connected":true,"schema":"catalog","placesCount":600,"categoriesCount":9}`. `placesCount` liczy wszystkie rekordy, także usunięte i elementy. 503 = problem z konfiguracją, hasłem, siecią albo uśpiony projekt Supabase.
