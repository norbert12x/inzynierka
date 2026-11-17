# API — opis endpointów i testy w Swaggerze

Swagger: `http://localhost:5201/swagger` (profil `http`, tylko środowisko Development). Wszystkie błędy zwracane są jako `ProblemDetails` z polskim opisem. Błąd bazy lub dostawcy zewnętrznego daje 502/503/504, nigdy pozornie poprawną pustą listę.

Zasada dla wszystkich odpowiedzi: `null` oznacza brak danych, nie wartość domyślną. `openingHours=null` to nieznane godziny, nie „całodobowo”.

## Kategorie — `GET /api/attraction-categories`

Słownik 9 kategorii z bazy (`code`, `name`, `defaultVisitMinutes`), np. `museums`, `nature`, `public-art`; „Pozostałe atrakcje” na końcu. Jedna atrakcja może mieć kilka kategorii (np. muzeum w zamku).

Test: HTTP 200 i 9 pozycji, każda z `defaultVisitMinutes` (np. `museums` = 90, `public-art` = 10). Bez wykonanego `database/005_category_visit_minutes.sql` → 503.

## Czas wizyty

Lista i szczegóły atrakcji zwracają `visitMinutes` i `visitMinutesSource`:

- `place` – czas zapisany przy atrakcji (`estimated_visit_minutes`, np. ręczna korekta dla szczytu lub szlaku),
- `category_default` – szacunek: najdłuższy `defaultVisitMinutes` spośród kategorii atrakcji; bez kategorii – czas kategorii `other`.

Logika: `Inżynierka/Planning/VisitDuration.cs`, testy `Inżynierka.Tests/VisitDurationTests.cs`. Interfejs powinien pokazywać szacunek jako przybliżony („ok. 90 min”).

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

Odpowiedź: `items`, `totalCount`, `page`, `pageSize`, `totalPages`. Element: id, nazwa, miasto, adres, współrzędne, kategorie, godziny (tekst źródłowy, bez interpretacji), `visitMinutes` + `visitMinutesSource`, `distanceKm` (tylko w promieniu), `hasPhoto`, `pendingPhotoCount`. Kolejność: w promieniu odległość → nazwa → id; inaczej nazwa → id. Odległość PostGIS w linii prostej, nie po drogach.

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

Jedna aktywna atrakcja: nazwa, opis, adres, współrzędne, strona WWW, kategorie, godziny z weryfikacją, `visitMinutes` + `visitMinutesSource`, `components` (elementy obiektu, np. tablice w parku), zatwierdzone zdjęcia z autorem i licencją – osobno `photos` (sama atrakcja) i `exhibits` (eksponaty/wnętrza muzeum, z podpisem `caption`), `attribution` (źródło danych). Opisy są na razie puste (zadanie 1.5). Przykład: Muzeum – Zamek w Łańcucie → `visitMinutes=90`, `visitMinutesSource="category_default"`.

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

Lista zatwierdzonych zdjęć (autor, licencja, link do źródła, `imageUrl`, `kind`: `place`/`exhibit`, `caption`) – najpierw zdjęcia miejsca, potem eksponaty – i sam plik obrazu. `hasPhoto` i `requirePhoto` w liście atrakcji liczą tylko zdjęcia miejsca. W Development `?includePending=true` pokazuje też propozycje do przeglądu. Pliki leżą lokalnie w `.local/photo-cache` — brak pliku daje 404 (część plików wymaga ponownego pobrania, zadanie 1.4).

Automat pobierający propozycje zdjęć z Wikimedia Commons: `scripts/fetch_place_photos.py`, opis w `data/photos/README.md`.

## Naprawa brakujących plików zdjęć — `POST /api/dev/photos/repair` (tylko Development)

Sprawdza każdy wpis zdjęcia w bazie (bez odrzuconych) i czy jego plik jest w `.local/photo-cache`. Brakujące pliki Wikimedia pobiera ponownie z `thumbnail_url` zapisanego w bazie (te same zasady co skrypt Python: tylko hosty Wikimedia, maks. 8 MB, JPEG/PNG/WebP, 0,4 s odstępu). **Nie zmienia rekordów w bazie.** Zdjęcia własne (`user_provided`) trzeba skopiować ręcznie.

| Parametr | Działanie |
|---|---|
| `dryRun` | `true` (domyślnie) – tylko raport braków; `false` – pobieranie. |
| `limit` | Maks. liczba pobrań w jednym wywołaniu, 1–200 (domyślnie 50). |

Odpowiedź: `checked`, `present`, `missing`, `downloaded`, `failed`, `remaining` i lista `items` (tylko braki) z `outcome`: `missing`, `downloaded`, `failed` (z opisem), `manual_required`, `invalid_path`.

Testy:
1. `dryRun=true` → lista braków, nic nie pobrane (stan 17.11: ok. 74 wpisy, z czego część bez pliku).
2. `dryRun=false` → `downloaded` > 0; potem Biały Ogród (`/api/attractions/73fb5dd6-f1f9-50bf-ad22-2dc1bf47f37b/photos/e230b824-55b8-5e2a-87be-9fb58255cc9d/image?includePending=true`) pokazuje zdjęcie.
3. Ponowne `dryRun=true` → `missing` = 0 (albo tylko `failed`/`manual_required`).
4. Poza Development → 404.

## Propozycje zdjęć atrakcji — `POST /api/dev/attractions/{id}/photo-candidates` (tylko Development)

Szuka w Wikimedia Commons kilku zdjęć jednej atrakcji. Kategorię Commons obiektu bierze z Wikidata (P373) na podstawie tagu OSM `wikidata` z `place_sources.raw_data`; tag `subject:wikidata` (osoba lub temat pomnika, np. „Adam Mickiewicz”) jest celowo pomijany. Stan 17.11: ok. 30 atrakcji ma własną kategorię (razem ok. 680 plików).

- `kind=place` (domyślnie): pliki z kategorii obiektu i do 3 jej podkategorii (bez kolekcji/wnętrz), potem po nazwie i mieście, na końcu z geotagiem w promieniu `radiusMeters`.
- `kind=exhibit` (muzea): tylko pliki z podkategorii kolekcji/wnętrz/wystaw (np. „Collections of Rzeszów Regional Museum”, „Interior of the Łańcut Castle”), do 5 podkategorii. Podpis eksponatu (`caption`) z opisu pliku. Bez kategorii → `note` z wyjaśnieniem.

 Przepuszcza tylko CC BY / CC BY-SA 1.0, 2.0, 2.5, 3.0, 4.0 (także krajowe, np. „3.0 pl”), CC0 i domenę publiczną, z autorem i linkiem licencji, w JPEG/PNG/WebP. Pobiera miniaturę 1280 px do `.local/photo-cache` i **zapisuje w bazie jako `pending_review`** (niewidoczne w publicznym API). Pliki już znane dla atrakcji – także odrzucone – są pomijane, więc odrzucone zdjęcie nie wraca.

| Parametr | Działanie |
|---|---|
| `limit` | Ile nowych propozycji zapisać, 1–20 (domyślnie 5). |
| `radiusMeters` | 0–500 m (domyślnie 50); 0 = bez wyszukiwania w promieniu. Większy promień = więcej sąsiednich budynków. |
| `kind` | `place` (domyślnie) albo `exhibit`. |

Odpowiedź: `kind`, `commonsCategory`, `note`, `added` (z `imageUrl`, `kind`, `caption`), `skipped` (z powodem, miniaturą prosto z Wikimedia, licencją i autorem – tylko podgląd, bez zapisu; typowe powody: brak autora, dodatkowe ograniczenia, format TIFF/SVG), `alreadyKnown`.

## Decyzja o zdjęciu — `POST /api/dev/photos/{photoId}/review` (tylko Development)

Body: `{"status": "approved"}` – zdjęcie widoczne w API; `"rejected"` – odrzucone; `"pending_review"` – cofnięcie decyzji. Opcjonalnie `"kind": "place" | "exhibit"` – przeniesienie między galerią miejsca a wnętrzami/eksponatami (można podać samo `kind`). Nowo zatwierdzone zdjęcie trafia na koniec galerii. Brak obu pól lub inna wartość → 400, nieznane zdjęcie → 404.

## Zdjęcie główne — `POST /api/dev/photos/{photoId}/primary` (tylko Development)

Ustawia zatwierdzone zdjęcie jako pierwsze w jego galerii (`sort_order` = 0, pozostałe 1, 2, …). Pierwsze zatwierdzone zdjęcie rodzaju `place` to zdjęcie główne atrakcji – publiczne API zwraca zdjęcia w kolejności `sort_order`. Niezatwierdzone → 400.

## Wszystkie zdjęcia atrakcji — `GET /api/dev/attractions/{id}/photos` (tylko Development)

Zatwierdzone, propozycje i odrzucone, z `kind`, `caption`, `sortOrder`, `isPrimary`, informacją o pliku na dysku i identyfikatorem Wikidata (`commonsHint`).

### Strona przeglądu — `http://localhost:5201/dev/zdjecia` (tylko Development)

Wygodna alternatywa dla Swaggera (plik `Inżynierka/DevTools/zdjecia.html`, dane z `GET /api/dev/photos/queue`):

- **Propozycje** – zdjęcia `pending_review` po kolei, z autorem, licencją, źródłem i linkiem do mapy; miniatury innych propozycji tego samego miejsca. Klawisze: **A** zatwierdź, **R** odrzuć, **S** / → pomiń, ← poprzednie, **U** cofnij ostatnią decyzję. Przy braku pliku przycisk „Pobierz brakujące pliki” (repair). „Szukaj więcej zdjęć tego miejsca” uruchamia `photo-candidates`.
- **Miejsca** – aktywne atrakcje i murale z liczbą zatwierdzonych zdjęć, eksponatów i propozycji; filtry „tylko bez zdjęcia” i „tylko muzea”; przyciski „Szukaj zdjęć” i (dla muzeów) „Szukaj eksponatów”. Propozycje eksponatów mają żółtą etykietę „eksponat” i opis.
- **Atrakcje – wszystkie zdjęcia** – jedna atrakcja na raz (← / → albo lista), wszystkie jej zdjęcia w grupach: zatwierdzone zdjęcia miejsca, zatwierdzone wnętrza i eksponaty, propozycje, odrzucone. Przy każdym zdjęciu: „Ustaw jako główne”, zatwierdź, odrzuć, „Do przeglądu”, przeniesienie między zdjęciem miejsca a wnętrzem/eksponatem. Główne zdjęcie ma zieloną ramkę. Filtr „tylko z jakimkolwiek zdjęciem”.
- Przycisk „Szukaj wnętrz i eksponatów” jest przy każdej atrakcji (nie tylko muzeach) – np. wnętrza pałaców i kościołów.
- **Pominięte** – po każdym wyszukiwaniu pod spodem miniatury plików odrzuconych przez filtr, z powodem i linkiem do Commons (tylko podgląd).

Poza Development API zwraca 404, więc strona nic nie pokaże. Docelowo zastąpi ją panel administratora (E6.6).

### Przegląd zdjęć krok po kroku w Swaggerze (przykład: Biały Ogród `73fb5dd6-f1f9-50bf-ad22-2dc1bf47f37b`)

1. `POST /api/dev/attractions/73fb5dd6-f1f9-50bf-ad22-2dc1bf47f37b/photo-candidates` → lista `added`.
2. Każdy `imageUrl` otwórz w przeglądarce z prefiksem `http://localhost:5201`.
3. Najlepsze: `POST /api/dev/photos/{photoId}/review` z `{"status":"approved"}`; pozostałe (i stare, nieudane): `{"status":"rejected"}`.
4. `GET /api/attractions/73fb5dd6-f1f9-50bf-ad22-2dc1bf47f37b` → `photos` zawiera zatwierdzone zdjęcia.

## Wyszukiwanie lokalizacji — `GET /api/locations/search`

Geokodowanie Geoapify, do 5 propozycji w Polsce.

- `text`: 2–200 znaków; `type`: `address` (domyślnie) lub `city`.
- Odpowiedź: `items` (`placeId`, `formattedAddress`, `city`, `resultType`, `latitude`, `longitude`, `confidence`), `provider`, `attribution`.
- `confidence` to ocena dostawcy, nie potwierdzenie adresu; wynik może wskazywać ulicę lub miasto zamiast budynku. Hotel najlepiej szukać po ulicy, numerze i miejscowości.
- Każde wywołanie zużywa limit konta Geoapify. Klucz nie trafia do odpowiedzi ani logów.

Testy: `?text=Rzeszów&type=city` → 1 propozycja; `?text=Rynek 1, Rzeszów` → kilka propozycji; 400 dla `?text=a` i `?type=hotel`; brak klucza → 503.

## Diagnostyka — `GET /api/database/status` (tylko Development)

Sprawdza połączenie z Supabase. Oczekiwane: `{"connected":true,"schema":"catalog","placesCount":600,"categoriesCount":9}`. `placesCount` liczy wszystkie rekordy, także usunięte i elementy. 503 = problem z konfiguracją, hasłem, siecią albo uśpiony projekt Supabase.
