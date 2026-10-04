# Katalog beta — Rzeszów i okolice

Pobrano 4 października 2026 z Geoapify Places API. Wybrano 300 rekordów z 689 nazwanych kandydatów o poprawnych współrzędnych. Wykonano 12 zapytań. Promień 30 km; maksymalna odległość wybranego punktu od centrum: 29,771 km w linii prostej.

## Zestaw roboczy

Plik `beta-places.json` jest zestawem do dalszej pracy; `raw-*.geojson` zawierają odpowiedzi źródłowe, a `candidates.json` dodatkowe miejsca do ewentualnej zamiany. Nie trzeba ponownie pobierać danych przy uruchamianiu aplikacji. Odczyt tego pliku przez backend nie jest jeszcze zaimplementowany.

| Grupa użyta przy doborze | Liczba |
|---|---:|
| Muzea i galerie | 40 |
| Zabytki i historia | 82 |
| Przyroda, parki i ogrody | 81 |
| Punkty widokowe | 9 |
| Rekreacja | 7 |
| Pozostałe atrakcje | 81 |

Każdy wpis jest liczony w jednej grupie doboru, ale źródłowe kategorie mogą się nakładać. 204 wpisy mają miejscowość Rzeszów, 96 inne miejscowości. Dane zawierają m.in. Rzeszowskie Piwnice, Muzeum Dobranocek, obiekty w Łańcucie i atrakcje rekreacyjne w okolicy.

## Kontrola jakości

- 300 różnych identyfikatorów Geoapify; nazwy i współrzędne obecne we wszystkich wpisach.
- Godziny otwarcia obecne w 24 wpisach, brak w 276. Ich aktualności nie zweryfikowano.
- Strona WWW obecna w 50 wpisach.
- Wszystkie wpisy mają status `unverified`, a szacowany czas wizyty pozostaje pusty.
- Znaleziono cztery pary o tej samej nazwie w odległości poniżej 150 m: trzy pary między trzema wpisami „Prasłowiańska Figura” oraz para „W Hołdzie Zesłańcom”. To kandydaci do ręcznego sprawdzenia, nie potwierdzone duplikaty. Identyfikatory w `quality-report.json`.
- Są niewielkie rzeźby, pomniki, skwery i elementy instalacji. Należy zdecydować, które stanowią samodzielne atrakcje, a które warto łączyć w jeden przystanek.
- Nie sprawdzano dostępności wejścia, aktualnych cen ani godzin na stronach obiektów.
- Sprawdzono, że zapisane pliki nie zawierają klucza API.

## Następny mały krok

Udostępnić lokalny zestaw przez endpoint API, bez połączenia z dostawcą. Następnie przejrzeć wybrane miejsca i ustalić edytowalne czasy wizyt. Planowanie przejazdów wymaga osobnego źródła czasów podróży; odległość w linii prostej nie jest czasem przejazdu.

Źródło: [Powered by Geoapify](https://www.geoapify.com/) oraz [© OpenStreetMap contributors / licencja](https://www.openstreetmap.org/copyright). Zachować oznaczenia przy wyświetlaniu danych.
