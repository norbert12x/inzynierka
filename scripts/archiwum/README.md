# Archiwum skryptów danych

Skrypty, którymi w październiku 2026 zbudowano katalog (600 miejsc z Geoapify → selekcja turystyczna → import do Supabase) i pierwszą partię zdjęć. **Zrobiły swoje** – zostają jako dokumentacja procesu (przydatna w pracy inżynierskiej). Nie są częścią bieżącej pracy:

| Skrypt | Co robił | Czym zastąpiony |
|---|---|---|
| `extend_geoapify_beta.py` | druga partia 300 miejsc z zachowanych kandydatów Geoapify | – (jednorazowe) |
| `curate_beta_catalog.py`, `prepare_catalog_import.py` | ręczne decyzje R01–R19, przygotowanie importu (`database/002`) | – (jednorazowe) |
| `apply_catalog_database.py` | wykonanie `database/001–004` i importu | migracje EF Core (`Update-Database`) |
| `apply_tourism_review.py` | selekcja turystyczna 5.10.2026 (152 aktywne) | – (jednorazowe) |
| `apply_photo_review_01.py` | decyzje Z04/Z05/Z10 z przeglądu zdjęć | strona `/dev/zdjecia` |
| `fetch_place_photos.py`, `report_photo_coverage.py` | automat zdjęć Wikimedia i raporty w `data/photos` | `/dev/zdjecia` (propozycje z Commons, kolejka, naprawa plików) |
| `import_place_photo.py` | import zdjęcia własnego | do zrobienia: wgrywanie na `/dev/zdjecia` (plan, zadanie 1.9) |

Wymagały pakietów `psycopg` (i `Pillow`) w `.local/python-packages` – obecnie niezainstalowanych. Ścieżka `ROOT` wskazuje katalog repozytorium (`parents[2]`), więc da się je uruchomić z tego folderu, ale schemat bazy zmienia się już tylko migracjami EF.
