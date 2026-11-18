# Atrakcje Podkarpacia – opis aplikacji

Aplikacja webowa do odkrywania atrakcji turystycznych i planowania jedno- oraz wielodniowych wyjazdów na Podkarpaciu. Praca inżynierska; termin MVP: **31 stycznia 2027**. Uczelnia ani promotor nie narzucają technologii ani dodatkowych wymagań.

## Cel

Użytkownik wybiera, gdzie będzie (miasto, okolice albo adres hotelu), daty i zainteresowania. Aplikacja proponuje atrakcje i układa plan dni: kolejność zwiedzania, czasy wizyt, przejazdy, przerwy na posiłek. Optymalizujemy czas przemieszczania. Plan można poprawiać ręcznie i otworzyć nawigację w Google Maps.

## Zasięg

- MVP: **Rzeszów i okolice** (obecnie 152 aktywne atrakcje, promień ok. 30–50 km).
- Kod i dane przygotowane na kolejne obszary (Bieszczady, Przemyśl, Krosno…): obszar to dane/konfiguracja, a nie stała w kodzie. Ile dodamy – zależy od czasu.

## Role

| Rola | Może |
|---|---|
| Gość | Przeglądać katalog i mapę, tworzyć plan. Plan trzymany tylko w przeglądarce (localStorage). |
| Użytkownik | Jak gość + zapis planów na koncie, edycja zapisanych planów, oceny i opinie, zgłoszenia błędów danych, wysyłka planu e-mailem. Po zalogowaniu może zapisać plan utworzony jako gość. |
| Administrator | Edycja atrakcji (dane, kategorie, godziny, czas wizyty, status), zatwierdzanie zdjęć, moderacja opinii, obsługa zgłoszeń. |

Logowanie: e-mail + hasło (ASP.NET Core Identity).

## Główne scenariusze

1. **Przeglądanie katalogu** – lista i mapa, filtry: miasto / promień od punktu, kategoria (zainteresowania), nazwa. Szczegóły atrakcji: opis, adres, godziny (z informacją o weryfikacji), zdjęcia z autorem i licencją, oceny.
2. **Wybór lokalizacji** – trzy tryby: miasto, miasto i okolice (promień), adres hotelu/noclegu (geokodowanie Geoapify; użytkownik wybiera propozycję).
3. **Tworzenie planu wyjazdu**
   - daty (jeden lub wiele dni), dla każdego dnia: nocleg (punkt startu i końca; może być inny w każdym dniu) i godziny aktywności,
   - zainteresowania (kategorie), atrakcje **obowiązkowe** i **opcjonalne**,
   - aplikacja może zaproponować dodatkowe atrakcje – trafiają do planu dopiero po akceptacji,
   - przerwa na posiłek jako blok czasu (np. 13:00–14:00), bez wskazywania restauracji.
4. **Wynik planu** – podział atrakcji na dni, kolejność, godziny przyjazdu i wyjazdu, czasy przejazdów, ostrzeżenia (godziny otwarcia nieznane lub atrakcja zamknięta w planowanym czasie, nie zmieściło się w dniu). Link do Google Maps dla każdego odcinka/dnia.
5. **Edycja planu** – zmiana kolejności w dniu, przenoszenie atrakcji między dniami, zmiana czasu wizyty. Po zmianie aplikacja przelicza godziny i przejazdy, nie zmienia sama kolejności.
6. **Zapis i e-mail** – zapis na koncie, lista moich planów, wysyłka prostego harmonogramu z linkami Google Maps.
7. **Opinie i zgłoszenia** – ocena (1–5) i opinia o atrakcji (widoczna po moderacji albo od razu z możliwością ukrycia – do ustalenia w epiku); zgłoszenie błędnych danych (godziny, lokalizacja, nazwa, atrakcja nie istnieje).
8. **Panel administratora** – edycja katalogu, zatwierdzanie zdjęć, moderacja, zgłoszenia.

## Transport i czasy przejazdów

- Tryb główny: **samochód + spacer** – krótkie odcinki pieszo (poniżej progu z konfiguracji), dłuższe samochodem. Uproszczenie MVP: nie śledzimy, gdzie zostało auto (znane ograniczenie, opisane w pracy).
- Czasy przejazdów liczy aplikacja sama, **bez wywoływania usług routingu w czasie planowania**:
  - między atrakcjami – z macierzy czasów po drogach (samochód i pieszo) pobranej raz, partiami, z **OpenRouteService** i zapisanej w bazie (wyniki CC-BY-SA 4.0, podpis ORS i OpenStreetMap);
  - odcinki z/do noclegu i pary spoza macierzy – **szacunek** z odległości w linii prostej (współczynnik objazdu, prędkość, czas na parkowanie), skalibrowany na danych z ORS i oznaczony w planie jako „czas przybliżony”. Adresów noclegu nie wysyłamy do zewnętrznych usług.
- **Nawigacja**: linki Google Maps (dzień i odcinki). Czasy w planie są orientacyjne – Google może pokazać inne (korki).
- **Komunikacja publiczna**: planer nie liczy rozkładów; plan daje link do Google Maps w trybie komunikacji.

## Dane

- Katalog w Supabase, schemat `catalog`: `places` (punkt PostGIS, status, godziny, szacowany czas wizyty, relacja element → obiekt nadrzędny), `categories` (9), `place_categories`, `place_sources` (surowe dane dostawcy oddzielone od ręcznych poprawek), `place_photos`.
- Źródło: Geoapify Places / OpenStreetMap, ręczna selekcja turystyczna (5.10.2026: 152 aktywne, 33 murale czekające na zdjęcie).
- Godziny otwarcia: tekst w formacie OSM, często brak. Nieznane ≠ całodobowo. W MVP planer tylko **ostrzega**, nie przestawia kolejności.
- Czas wizyty: szacunek z katalogu (lub domyślny dla kategorii), edytowalny w planie.
- Nowe dane w MVP: konta (Identity), plany (dni, pozycje), oceny i opinie, zgłoszenia, macierz czasów przejazdów między atrakcjami (`routing.travel_times`).
- Oceny w aplikacji i opinie Google to osobne źródła; opinii Google nie pobieramy.

## Poza MVP („później”)

- Komunikacja publiczna w algorytmie (GTFS).
- Parsowanie godzin otwarcia i uwzględnianie ich w kolejności.
- Algorytm „przeplanuj” proponujący nową wersję planu do akceptacji.
- Propozycje restauracji, ograniczenia poruszania się, śledzenie miejsca zaparkowania auta.
- Kolejne obszary Podkarpacia, wielojęzyczność, wdrożenie online.

## Wdrożenie i obrona

Na obronie aplikacja działa **lokalnie** na laptopie: backend z Visual Studio, frontend z Vite, baza Supabase (chmura), e-mail do lokalnej skrzynki testowej. Wymagane klucze w `.local/`.

## Ustalenia techniczne

Stos, konwencje i zasady pracy: `AGENTS.md`. Plan etapów: `docs/plan-prac.md`.

## Otwarte kwestie

- Opinie: moderacja przed publikacją czy po (ukrywanie)?
- Algorytm wielodniowy: dokładne kryterium podziału na dni (najbliżej noclegu danego dnia vs równomierne obciążenie) – do ustalenia w epiku planera.
- Czy potrzebny czysty tryb „tylko pieszo” (np. zwiedzanie samego Rzeszowa) obok trybu samochód + spacer.
