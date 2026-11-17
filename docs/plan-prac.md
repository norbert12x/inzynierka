# Plan prac – MVP do 31 stycznia 2027

Epiki w kolejności realizacji. Każde podzadanie to mały etap: kod → użytkownik kompiluje i testuje w Swaggerze/przeglądarce → poprawki. Oznaczenia: ✅ zrobione, 🔶 częściowo, ⬜ do zrobienia. Zasady techniczne: `AGENTS.md`, wymagania: `docs/opis-aplikacji.md`.

## Harmonogram (orientacyjny)

| Tydzień | Daty | Epiki |
|---|---|---|
| 1 | 16–22.11 | E0 Fundamenty, E1 Katalog – domknięcie (1.4 i 1.5 w tle, do końca grudnia) |
| 2 | 23–29.11 | E2 Konta i role |
| 3 | 30.11–6.12 | E3 Czasy przejazdów, E6.1–6.2 szkielet frontendu i katalog |
| 4–5 | 7–20.12 | E4 Planer |
| 6 | 21–23.12 | E5 Zapis i edycja planów |
| — | 24.12–1.01 | Bufor (święta) |
| 7–8 | 2–17.01 | E6.3–6.6 frontend planera, konta i panelu admina |
| 9 | 18–24.01 | E7 Opinie i zgłoszenia, E8 E-mail |
| 10 | 25–31.01 | E9 Przygotowanie do obrony |

Jeśli zabraknie czasu, w tej kolejności tniemy: E7 (zgłoszenia, potem opinie) → E8 → panel admina (edycja przez SQL/skrypty).

---

## E0 Fundamenty

- ✅ **0.1 Dokumenty dla AI i opis** (README jako strona projektu, szczegóły API w `docs/api.md`) – `AGENTS.md`, `CLAUDE.md`, `docs/opis-aplikacji.md`, ten plan. README skrócone do uruchomienia i odsyłaczy.
  Gotowe, gdy: użytkownik zaakceptował dokumenty.
- ✅ **0.2 Projekt testów** – `Inżynierka.Tests` (xUnit) w rozwiązaniu, katalog `Inżynierka/Planning/` na logikę planera, jeden przykładowy test.
  Gotowe, gdy: `dotnet test` przechodzi u użytkownika.
- ✅ **0.3 Konfiguracja obszaru** – sekcja `Catalog:Regions` w `appsettings.json` (kod, nazwa, środek, promień, domyślny) z walidacją przy starcie; endpoint `GET /api/regions`. Rzeszów i okolice (30 km) jako pierwszy wpis.
  Gotowe, gdy: endpoint zwraca obszar, testy walidacji przechodzą.

## E1 Katalog – domknięcie (🔶 lista, filtry, lokalizacje i zdjęcia już są)

- ✅ Kategorie, lista atrakcji z filtrami (miasto, promień, nazwa, kategoria), wyszukiwanie lokalizacji, zdjęcia.
- ✅ **1.1 Szczegóły atrakcji** – `GET /api/attractions/{id}`: opis, adres, godziny + weryfikacja, strona WWW, czas wizyty, kategorie, elementy obiektu, zatwierdzone zdjęcia z autorem i licencją, oznaczenie źródła. 404 dla nieaktywnych.
  Gotowe, gdy: test w Swaggerze dla aktywnej, usuniętej i nieistniejącej atrakcji.
- ⬜ **1.2 Domyślne czasy wizyt** – czas wizyty dla kategorii (konfiguracja lub tabela), używany gdy atrakcja nie ma własnego; w odpowiedzi informacja, czy to szacunek.
  Gotowe, gdy: każda aktywna atrakcja ma w API czas wizyty z oznaczeniem źródła.
- 🔶 **1.3 Usunięcie podglądu Overpass** – usunięte: endpoint `preview/rzeszow`, `OverpassAttractionService`, `AttractionPreview`, klient HTTP w `Program.cs`; katalog czyta wyłącznie bazę. (Czeka na kompilację i test u użytkownika.)
  Gotowe, gdy: API nie wysyła zapytań do Overpass, w kodzie nie ma stałych współrzędnych Rzeszowa.
- ⬜ **1.4 Przegląd zdjęć** (praca użytkownika, nie kod) – 73 propozycje w `data/photos/DO_PRZEJRZENIA.md` do zatwierdzenia/odrzucenia; przy okazji sprawdzić podejrzane wpisy bez zdjęcia (np. „Deloitte”, „Burger”, „Centrum Snu”) – mural czy do usunięcia. Stan 17.11: 1 zatwierdzone, 73 propozycje, 111 bez zdjęcia. **Brakuje części plików**: w `.local/photo-cache` jest 55 plików, z tego tylko 38 pasuje do raportu; zdjęcia Białego Ogrodu i zatwierdzone zdjęcie Zamku w Łańcucie zwracają 404. Najpierw ponowne pobranie brakujących plików z `original_url` zapisanego w bazie (nowy tryb skryptu, bez zmian w rekordach).
  Gotowe, gdy: najważniejsze atrakcje (Rzeszów + najbliższe okolice) mają zatwierdzone zdjęcie; raport `POKRYCIE.md` odświeżony.
- ⬜ **1.5 Krótkie opisy** – opis 1–3 zdania dla najważniejszych atrakcji (teraz 0 z 185), napisany samodzielnie lub z licencjonowanego źródła z podaniem źródła; zapis plikiem SQL lub skryptem z ręczną korektą (`manually_edited_fields`).
  Gotowe, gdy: atrakcje używane w scenariuszu obrony mają opis.
- ⬜ **1.6 Porządki w katalogu** – jedno źródło kategorii (endpoint czyta z bazy zamiast `AttractionCategoryCatalog` albo test zgodności), w `AttractionPhotosController` obsługa przekroczenia czasu (503 zamiast 500) i brak `?includePending=true` w linku poza Development.
  Gotowe, gdy: zmiany przejrzane, testy przechodzą.
- ⬜ **1.7 Więcej źródeł zdjęć** (po naprawie plików z 1.4) – tylko zdjęcia z jasną licencją, zawsze jako `pending_review` z autorem, licencją i linkiem źródła; bez scrapowania stron i Google Grafika.
  1. Wikimedia Commons – wyszukiwanie plików w promieniu (np. 100–200 m) od współrzędnych atrakcji; bez klucza.
  2. Openverse API (zdjęcia CC z wielu serwisów) – wyszukiwanie po nazwie i miejscowości; bez klucza przy małej liczbie zapytań.
  3. Opcjonalnie, za zgodą użytkownika (nowe usługi): Flickr API z filtrem licencji CC, Mapillary (pomniki, murale).
  4. Uzupełnienie ręczne: zdjęcia własne lub za zgodą właściciela (`user_provided`, `scripts/import_place_photo.py`).
  Gotowe, gdy: nowe propozycje są w `DO_PRZEJRZENIA.md`, raport pokrycia pokazuje wzrost, żadna propozycja nie jest publikowana bez przeglądu.

## E2 Konta i role

- ⬜ **2.1 Tabele Identity** – plik `database/005_identity.sql` (schemat `identity`), mapowanie `IdentityDbContext`.
  Gotowe, gdy: użytkownik wykonał SQL, aplikacja startuje i widzi tabele.
- ⬜ **2.2 Rejestracja, logowanie, wylogowanie, „ja”** – `POST /api/auth/register|login|logout`, `GET /api/auth/me`; ciasteczko HttpOnly; walidacja hasła; polskie komunikaty.
  Gotowe, gdy: scenariusz w Swaggerze: rejestracja → logowanie → `me` → wylogowanie → `me` = 401.
- ⬜ **2.3 Rola Admin** – rola `Admin`, nadawana skryptem/SQL; polityka `[Authorize(Roles = "Admin")]`.
  Gotowe, gdy: endpoint testowy zwraca 403 dla zwykłego użytkownika i 200 dla admina.

## E3 Czasy przejazdów (OpenRouteService)

- ⬜ **3.1 Klient ORS** – `OpenRouteServiceClient` (macierz czasów i odległości, profile `driving-car` i `foot-walking`), klucz w `.local/ors-key.txt`, timeout, obsługa limitów (429 → 503).
  Gotowe, gdy: endpoint diagnostyczny (Development) zwraca macierz dla 3 atrakcji.
- ⬜ **3.2 Pamięć podręczna** – tabela `routing.travel_times` (para punktów, profil, czas, odległość, data), plik SQL; najpierw baza, potem ORS tylko dla brakujących par.
  Gotowe, gdy: drugie wywołanie dla tych samych punktów nie wysyła zapytania do ORS.
- ⬜ **3.3 Tryb samochód + spacer i przybliżenie** – odcinek pieszo poniżej progu (konfiguracja), inaczej samochód; gdy ORS niedostępny – szacunek z odległości PostGIS oznaczony jako przybliżony.
  Gotowe, gdy: testy xUnit wyboru trybu i przybliżenia; ostrzeżenie w odpowiedzi przy przybliżeniu.

## E4 Planer

- ⬜ **4.1 Model planu** – klasy w `Planning/`: wyjazd, dni (data, nocleg, godziny aktywności, przerwa na posiłek), pozycje (atrakcja, obowiązkowa/opcjonalna, czas wizyty), ostrzeżenia.
  Gotowe, gdy: model przejrzany przez użytkownika.
- ⬜ **4.2 Plan jednego dnia** – kolejność od noclegu do noclegu (najbliższy sąsiad + 2-opt na macierzy czasów), godziny przyjazdu/wyjazdu, wstawienie przerwy na posiłek, atrakcje opcjonalne odrzucane, gdy nie mieszczą się w dniu.
  Gotowe, gdy: testy xUnit: kolejność, mieszczenie się w oknie dnia, posiłek, obowiązkowe zawsze w planie albo ostrzeżenie.
- ⬜ **4.3 Wiele dni** – przydział atrakcji do dni (uwzględnia nocleg każdego dnia i obciążenie), potem 4.2 dla każdego dnia.
  Gotowe, gdy: testy xUnit dla 2–3 dni z różnymi noclegami.
- ⬜ **4.4 Ostrzeżenia o godzinach** – brak godzin → „nieznane godziny”; jeśli zapis da się prosto odczytać i wizyta wypada poza nim → „może być zamknięte”. Bez zmiany kolejności.
  Gotowe, gdy: testy xUnit dla trzech przypadków (brak, otwarte, zamknięte).
- ⬜ **4.5 Propozycje dodatkowych atrakcji** – na podstawie zainteresowań i wolnego czasu; zwracane osobno, do akceptacji.
  Gotowe, gdy: propozycje nie trafiają do planu bez akceptacji.
- ⬜ **4.6 Endpoint** – `POST /api/plans/generate` (bez zapisu, działa dla gościa): wejście → plan + ostrzeżenia + propozycje + linki Google Maps (dzień i odcinki, także tryb komunikacji).
  Gotowe, gdy: test w Swaggerze dla 1 i 3 dni; błędy 400 dla złych dat/pustej listy.

## E5 Zapis i edycja planów

- ⬜ **5.1 Tabele planów** – `trips.plans`, `trips.plan_days`, `trips.plan_items` (plik SQL), właściciel = użytkownik.
  Gotowe, gdy: SQL wykonany, mapowanie EF.
- ⬜ **5.2 CRUD** – `POST/GET/PUT/DELETE /api/plans`, lista „moje plany”; tylko właściciel (inaczej 404).
  Gotowe, gdy: test w Swaggerze na dwóch kontach.
- ⬜ **5.3 Edycja i przeliczenie** – zmiana kolejności w dniu, przeniesienie między dniami, zmiana czasu wizyty → przeliczenie godzin i przejazdów bez zmiany ręcznej kolejności (`POST /api/plans/recalculate` dla gościa i zapis dla konta).
  Gotowe, gdy: testy xUnit przeliczenia + test w Swaggerze.

## E6 Frontend (React + Vite)

- ⬜ **6.1 Szkielet** – `frontend/` (Vite, TypeScript, React Router, Leaflet), proxy `/api`, układ strony, polskie teksty, typy kontraktów (ręcznie lub generowane z OpenAPI – do decyzji). Usunięcie nieużywanego szablonu Razor Pages (`Pages/`, `wwwroot/lib`).
  Gotowe, gdy: `npm run dev` pokazuje stronę i pobiera kategorie z API.
- ⬜ **6.2 Katalog i mapa** – lista + mapa, filtry, wybór lokalizacji (3 tryby), szczegóły atrakcji ze zdjęciami i oznaczeniami źródeł.
- ⬜ **6.3 Kreator planu** – daty, noclegi, godziny, zainteresowania, obowiązkowe/opcjonalne, akceptacja propozycji; plan gościa w localStorage.
- ⬜ **6.4 Widok i edycja planu** – dni, oś czasu, mapa trasy, ostrzeżenia, przeciąganie, przenoszenie między dniami, zmiana czasu wizyty, linki Google Maps.
- ⬜ **6.5 Konto** – rejestracja, logowanie, moje plany, zapis planu gościa po zalogowaniu.
- ⬜ **6.6 Panel administratora** – edycja atrakcji, zatwierdzanie zdjęć (moderacja i zgłoszenia po E7).
  Gotowe (każde), gdy: scenariusz przeklikany przez użytkownika, `npm run build` bez błędów.

## E7 Opinie i zgłoszenia

- ⬜ **7.1 Oceny i opinie** – tabela, `POST/GET /api/attractions/{id}/reviews`, średnia ocena w liście i szczegółach; jedna opinia na użytkownika i atrakcję.
- ⬜ **7.2 Moderacja** – admin ukrywa/zatwierdza opinie (tryb do ustalenia, patrz otwarte kwestie).
- ⬜ **7.3 Zgłoszenia błędów danych** – użytkownik zgłasza, admin zamyka z komentarzem i poprawia atrakcję.
  Gotowe (każde), gdy: test w Swaggerze dla ról gość/użytkownik/admin + ekran we frontendzie.

## E8 E-mail

- ⬜ **8.1 Wysyłka SMTP** – MailKit, konfiguracja `Smtp`, lokalnie Mailpit; szablony po polsku.
- ⬜ **8.2 Plan e-mailem** – `POST /api/plans/{id}/email`: prosty harmonogram dni z linkami Google Maps; limit wysyłek.
  Gotowe, gdy: wiadomość widoczna w Mailpit.

## E9 Przygotowanie do obrony

- ⬜ **9.1 Dane demo** – konto admina i użytkownika, 2–3 zapisane plany, zatwierdzone zdjęcia dla najważniejszych atrakcji.
- ⬜ **9.2 Instrukcja uruchomienia** – README: wymagania, `.local/`, kolejność SQL, start backendu, frontendu i Mailpit. Skrypty Python uruchamiane zwykłym Pythonem (teraz ścieżka do `codex-runtimes` w README). Osobna rola bazy dla backendu z ograniczonymi prawami zamiast konta administratora.
- ⬜ **9.3 Materiały do pracy** – diagram architektury, ERD, przypadki użycia, opis algorytmu i ograniczeń (powrót do auta, przybliżenia, godziny).
- ⬜ **9.4 Próba generalna** – pełny scenariusz na czystym uruchomieniu.
  Gotowe, gdy: scenariusz obrony przechodzi bez poprawek.

## Później (poza MVP)

Komunikacja publiczna (GTFS), parsowanie godzin i ich wpływ na kolejność, „przeplanuj” z akceptacją, restauracje, kolejne obszary Podkarpacia, wdrożenie online, wielojęzyczność.
