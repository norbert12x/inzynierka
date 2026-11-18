# Źródła danych o atrakcjach – porównanie (18.11.2026)

Cel: lepsze pokrycie „nowoczesnych” atrakcji (laser tag, escape roomy, parki trampolin, gokarty, paintball, park linowy) i aktywnego wypoczynku (kajaki, jazda konna, quady) – za darmo i zgodnie z licencją, czyli z prawem do **zapisania danych w naszej bazie** (katalog i planer potrzebują ich na stałe).

## Wynik dla okolic Rzeszowa (ok. 30 km)

| Źródło | Ile miejsc aktywnej rozrywki | Laser tag | Kajaki | Zapis w bazie | Koszt / dostęp |
|---|---|---|---|---|---|
| OpenStreetMap (Overpass) | ok. 15 | brak | brak | tak (ODbL, z oznaczeniem) | darmowe; 18.11 wszystkie 3 publiczne serwery przeciążone (502/504) |
| Geoapify Places | jak OSM (te same dane) | brak | brak | tak (z oznaczeniem) | klucz już mamy, darmowy limit |
| **Overture Maps Places** | **ok. 110 kandydatów** (77 z pewnością ≥ 0,8) | **Laser Arena Rzeszów, Laserowa Twierdza** | **Dolina Wisłoka, Spływy kajakowe LOLEK, Adventure&Passion, Przystań w Dolinie Sanu, szlak Wisłoka** | **tak** (CDLA Permissive 2.0 / Apache 2.0 / CC0, z oznaczeniem) | darmowe pliki Parquet na S3, bez konta |
| Foursquare Places API | dobre | – | – | **nie** (Pay as You Go: zapis danych poza identyfikatorem zabroniony) | konto + klucz |
| Google Places | bardzo dobre | – | – | **nie** (regulamin) | decyzja: bez Google |
| Foursquare OS Places (pełny zbiór) | prawdopodobnie lepsze niż część w Overture | – | – | tak (Apache 2.0) | wymaga konta Hugging Face i akceptacji warunków |
| Wikivoyage, dane.gov.pl, geoportal województwa | brak przydatnych list z współrzędnymi | – | – | – | – |

## Overture Maps Places – szczegóły

- Fundacja Linux Foundation (Amazon, Meta, Microsoft, TomTom). Ok. 88 mln miejsc na świecie; dane od Meta (strony firm), BrightQuery, Microsoft, Foursquare, AllThePlaces i innych. **Nie zawiera danych OSM** – dlatego uzupełnia nasz katalog z Geoapify/OSM.
- Wydania co miesiąc; starsze wydania znikają po ok. 60 dniach, więc wyciąg zapisujemy u siebie.
- W prostokącie wokół Rzeszowa: 19 649 miejsc (plik 3,4 MB, pobranie z S3 w 21 s przez DuckDB – czyta tylko potrzebne fragmenty plików).
- Przydatne pola: `taxonomy` (np. `escape_room`, `paintball`, `canoe_and_kayak_hire_service`, `atv_rental_tour`, `go_kart_club`, `horse_riding`, `water_park`, `amusement_park`), `confidence` (0–1, czy miejsce istnieje), `operating_status` (np. `permanently_closed`), `websites`, `phones`, `addresses`, `sources`.
- Słabsze strony: część miejsc źle skategoryzowana (kręgielnia jako „dance_club”, park trampolin jako „park”) – filtr łączy kategorie ze słowami w nazwie; zdarzają się powtórzenia i pozycje „z Facebooka” (np. sklep albo producent zamiast atrakcji) oraz przybliżone współrzędne → **każde miejsce przechodzi przegląd** przed dodaniem (jak zdjęcia) i ochronę przed duplikatami (`PlaceDuplicates`).
- Oznaczenie źródła (wymagane): „Data from Meta. Available under CDLA Permissive 2.0.”, „Data from Microsoft. Available under CDLA Permissive 2.0.”, dla Foursquare: „Copyright 2024 Foursquare Labs, Inc. All rights reserved.” (Apache 2.0), AllThePlaces: CC0 1.0 – zapisujemy przy miejscu i pokazujemy w szczegółach atrakcji. Łączenie z danymi OSM jest dozwolone; nasza baza nie jest publikowana jako zbiór, aplikacja pokazuje dane z oznaczeniami.

Źródła: [Overture – Places Guide](https://docs.overturemaps.org/guides/places), [Overture – atrybucja](https://docs.overturemaps.org/attribution/), [Foursquare – zasady użycia API](https://docs.foursquare.com/developer/reference/usage-guidelines-personalization-apis), [FSQ OS Places (Hugging Face)](https://huggingface.co/datasets/foursquare/fsq-os-places).

## Rekomendacja

1. **Overture jako główne źródło aktywnej rozrywki** – skrypt w `scripts/` pobiera wyciąg dla obszarów z `appsettings.json`, filtr (kategorie + słowa kluczowe), wynik do `data/overture/<wydanie>/`; strona `/dev/atrakcje` dostaje zakładkę „Z Overture” z tym samym przeglądem, ochroną przed duplikatami i dodawaniem (źródło `overture`, identyfikator Overture, oznaczenie licencji).
2. ~~OSM (Overpass) jako uzupełnienie~~ – po próbie usunięty (18.11): publiczne serwery przeciążone, a dane OSM mamy już z importu Geoapify. **Decyzja: Geoapify (istniejący katalog) + Overture (nowe atrakcje) + ręcznie, z ochroną przed duplikatami.**
3. Ręczne dodawanie – dla reszty.
4. Opcjonalnie później: pełny Foursquare OS Places (konto Hugging Face).
