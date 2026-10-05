# Supabase — katalog atrakcji, etap 1

Schemat i import wykonano 5 października 2026. Weryfikacja potwierdziła 600 rekordów, 9 kategorii i 600 wpisów źródłowych. Ponowny import dodał 0 rekordów i zachował katalog bez zmian. Wyniki zapisano w `database-verification.json`. Połączenie aplikacji .NET pozostaje do przetestowania w Visual Studio.

Backend używa PostgreSQL przez EF Core/Npgsql. PostGIS obsługuje lokalizację i przyszłe wyszukiwanie przestrzenne. Na tym etapie wersjonujemy schemat plikami SQL; nie używamy `Database.Migrate()` ani `EnsureCreated()` i nie generowaliśmy migracji EF przez build.

## Struktura

Tabele w schemacie `catalog`:

| Tabela | Przeznaczenie |
|---|---|
| `places` | Edytowalny katalog, punkt geograficzny, godziny, status, elementy i relacja do obiektu nadrzędnego. |
| `categories` | Dziewięć kategorii aplikacji. |
| `place_categories` | Wiele kategorii dla jednego miejsca. |
| `place_sources` | Dostawca, identyfikator zewnętrzny, czas pobrania i JSON źródłowy. |

`geography(point,4326)` ma kolejność długość, szerokość geograficzna. Indeks GiST przygotowuje wyszukiwanie w promieniu; taka odległość nie oznacza czasu przejazdu.

Import rozlicza wszystkie 600 rekordów. Statusy:

- `active`: 152 samodzielne wpisy;
- `awaiting_photo`: 33 murale, warunkowo zachowane;
- `component`: 89 elementów (37 powiązanych, 52 do przypisania);
- `removed`: 326 wpisów zachowanych tylko w historii, nie do prezentowania jako atrakcje.

Lista atrakcji wybiera tylko `kind='attraction'` i `status='active'`. Konta, wycieczki i oceny pozostają osobnymi etapami. Zdjęcia są przechowywane w `place_photos`; próby pozyskania w `photo_fetch_attempts`. Selekcję turystyczną wykonano w Supabase poprzez `scripts/apply_tourism_review.py --apply`; wynik w `data/tourism-review-2026-10-05/applied.json`. Ponowny import nie przywraca wyłączonych miejsc.

## Konfiguracja lokalna

Plik `.local/supabase.json` w katalogu rozwiązania jest ignorowany przez Git. Wczytywany tylko w środowisku Development. Pola `Database`: `Host`, `Port`, `Name`, `Username`, `Password`, `RootCertificate`. Certyfikat `.local/supabase-ca.crt` jest używany z weryfikacją nazwy hosta (`VerifyFull`). Hasła nie wpisujemy w `Program.cs` ani `appsettings.json`.

Używamy parametrów **Connect → Method → Session pooler**, port 5432. Bezpośredni host `db.<ref>.supabase.co` wymaga tutaj niedostępnej sieci IPv6. Username poolera ma postać `postgres.<ref>`. Nawiasy `[YOUR-PASSWORD]` w przykładzie są placeholderem — hasło wpisujemy bez nawiasów. Klucz publishable i Supabase CLI nie są potrzebne do tego połączenia.

Zmienne środowiskowe mają pierwszeństwo, np. `Database__Password` i `Database__Host`. Zmiana konfiguracji wymaga ponownego uruchomienia aplikacji.

## Schemat i import

1. `001_catalog_schema.sql`: schemat, cztery tabele, indeksy, dziewięć kategorii i ograniczenia dostępu. PostGIS ma być w `extensions`; skrypt zatrzyma się, jeśli rozszerzenie istnieje w innym schemacie.
2. `002_import_beta_catalog.sql`: import katalogu. Identyfikatory UUID są deterministyczne względem identyfikatora Geoapify. Miejsca i kategorie istniejących miejsc pozostają bez zmian przy ponowieniu. Zaktualizować można jedynie snapshot źródła z co najmniej taką samą datą pobrania. Dane źródłowe i ręczne korekty pozostają oddzielone.
3. `003_place_photos.sql`: zdjęcia i zapis prób wyszukiwania. Wymagane przez obecny endpoint listy i zdjęć.
4. `004_user_provided_photos.sql`: dopuszczenie zdjęć własnych lub udostępnionych przez autorów.

Skrypt `apply_catalog_database.py --apply` wykonuje również schematy 003 i 004. Nie zatwierdza ani nie usuwa istniejących zdjęć.

Można uruchomić pliki kolejno w SQL Editor Supabase. Alternatywnie skrypt `scripts/apply_catalog_database.py` używa lokalnego sterownika PostgreSQL z `.local/python-packages`:

```powershell
& 'C:/Users/Norbert/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/apply_catalog_database.py --check-only
& 'C:/Users/Norbert/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/apply_catalog_database.py --apply
```

Polecenia z katalogu rozwiązania. Nie uruchamiają .NET. Tryb `--apply` wykonuje schemat, import i ponowny import do sprawdzenia braku nowych rekordów oraz zmian katalogu. Przy sukcesie zapisuje `database-verification.json`; brak tego raportu oznacza, że import nie został potwierdzony. `import-preview.json` jest wyłącznie raportem przygotowania.

Nie włączamy schematu `catalog` jako publicznego schematu Data API. RLS jest włączone bez publicznych polityk; dostęp ma backend przez połączenie PostgreSQL. Na lokalny etap używamy wskazanej roli administracyjnej bazy; przed udostępnieniem aplikacji przygotujemy oddzielną rolę backendu z ograniczonymi prawami.

## Kategorie

Słownik API został rozszerzony o `culture`, `public-art` i `other`. Reguły importu mapują kategorie Geoapify na słownik aplikacji. Ręczne `categoryCodes` mają pierwszeństwo (teatr, kaplica/muzeum i ośrodek kultury/park). Przy nieznanym dopasowaniu używane jest `other`. Pozostałe źródłowe oznaczenia zachowane w JSON, a kategorie aplikacji można później poprawiać.

## Test po Twojej stronie

Po wykonaniu schematu/importu zatrzymaj aplikację (Shift+F5), skompiluj (Ctrl+Shift+B) i uruchom (F5).
W Swaggerze: **Database → GET /api/database/status → Try it out → Execute**.

Po poprawnym imporcie oczekiwane HTTP 200:

```json
{"connected":true,"schema":"catalog","placesCount":600,"categoriesCount":9}
```

`placesCount` liczy również elementy i wpisy wycofane; nie oznacza 600 aktywnych atrakcji. HTTP 503 z informacją o odczycie bazy oznacza problem z konfiguracją, hasłem, połączeniem lub brak schematu. Endpoint diagnostyczny dostępny tylko w Development. Testów builda i aplikacji nie wykonywał agent, zgodnie z ustaleniem.

Dokumentacja: [połączenia Supabase](https://supabase.com/docs/guides/database/connecting-to-postgres), [TLS i certyfikat](https://supabase.com/docs/guides/database/psql), [PostGIS](https://supabase.com/docs/guides/database/extensions/postgis), [Npgsql / NetTopologySuite](https://www.npgsql.org/efcore/mapping/nts.html).
