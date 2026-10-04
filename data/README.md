# Dane do wersji beta

## Aktualna kopia po ręcznych decyzjach

`curated-beta-v1/catalog.json` zawiera katalog po decyzjach R01–R19. Szczegóły w `curated-beta-v1/README.md`.
518 samodzielnych wpisów (485 aktywnych i 33 murale oczekujące na zdjęcia), 7 elementów w obiektach nadrzędnych, 52 tablice do przypisania i 23 usunięte wpisy zachowane w historii. Dane źródłowe niżej pozostają bez zmian.

## Pobrany zestaw

Katalog rozszerzony do **600 rekordów**: `geoapify/20261004T095555262149Z/batch-02/beta-places-600.json`.
Sama druga partia: `geoapify/20261004T095555262149Z/batch-02/beta-places-next-300.json`.
Drugą partię dobrano z zachowanych kandydatów Geoapify bez nowych wywołań API.
Wykluczono identyfikatory Geoapify i pary OSM typ/ID obecne w pierwszej partii oraz potencjalne duplikaty o tej samej znormalizowanej nazwie w promieniu 150 m. Kontrole obejmują też wnętrze drugiej partii.
Nie dowodzi to braku duplikatów o różnych nazwach. Pierwsze 300 rekordów zachowano bez zmian.
Szczegóły: `geoapify/20261004T095555262149Z/batch-02/quality-report.json`.
Nowa partia: 132 zabytki, 123 obiekty przyrodnicze/parki i 45 pozostałych atrakcji; 31 wpisów z miejscowością Rzeszów, 269 z okolic. Tylko 1 nowy wpis zawiera godziny otwarcia, 14 ma stronę WWW. Selekcja nie jest potwierdzeniem przydatności turystycznej ani aktualności danych.

4 października 2026 zapisano **300 miejsc** w `geoapify/20261004T095555262149Z/beta-places.json`.
Podsumowanie i ograniczenia: `geoapify/20261004T095555262149Z/RAPORT.md`.
To roboczy zestaw do bety, jeszcze bez integracji z endpointem aplikacji.

## Ponowne pobieranie

Docelowo około 300 nazwanych miejsc z Geoapify wokół Rzeszowa.
Skrypt `../scripts/download_geoapify_beta.py` pobiera dane bez uruchamiania aplikacji .NET.
Wymaga klucza zapisanego lokalnie w `../.local/geoapify-key.txt` (katalog ignorowany przez Git).

Po pobraniu powstanie folder `geoapify/<czas-UTC>/` zawierający:

- `raw-*.geojson`: źródłowe odpowiedzi Geoapify, do ponownego przetwarzania bez kolejnych zapytań;
- `beta-places.json`: wybrane maksymalnie 300 miejsc i metadane pobrania;
- `candidates.json`: wszyscy poprawni kandydaci z tej próby;
- `incomplete.json`: tylko przy przerwaniu pobierania; nie oznacza gotowego katalogu.

Najpierw promień 30 km; przy mniej niż 300 kandydatach rozszerzenie do 50 km.
Sześć grup: muzea i galerie, zabytki (w tym obiekty sakralne), przyroda, punkty widokowe, rekreacja i pozostałe atrakcje.
Z każdej grupy pobieramy do trzech stron po 100 rekordów dla danego promienia.
Maksymalnie 36 żądań Places API, bez automatycznych ponowień i bez wywołań Place Details.
Nie jest to pełne przeszukanie regionu; jeśli kandydatów jest za mało, wynik pozostaje mniejszy niż 300.
Selekcja na zmianę z grup, zaczynając od najbliższych miejsc; identyfikator Geoapify usuwa powtórzenia między stronami/grupami.

Nie uzupełniamy fikcyjnych godzin, opinii i czasów zwiedzania. Zestaw wymaga przeglądu pod kątem duplikatów, przydatności turystycznej i jakości lokalizacji. Nie zawiera jeszcze czasów przejazdów.
Po zatwierdzeniu danych dodamy ich odczyt w aplikacji — obecny endpoint Overpass pozostaje bez zmian.

Źródła: [Geoapify Places API](https://apidocs.geoapify.com/docs/places/), [zasady przechowywania i oznaczenia źródła](https://www.geoapify.com/places-api/).
Przy prezentacji danych należy zachować oznaczenia Geoapify i OpenStreetMap oraz wymagane odnośniki.
