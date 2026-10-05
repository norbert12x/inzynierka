# Automatyczne pobieranie propozycji zdjęć

`scripts/fetch_place_photos.py` odczytuje aktualny katalog z Supabase, wybiera kolejną partię miejsc i pobiera zdjęcia oraz metadane z Wikimedia Commons. Nie wymaga nowego klucza API. Nie uruchamia aplikacji .NET ani builda. Nie jest zadaniem uruchamianym przy każdym otwarciu strony.

Pierwszą partię wykonano 5 października 2026: sprawdzono 20 miejsc, pobrano 10 propozycji i zapisano 10 wyników bez kandydata. Zweryfikowano dekodowanie wszystkich 10 plików, wymagane metadane i brak duplikatów. Statusy 600 rekordów katalogu nie zmieniły się. Kontrola ponowienia błędów wybrała 0 miejsc. `latest-run.json` dotyczy ostatniego podetapu, natomiast `DO_PRZEJRZENIA.md` zawiera wszystkie propozycje oczekujące na przegląd.

## Uruchamianie

Z katalogu rozwiązania w PowerShell:

```powershell
$env:PYTHONIOENCODING='utf-8'
& 'C:/Users/Norbert/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/fetch_place_photos.py --limit 20
```

Przy konfiguracji na nowej bazie dodaj `--apply-schema`. Skrypt wykonuje wtedy `database/003_place_photos.sql`. Korzysta z istniejącego `.local/supabase.json`, certyfikatu i sterownika PostgreSQL w `.local/python-packages`.

`--dry-run` wyświetla kolejne wybrane miejsca bez pobierania i zapisów. `--retry-errors` ponawia wyłącznie wcześniejsze błędy, bez czekania doby. Limit partii wynosi od 1 do 100.

## Co robi automat

1. Wybiera samodzielne miejsca o statusie `active` lub `awaiting_photo`, priorytetowo z odwołaniem do zdjęć/Wikidata. Pomija miejsca z już zapisanymi zdjęciami, także odrzuconymi. Odrzucenie nie powoduje ponownego pobierania tej samej propozycji.
2. Sprawdza bezpośredni tag Commons/pliku albo pole zdjęcia `P18` przypisanego obiektu Wikidata. Nie zgaduje po podobnej nazwie. Nie pobiera zdjęć z Google ani nieznanych stron. Ręcznie zmienione nazwy wymagają osobnego sprawdzenia powiązania.
3. Sprawdza metadane pliku, autora i rozpoznaną licencję: CC BY/CC BY-SA 1.0–4.0, CC0 lub public domain. Nieznane licencje i wpisy z dodatkowymi ograniczeniami są pomijane. Warunki licencji konkretnego pliku należy sprawdzić przed publikacją.
4. Pobiera jedną propozycję dla miejsca, do lokalnego `.local/photo-cache`, z limitem 8 MB. Akceptuje JPEG, PNG i WebP. Przechowuje metadane i ścieżkę w `catalog.place_photos`, zawsze ze statusem `pending_review`.
5. Zapisuje wynik próby. Brak zdjęcia jest pomijany przez 30 dni, błąd przez dobę. Używa blokady PostgreSQL, żeby dwie partie nie przetwarzały tych samych miejsc równolegle. Unikalne klucze zabezpieczają przed duplikatami.
6. Tworzy `latest-run.json` (ostatnia partia) i `DO_PRZEJRZENIA.md` (wszystkie oczekujące propozycje z lokalnymi podglądami).

Automat nie zmienia nazw, kategorii ani statusu atrakcji. API udostępnia zatwierdzone zdjęcia; propozycje są dostępne do przeglądu tylko w Development po podaniu `includePending=true`. Murale pozostają `awaiting_photo`. Powtarzanie całego importu atrakcji nie usuwa propozycji zdjęć.

## Przechowywanie i przenoszenie

Pliki zdjęć w `.local` są wykluczone z Git, tak jak hasła. Metadane i raporty nie zawierają sekretów. Lokalna ścieżka w bazie nie jest publicznym adresem zdjęcia i nie zadziała na innym komputerze bez przeniesienia plików. Przed wdrożeniem zatwierdzone pliki przeniesiemy do Storage i zachowamy autora, licencję oraz link do źródła. Raport może zawierać zdjęcie budynku, całego pomnika lub historyczny widok, dlatego wymaga sprawdzenia.

Nie ustawiono harmonogramu: kolejne partie wykonują się po uruchomieniu skryptu. W razie przerwania zapisane zdjęcia są pomijane przy następnej partii. Podgląd raportu odtwarza się z bazy, nawet gdy poprzedni proces nie zdążył go zapisać.

## Wymaganie co najmniej jednego zdjęcia

Google nie jest używane: użytkownik nie chce zakładać projektu Google Cloud. Brak zdjęcia w publicznych źródłach nie może być zamieniony na przypadkową fotografię innego miejsca. Własne zdjęcie lub materiał udostępniony przez uprawnionego autora pozostaje koniecznym uzupełnieniem dla niektórych obiektów.

Rozszerzone wyszukiwanie uruchamia się z `--expanded --retry-missing --limit 100`. Oprócz bezpośrednich powiązań przegląda kategorie Commons oraz wyszukuje nazwę z miejscowością, ewentualnie pasujący tytuł w promieniu 250 m. Wszystkie wyniki nadal wymagają przeglądu; bliskość nie dowodzi, że zdjęcie pokazuje właściwy obiekt. `--retry-missing` ponawia braki starej strategii, ale pomija już sprawdzone braki oznaczone `Expanded:`, dzięki czemu można wykonać kolejne partie bez zapętlenia.

`scripts/report_photo_coverage.py` tworzy pełny raport `POKRYCIE.md`, `coverage.json` i listę `BRAKUJACE_ZDJECIA.csv`. Rozróżnia zatwierdzone zdjęcia, propozycje do przeglądu, braki i jeszcze niesprawdzone miejsca. Propozycja nie jest liczona jako zdjęcie gotowe do publikacji.

### Obsługa w API — do kompilacji i testu przez użytkownika

Wynik pełnego przebiegu z 5 października 2026: sprawdzono wszystkie 515 samodzielnych miejsc (482 aktywne atrakcje i 33 murale warunkowe). 118 miejsc ma propozycję do przeglądu, 1 ma zatwierdzone zdjęcie, a 396 nadal nie ma zdjęcia. Ponowny wybór z `--expanded --retry-missing --dry-run` zwrócił 0 rekordów. Sprawdzono odczyt wszystkich 121 zapisanych plików (w tym 2 wcześniej odrzuconych), ich ścieżki, metadane i brak duplikatów kluczy zdjęć; wyniki zapisano w `verification.json`. Kod aplikacji wymaga kompilacji i testu przez użytkownika.

- `GET /api/attractions` zwraca dodatkowo `hasPhoto` (istnieje zatwierdzone zdjęcie) oraz `pendingPhotoCount`.
- `GET /api/attractions?requirePhoto=true` zwraca wyłącznie aktywne miejsca z co najmniej jednym zatwierdzonym zdjęciem. Pozostałe filtry i paginacja pozostają dostępne. To tryb przeznaczony dla przyszłej listy publikowanych atrakcji; domyślny endpoint pozostaje narzędziem do pracy nad kompletnym katalogiem.
- `GET /api/attractions/{id}/photos` zwraca zatwierdzone zdjęcia, adres lokalnego endpointu pliku, autora, licencję, źródło i status. Frontend musi wyświetlać wymagany podpis, licencję i źródło przy zdjęciu.
- W Development `?includePending=true` pozwala przejrzeć propozycje, także dla murali `awaiting_photo`. W innych środowiskach nie udostępnia propozycji.
- Adres `imageUrl` pobiera plik przez `GET /api/attractions/{id}/photos/{photoId}/image`. Pliki z `.local/photo-cache` są serwowane tylko po sprawdzeniu powiązania, statusu i bezpiecznej ścieżki; nie ma publicznego dostępu do całego folderu `.local`.

`requirePhoto=true` uwzględnia obecnie zatwierdzone zdjęcie Z04; kolejne atrakcje pojawią się po zatwierdzeniu ich fotografii. Nie aktywujemy automatycznie murali ani nie akceptujemy nowych zdjęć w imieniu użytkownika. Jeśli plik zostanie usunięty z dysku, endpoint zdjęcia zwróci 404 — przed wdrożeniem potrzebne będzie przeniesienie do Storage i monitorowanie dostępności. Nie obiecujemy 100% pokrycia tylko na podstawie zewnętrznego wyszukiwania.

Z04 zostało zatwierdzone do wyświetlania na podstawie decyzji użytkownika o przypisaniu tej fotografii do zamku oraz prośby o widoczne zdjęcia. Przykład testowy po kompilacji: `/api/attractions/4402cf21-12ab-5f1c-9802-f07ab332feb7/photos`. Pozostałe nowe propozycje oczekują na sprawdzenie.

### Własne lub udostępnione zdjęcie

Wykonano rozszerzenie `database/004_user_provided_photos.sql`, dopuszczające źródło `user_provided`. Skrypt `scripts/import_place_photo.py` pozwala uzupełnić dowolną aktywną atrakcję lub mural lokalnym plikiem. Wymaga ID atrakcji, autora, wskazanej licencji lub zgody oraz opisu autorstwa/praw. Sprawdza format i rozmiar, kopiuje plik do cache, zapisuje metadane i pomija duplikat o identycznej zawartości. Nie interpretuje deklaracji jako zweryfikowanego prawa do zdjęcia — plik pozostaje propozycją do przeglądu.

Przykład dla rzeczywiście własnego zdjęcia; zastąp ID, ścieżkę i autora:

```powershell
& 'C:/Users/Norbert/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/import_place_photo.py --place-id 'ID-ATRAKCJI' --file 'D:\zdjecia\zamek.jpg' --author 'Autor zdjęcia' --license 'CC BY 4.0' --rights-note 'Jestem autorem i udostępniam to zdjęcie na wskazanej licencji.'
```

Jeśli właściciel udostępnił zdjęcie na podstawie konkretnej zgody, można wskazać `--license permission` i wpisać warunki zgody w `--rights-note`, opcjonalnie z `--source-url https://...`. Nie pobieramy automatycznie zdjęć ze stron hoteli lub atrakcji tylko dlatego, że są publicznie widoczne. Na nowej bazie pierwsze wywołanie wymaga `--apply-schema`, po wykonaniu schematu 003.

Źródła: [Wikimedia Imageinfo API](https://www.mediawiki.org/wiki/API:Imageinfo), [Wikidata Data access](https://www.wikidata.org/wiki/Help:Data_access), [warunki wykorzystania Commons](https://commons.wikimedia.org/wiki/Commons:Reusing_content_outside_Wikimedia).

## Stan po selekcji turystycznej

Po wyłączeniu mniej przydatnych rekordów katalog obejmuje 185 samodzielnych miejsc: 1 ma zdjęcie zatwierdzone, 73 mają propozycje do przeglądu, a 111 nie ma fotografii. Oddzielnie 8 propozycji pozostaje przy komponentach; ich zdjęć nie przeniesiono ani nie zatwierdzono automatycznie. Fotografie miejsc usuniętych z katalogu otrzymały status `rejected`, pliki i metadane zachowano. Aktualne liczby są w `coverage.json`; powyższy wynik pełnego wyszukiwania opisuje stan sprzed selekcji.
