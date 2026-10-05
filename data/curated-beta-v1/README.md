# Katalog po decyzjach użytkownika i selekcji turystycznej beta

Kopia robocza: `catalog.json`. Oryginalne dane Geoapify pozostają bez zmian.
Katalog został zaimportowany do Supabase, skąd czytają go endpointy aplikacji.

Z 600 rekordów:

- 152 samodzielne wpisy ze statusem `active`;
- 33 murale ze statusem `awaiting_photo`, wyłączone z automatycznego planowania do czasu uzyskania zdjęć;
- 326 wpisów usuniętych z katalogu roboczego, zachowanych w `removed.json` jako historia;
- 37 elementów przypisanych do nadrzędnych miejsc w polu `components`;
- 52 tablice w `pending-components.json`, oczekujące na przypisanie do właściwego obiektu. Nie są samodzielnymi przystankami.

`catalog.json` zawiera 185 samodzielnych wpisów, w tym murale oczekujące na zdjęcie. Wszystkie oryginalne 600 identyfikatorów są rozliczone dokładnie raz pomiędzy tymi zbiorami.

Z04: Brama Główna jako element Muzeum - Zamku w Łańcucie; propozycja zdjęcia została przypisana zamkowi. Z05: Dawna synagoga w Czudcu i Z10: Izba Pamięci Sługi Bożego ks. Stanisława Sudoła usunięte z katalogu roboczego zgodnie z decyzją użytkownika; ich propozycje zdjęć odrzucone. Korekty zapisano również w Supabase i skrypcie importu, z zachowaniem historii.

Zastosowano ogólną zasadę użytkownika, że tablice stanowią elementy obiektów. Nie przypisywano ich wyłącznie na podstawie bliskości. Dotyczy to także R16: tablicy o 40 mieszkańcach Rzeszowa i tablicy o Stronnictwie Ludowym. Tablica o Sikorskim jest już elementem pomnika.

R01: nazwa „Teatr Narodowy w Rzeszowie”, ręczny typ `theatre`, kategoria `culture`, zgodnie z korektą użytkownika. Dawny tag tablicy pozostaje w danych źródłowych, ale nie wyznacza już typu tego rekordu.
R06: kategorie `nature` i `culture` (park oraz ośrodek kultury).
R17: kategorie `museums` i `religious-sites`.
Kategoria `culture` jest dostępna również w słowniku endpointu kategorii.
R18: godziny zwiedzania Zamku Lubomirskich **pon.–pt. 07:30–15:30** potwierdzone przez użytkownika 4 października 2026. Status `confirmed_by_user`, źródło potwierdzenia `user`. Nie jest to niezależna weryfikacja u operatora obiektu.

Ręczne korekty mają pierwszeństwo przed `sourceCategories` i `sourceDetails`. Te pola zachowują oryginalne dane dostawcy. Pozostałe wpisy wymagają późniejszego mapowania kategorii. `active` oznacza tylko obecność w katalogu, a nie potwierdzoną dostępność, godziny ani gotowość do automatycznego planowania.

Propozycje zdjęć z Wikimedia Commons zapisano osobno w `catalog.place_photos`; oczekują na przegląd. Sam link ani niezatwierdzona propozycja nie spełniają warunku aktywacji muralu.
5 października 2026 użytkownik zlecił selekcję miejsc wartych odwiedzenia i usunięcie pozostałych. Z 515 samodzielnych rekordów pozostawiono 185, 29 przypisano jako komponenty, a 301 wyłączono. Pełna ocena i powody każdego działania są w [przeglądzie](../tourism-review-2026-10-05/PRZEGLAD.md), a dowód wykonania w Supabase w `../tourism-review-2026-10-05/applied.json`. Jest to ocena redakcyjna beta; nie wszystkie miejsca sprawdzono w niezależnych źródłach i nie potwierdza ona aktualnej dostępności. Brak zdjęcia nie był powodem usunięcia. Kopia sprzed zmian znajduje się lokalnie w `.local/backups/tourism-selection-2026-10-05`.

Historia zmian i identyfikatory: `changes.json`. Skrypt odtwarzający: `../../scripts/curate_beta_catalog.py` (nie nadpisuje istniejącego folderu).
