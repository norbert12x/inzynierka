# Zdjęcia atrakcji – zasady

Bieżąca praca ze zdjęciami odbywa się na stronie **`/dev/zdjecia`** (tylko Development; opis endpointów w `docs/api.md`): propozycje z Wikimedia Commons (kategoria obiektu z Wikidata, nazwa, promień), wnętrza i eksponaty, kolejka przeglądu, zdjęcie główne, naprawa brakujących plików. Aktualny stan pokrycia pokazuje zakładka „Miejsca”. Pierwszy import (październik 2026) wykonały skrypty z `scripts/archiwum/`.

## Zasady

- **Tylko zdjęcia z jasną licencją**: CC BY / CC BY-SA 1.0–4.0 (także wersje krajowe), CC0, domena publiczna – z autorem i linkiem licencji; bez dodatkowych ograniczeń (wizerunek, znak towarowy); JPEG/PNG/WebP do 8 MB. Bez Google i bez kopiowania zdjęć ze stron firm.
- **Każde zdjęcie przechodzi przegląd** (`pending_review` → `approved` / `rejected`). Odrzucone nie wraca jako propozycja. Bliskość lub podobna nazwa nie dowodzi, że zdjęcie pokazuje właściwy obiekt.
- Brak zdjęcia nie jest zastępowany fotografią innego miejsca. Brakujące uzupełniamy zdjęciem własnym albo udostępnionym przez uprawnionego autora (źródło `user_provided`; wgrywanie przez `/dev/zdjecia` – plan, zadanie 1.9).
- **Podpis przy zdjęciu jest obowiązkowy**: autor, licencja z linkiem, źródło – API zwraca te pola razem z `imageUrl`.

## Przechowywanie

Pliki leżą w `.local/photo-cache` (ignorowane przez Git), baza zna tylko ścieżkę. Na innym komputerze: skopiuj folder albo użyj „Pobierz brakujące pliki” na `/dev/zdjecia` (zdjęcia Wikimedia pobiorą się ponownie; własnych trzeba skopiować). Przed wdrożeniem online pliki przeniesiemy do Supabase Storage z zachowaniem autora, licencji i źródła.

Źródła: [Wikimedia Imageinfo API](https://www.mediawiki.org/wiki/API:Imageinfo), [Wikidata Data access](https://www.wikidata.org/wiki/Help:Data_access), [warunki wykorzystania Commons](https://commons.wikimedia.org/wiki/Commons:Reusing_content_outside_Wikimedia).
