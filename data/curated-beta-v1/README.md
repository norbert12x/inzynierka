# Katalog po decyzjach R01–R19

Kopia robocza: `catalog.json`. Oryginalne dane Geoapify pozostają bez zmian.
Nie podłączono jeszcze tego pliku do endpointów aplikacji.

Z 600 rekordów:

- 485 samodzielnych wpisów ze statusem `active`;
- 33 murale ze statusem `awaiting_photo`, wyłączone z automatycznego planowania do czasu uzyskania zdjęć;
- 23 wpisy usunięte z katalogu roboczego, zachowane w `removed.json` jako historia;
- 7 elementów przypisanych do nadrzędnych miejsc w polu `components`;
- 52 tablice w `pending-components.json`, oczekujące na przypisanie do właściwego obiektu. Nie są samodzielnymi przystankami.

`catalog.json` zawiera 518 samodzielnych wpisów, w tym murale oczekujące na zdjęcie. Wszystkie oryginalne 600 identyfikatorów są rozliczone dokładnie raz pomiędzy tymi zbiorami.

Zastosowano ogólną zasadę użytkownika, że tablice stanowią elementy obiektów. Nie przypisywano ich wyłącznie na podstawie bliskości. Dotyczy to także R16: tablicy o 40 mieszkańcach Rzeszowa i tablicy o Stronnictwie Ludowym. Tablica o Sikorskim jest już elementem pomnika.

R01: nazwa „Teatr Narodowy w Rzeszowie”, ręczny typ `theatre`, kategoria `culture`, zgodnie z korektą użytkownika. Dawny tag tablicy pozostaje w danych źródłowych, ale nie wyznacza już typu tego rekordu.
R06: kategorie `nature` i `culture` (park oraz ośrodek kultury).
R17: kategorie `museums` i `religious-sites`.
Kategoria `culture` jest na razie lokalną korektą katalogu; słownik endpointu kategorii nie został zmieniony.
R18: godziny zwiedzania Zamku Lubomirskich **pon.–pt. 07:30–15:30** potwierdzone przez użytkownika 4 października 2026. Status `confirmed_by_user`, źródło potwierdzenia `user`. Nie jest to niezależna weryfikacja u operatora obiektu.

Ręczne korekty mają pierwszeństwo przed `sourceCategories` i `sourceDetails`. Te pola zachowują oryginalne dane dostawcy. Pozostałe wpisy wymagają późniejszego mapowania kategorii. `active` oznacza tylko obecność w katalogu, a nie potwierdzoną dostępność, godziny ani gotowość do automatycznego planowania.

Nie wybrano ani nie pobierano źródła zdjęć. Sam link do zdjęcia nie jest tutaj traktowany jako spełnienie warunku muralu.
Nie wprowadzano niezaakceptowanych zasad dla szczytów, archeologii, parków i pozostałych pomników/rzeźb z grup G1–G5.

Historia zmian i identyfikatory: `changes.json`. Skrypt odtwarzający: `../../scripts/curate_beta_catalog.py` (nie nadpisuje istniejącego folderu).
