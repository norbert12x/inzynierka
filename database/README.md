# Baza danych (Supabase: PostgreSQL + PostGIS)

Backend łączy się z bazą przez EF Core/Npgsql (PostGIS do lokalizacji i wyszukiwania w promieniu). **Schemat zmieniamy migracjami EF Core** (`Inżynierka/Migrations/`, historia w `catalog.__ef_migrations_history`): zmiana encji w `Inżynierka/Data/` → `Add-Migration <Nazwa>` → `Update-Database` w Konsoli menedżera pakietów Visual Studio. Pliki `001`–`005` w tym folderze to historia schematu sprzed migracji – nie edytujemy ich i nie dodajemy nowych.

## Tabele (schemat `catalog`)

| Tabela | Przeznaczenie |
|---|---|
| `places` | Katalog: nazwa, punkt `geography(point,4326)` (długość, szerokość), godziny, czas wizyty, status, rodzaj (atrakcja / element obiektu), `manually_edited_fields`. |
| `categories` | Słownik kategorii z `default_visit_minutes` (10 kategorii, w tym „Aktywna rozrywka”). |
| `place_categories` | Wiele kategorii dla jednego miejsca. |
| `place_sources` | Źródło miejsca: `geoapify` (import), `overture`, `manual`; identyfikator zewnętrzny i surowe dane (`raw_data`) z oznaczeniem licencji. |
| `place_photos` | Zdjęcia: źródło, autor, licencja, ścieżka pliku w `.local/photo-cache`, status przeglądu, rodzaj (`place`/`exhibit`), podpis, kolejność galerii. |
| `photo_fetch_attempts` | Historia prób pierwszego automatu zdjęć (archiwalna). |

Statusy miejsc: `active` (widoczne w API), `awaiting_photo` (murale czekające na zdjęcie), `component` (element większego obiektu), `removed` (wyłączone, zachowane w historii). Publiczna lista bierze tylko `kind='attraction'` i `status='active'`.

Dostęp: schemat `catalog` nie jest wystawiony przez Data API Supabase, RLS włączone bez publicznych polityk – czyta i zapisuje tylko backend. Każda nowa tabela w migracji: `enable row level security`. Przed udostępnieniem aplikacji: osobna rola backendu z ograniczonymi prawami zamiast konta administratora.

## Konfiguracja lokalna

`.local/supabase.json` (ignorowany przez Git, wczytywany w Development), sekcja `Database`: `Host`, `Port`, `Name`, `Username`, `Password`, `RootCertificate` (`.local/supabase-ca.crt`, weryfikacja `VerifyFull`). Zmienne środowiskowe mają pierwszeństwo (`Database__Password` itd.). **Bez konfiguracji aplikacja nie wystartuje** (czytelny komunikat przy starcie).

Połączenie: w panelu Supabase **Connect → Session pooler**, port 5432 (bezpośredni host wymaga IPv6). Użytkownik ma postać `postgres.<ref>`.

Darmowy projekt usypia się po ok. tygodniu bez ruchu – przywróć go w panelu (**Restore project**). Test połączenia: w Swaggerze `GET /api/database/status` → `{"connected":true,...}`; przy problemie z bazą każdy endpoint zwraca 503 „Baza danych jest chwilowo niedostępna”.

## Historia (październik–listopad 2026)

1. `001_catalog_schema.sql` – schemat, tabele, indeksy, kategorie, ograniczenia dostępu.
2. `002_import_beta_catalog.sql` – import 600 miejsc z Geoapify (deterministyczne UUID, dane źródłowe oddzielone od ręcznych korekt). Weryfikacja: `database-verification.json`.
3. `003_place_photos.sql`, `004_user_provided_photos.sql` – zdjęcia i źródło `user_provided`.
4. `005_category_visit_minutes.sql` – domyślne czasy wizyt; wykonany w migracji startowej `InitialCatalog`.
5. Selekcja turystyczna 5.10.2026: 152 aktywne, 33 murale, 89 elementów, 326 wyłączonych (`data/tourism-review-2026-10-05/`).

Skrypty, którymi to wykonano: `scripts/archiwum/`. Kolejne zmiany: migracje `PhotoKindAndCaption`, `PhotoSortOrder`, `ActiveEntertainmentCategory`.

Dokumentacja: [połączenia Supabase](https://supabase.com/docs/guides/database/connecting-to-postgres), [PostGIS](https://supabase.com/docs/guides/database/extensions/postgis), [Npgsql / NetTopologySuite](https://www.npgsql.org/efcore/mapping/nts.html).
