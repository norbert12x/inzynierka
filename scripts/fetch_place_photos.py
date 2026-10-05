"""Pobiera partiami propozycje zdjęć Commons; bez builda i zmiany statusów atrakcji.

Uruchamiaj z dowolnego katalogu; wymagany istniejący .local/supabase.json.
Pierwszy raz: python scripts/fetch_place_photos.py --apply-schema --limit 20
Kolejna partia: python scripts/fetch_place_photos.py --limit 20
"""
import argparse
import hashlib
import html
import json
import pathlib
import re
import shutil
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import unicodedata
from collections import Counter
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.local/python-packages'))
import psycopg
from psycopg.types.json import Jsonb

USER_AGENT = 'InzynierkaPhotoBot/0.1 (https://github.com/norbert12x/inzynierka; catalog photo research)'
ALLOWED_HOSTS = {'www.wikidata.org', 'commons.wikimedia.org', 'upload.wikimedia.org', 'thumb.wikimedia.org'}
CACHE = ROOT / '.local/photo-cache'
REPORTS = ROOT / 'data/photos'


def request(url, binary=False):
    if urllib.parse.urlsplit(url).hostname not in ALLOWED_HOSTS or not url.startswith('https://'):
        raise ValueError('Unexpected Wikimedia host')
    for attempt in range(3):
        try:
            time.sleep(0.4)
            req = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})
            with urllib.request.urlopen(req, timeout=20) as response:
                if urllib.parse.urlsplit(response.url).hostname not in ALLOWED_HOSTS:
                    raise ValueError('Unexpected redirect')
                mime = response.headers.get_content_type()
                payload = response.read(8 * 1024 * 1024 + 1)
                if len(payload) > 8 * 1024 * 1024:
                    raise ValueError('Response too large')
                if binary:
                    if mime not in ('image/jpeg', 'image/png', 'image/webp'):
                        raise ValueError('Unsupported image format')
                    return payload, mime
                return json.loads(payload)
        except urllib.error.HTTPError as exc:
            if exc.code not in (429, 502, 503, 504) or attempt == 2:
                raise
            time.sleep(2 ** attempt)


def plain(value):
    return html.unescape(re.sub(r'<[^>]*>', '', value or '')).strip()


def commons_file(value):
    if not isinstance(value, str):
        return None
    value = urllib.parse.unquote(value.strip())
    if value.startswith(('File:', 'Image:')):
        return 'File:' + value.split(':', 1)[1]
    if value.startswith('https://commons.wikimedia.org/'):
        path = urllib.parse.urlsplit(value).path
        for marker in ('/wiki/File:', '/wiki/Special:FilePath/'):
            if marker in path:
                return 'File:' + path.split(marker, 1)[1]
    return None


def commons_query(**params):
    return request('https://commons.wikimedia.org/w/api.php?' + urllib.parse.urlencode(dict(action='query',format='json',**params)))


def category_files(category):
    title = category if category.startswith('Category:') else 'Category:' + category
    data = commons_query(list='categorymembers',cmtitle=title,cmtype='file',cmnamespace=6,cmlimit=3)
    return [item['title'] for item in data.get('query',{}).get('categorymembers',[])]


def candidates(tags, expanded=False):
    files = []
    for tag in ('wikimedia_commons', 'image'):
        name = commons_file(tags.get(tag))
        if name:
            files.append((name, 'osm_' + tag))
    category = tags.get('wikimedia_commons', '')
    if expanded and isinstance(category,str) and category.startswith('Category:'):
        files.extend((title,'osm_commons_category') for title in category_files(category))
    wikidata = tags.get('wikidata', '')
    if not files and isinstance(wikidata, str) and re.fullmatch(r'Q[1-9][0-9]*', wikidata):
        data = request('https://www.wikidata.org/wiki/Special:EntityData/' + wikidata + '.json')
        entity = data.get('entities', {}).get(wikidata, {})
        claims = entity.get('claims', {}).get('P18', [])
        claims = sorted(claims, key=lambda claim: claim.get('rank') != 'preferred')
        for claim in claims:
            if claim.get('rank') == 'deprecated':
                continue
            value = claim.get('mainsnak', {}).get('datavalue', {}).get('value')
            if isinstance(value, str):
                files.append(('File:' + value, 'wikidata_P18:' + wikidata))
        if expanded and not files:
            for claim in entity.get('claims',{}).get('P373',[])[:1]:
                category = claim.get('mainsnak',{}).get('datavalue',{}).get('value')
                if isinstance(category,str):
                    files.extend((title,'wikidata_commons_category:'+wikidata) for title in category_files(category))
    return list(dict.fromkeys(files))[:3]


def tokens(value):
    value = unicodedata.normalize('NFKD',value.casefold().replace('ł','l'))
    return set(re.findall(r'[a-z0-9]{3,}', ''.join(c for c in value if not unicodedata.combining(c))))


def search_candidates(name, city, latitude, longitude):
    # Szukanie to tylko propozycja do przeglądu, nigdy automatyczna akceptacja.
    useful = tokens(name) - {'pomnik','figura','park','zamek','muzeum','rzeszow','glowna','imienia','swietego'}
    if not useful:
        return []
    files = []
    if city:
        city_words = tokens(city)
        data = commons_query(list='search',srsearch=f'{name} {city}',srnamespace=6,srlimit=5)
        for item in data.get('query',{}).get('search',[]):
            words = tokens(item['title'])
            if useful & words and city_words & words:
                files.append((item['title'],'commons_name_city_search:needs_review'))
    if not files:
        nearby = commons_query(list='geosearch',gscoord=f'{latitude}|{longitude}',gsradius=250,
            gsnamespace=6,gslimit=10)
        for item in nearby.get('query',{}).get('geosearch',[]):
            if useful & tokens(item['title']):
                files.append((item['title'],'commons_nearby_name:needs_review'))
    return files[:3]


def photo_metadata(title):
    params = dict(action='query', titles=title, prop='imageinfo',
        iiprop='url|extmetadata|mime', iiurlwidth=1280, format='json')
    data = request('https://commons.wikimedia.org/w/api.php?' + urllib.parse.urlencode(params))
    pages = data.get('query', {}).get('pages', {})
    for page in pages.values():
        if not page.get('imageinfo'):
            continue
        info = page['imageinfo'][0]
        meta = info.get('extmetadata', {})
        get = lambda key: plain(meta.get(key, {}).get('value', ''))
        license_name, license_url = get('LicenseShortName'), get('LicenseUrl')
        # Propozycje tylko z rozpoznaną licencją; inne warunki wymagają osobnego przeglądu.
        if not re.fullmatch(r'CC BY(?:-SA)? [1-4]\.0|CC0|Public domain', license_name):
            return None
        if not license_url:
            return None
        if get('Restrictions') or get('Copyrighted') == 'True' and license_name == 'Public domain':
            return None
        if info.get('mime') not in ('image/jpeg', 'image/png', 'image/webp'):
            return None
        author = get('Artist')
        if not author:
            return None
        return dict(sourceFile=page['title'], sourcePageUrl=info['descriptionurl'],
            originalUrl=info['url'], thumbnailUrl=info.get('thumburl', info['url']),
            author=author, credit=get('Credit'), license=license_name, licenseUrl=license_url,
            metadata=meta)
    return None


def find_photo(place_id, tags, expanded=False, name='', city=None, latitude=None, longitude=None):
    linked = candidates(tags,expanded)
    options = linked or (search_candidates(name,city,latitude,longitude) if expanded else [])
    for title, method in options:
        photo = photo_metadata(title)
        if photo is None:
            continue
        payload, mime = request(photo['thumbnailUrl'], binary=True)
        extension = {'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp'}[mime]
        digest = hashlib.sha256(photo['sourceFile'].encode()).hexdigest()[:20]
        relative = pathlib.Path('.local/photo-cache') / (str(place_id) + '-' + digest + extension)
        CACHE.mkdir(parents=True, exist_ok=True)
        destination = ROOT / relative
        if not destination.exists():
            temporary = destination.with_suffix('.tmp')
            temporary.write_bytes(payload)
            temporary.replace(destination)
        photo.update(cachedRelativePath=relative.as_posix(), matchMethod=method)
        return photo
    return None


def process(db, row, expanded=False):
    place_id, name, status, manual_fields, tags, city, latitude, longitude = row
    entry = dict(placeId=str(place_id), name=name, attractionStatus=status)
    if 'name' in manual_fields:
        entry.update(outcome='no_candidate', detail='Nazwa poprawiona ręcznie; powiązanie źródłowe wymaga sprawdzenia.')
    else:
        try:
            photo = find_photo(place_id, tags or {},expanded,name,city,latitude,longitude)
            if photo:
                photo_id = uuid.uuid5(uuid.NAMESPACE_URL, str(place_id) + '/commons/' + photo['sourceFile'])
                with db.transaction():
                    db.execute('''insert into catalog.place_photos
                        (id,place_id,provider,source_file,source_page_url,original_url,thumbnail_url,
                         cached_relative_path,author,credit,license,license_url,match_method,metadata,fetched_at_utc)
                        values (%s,%s,'wikimedia_commons',%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,now())
                        on conflict (place_id,provider,source_file) do nothing''',
                        (photo_id, place_id, photo['sourceFile'], photo['sourcePageUrl'], photo['originalUrl'],
                         photo['thumbnailUrl'], photo['cachedRelativePath'], photo['author'], photo['credit'],
                         photo['license'], photo['licenseUrl'], photo['matchMethod'], Jsonb(photo['metadata'])))
                    save_attempt(db, place_id, 'pending_review', 'Zdjęcie oczekuje na sprawdzenie dopasowania i warunków użycia.')
                entry.update(outcome='pending_review', photo=photo)
                return entry
            entry.update(outcome='no_candidate', detail='Brak zdjęcia przez bezpośrednie powiązanie z rozpoznaną licencją.')
        except (urllib.error.URLError, TimeoutError, ValueError, KeyError) as exc:
            detail = 'Wyszukiwanie przerwane: ' + type(exc).__name__
            if isinstance(exc, urllib.error.HTTPError):
                detail += f' (HTTP {exc.code})'
            elif isinstance(exc, ValueError):
                detail += ': ' + str(exc)
            entry.update(outcome='error', detail=detail)
    save_attempt(db, place_id, entry['outcome'], entry['detail'])
    return entry


def save_attempt(db, place_id, outcome, detail):
    db.execute('''insert into catalog.photo_fetch_attempts values (%s,now(),%s,%s)
        on conflict(place_id) do update set attempted_at_utc=excluded.attempted_at_utc,
        outcome=excluded.outcome,detail=excluded.detail''', (place_id, outcome, detail))


def write_report(db, processed):
    REPORTS.mkdir(parents=True, exist_ok=True)
    now = datetime.now(timezone.utc).isoformat()
    report = dict(fetchedAtUtc=now, processed=len(processed),
        outcomes=dict(Counter(entry['outcome'] for entry in processed)), places=processed)
    (REPORTS / 'latest-run.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    pending = db.execute('''select p.name,ph.source_page_url,ph.cached_relative_path,ph.author,
        ph.license,ph.license_url,ph.match_method,ph.id,ph.provider from catalog.place_photos ph
        join catalog.places p on p.id=ph.place_id where ph.status='pending_review'
        order by ph.fetched_at_utc,p.name''').fetchall()
    labels_path = REPORTS / 'review-labels.json'
    labels = json.loads(labels_path.read_text(encoding='utf-8')) if labels_path.exists() else {}
    old_report = REPORTS / 'DO_PRZEJRZENIA.md'
    if not labels and old_report.exists():
        for number, photo_id in re.findall(r'## Z(\d+) .*?Id zdjęcia: `([^`]+)`',
                old_report.read_text(encoding='utf-8'), re.S):
            labels[photo_id] = int(number)
    for photo in pending:
        photo_id = str(photo[7])
        if photo_id not in labels:
            labels[photo_id] = max(labels.values(), default=0) + 1
    labels_path.write_text(json.dumps(labels, indent=2), encoding='utf-8')
    lines = ['# Zdjęcia do sprawdzenia', '',
        'Propozycje nie są jeszcze publikowane przez API. Sprawdź obiekt, autora i warunki licencji na stronie źródłowej.',
        'Pliki zdjęć znajdują się lokalnie w `.local/photo-cache`; raport zawiera wszystkie oczekujące propozycje.', '']
    for name, page, local, author, license_name, license_url, method, photo_id, provider in pending:
        i = labels[str(photo_id)]
        source = f'[Wikimedia Commons]({page})' if provider=='wikimedia_commons' else f'[Zdjęcie udostępnione]({page})' if page else 'Własne/udostępnione zdjęcie'
        license_text = f'[{license_name}]({license_url})' if license_url else license_name
        lines.extend([f'## Z{i:02d} — {name}', '', f'- Id zdjęcia: `{photo_id}`',
            f'- Źródło: {source}', f'- Autor: {author}',
            f'- Licencja: {license_text}', f'- Powiązanie: `{method}`', '',
            f'![Propozycja zdjęcia](../../{local})', ''])
    (REPORTS / 'DO_PRZEJRZENIA.md').write_text('\n'.join(lines), encoding='utf-8')
    print(json.dumps(dict(processed=len(processed), outcomes=report['outcomes'], pendingTotal=len(pending)), ensure_ascii=False))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--limit', type=int, default=20)
    parser.add_argument('--apply-schema', action='store_true')
    parser.add_argument('--dry-run', action='store_true', help='Tylko wybór miejsc, bez sieci Wikimedia i zapisów.')
    parser.add_argument('--retry-errors', action='store_true', help='Ponów wyłącznie błędy poprzednich partii, bez czekania doby.')
    parser.add_argument('--expanded', action='store_true', help='Dodatkowo kategorie Commons i wyszukiwanie nazwy w okolicy.')
    parser.add_argument('--retry-missing', action='store_true', help='Ponów braki wcześniejszej strategii; wymaga --expanded.')
    args = parser.parse_args()
    if not 1 <= args.limit <= 100:
        parser.error('--limit musi być od 1 do 100')
    if args.dry_run and args.apply_schema:
        parser.error('--dry-run nie można łączyć z --apply-schema')
    if args.retry_missing and not args.expanded:
        parser.error('--retry-missing wymaga --expanded')
    cfg = json.loads((ROOT / '.local/supabase.json').read_text(encoding='utf-8'))['Database']
    with tempfile.TemporaryDirectory(prefix='photo-db-') as temporary:
        cert = pathlib.Path(temporary) / 'ca.crt'
        shutil.copyfile(ROOT / '.local/supabase-ca.crt', cert)
        with psycopg.connect(host=cfg['Host'],port=cfg['Port'],dbname=cfg['Name'],user=cfg['Username'],
            password=cfg['Password'],sslmode='verify-full',sslrootcert=str(cert),connect_timeout=15,autocommit=True) as db:
            if args.apply_schema:
                db.execute((ROOT / 'database/003_place_photos.sql').read_text(encoding='utf-8'), prepare=False)
            # Jeden proces na katalog, także gdy dwie partie zostaną uruchomione równolegle.
            locked = db.execute('select pg_try_advisory_lock(718240503)').fetchone()[0]
            if not locked:
                raise RuntimeError('Inna partia zdjęć jest już uruchomiona.')
            retry_filter = "and exists(select 1 from catalog.photo_fetch_attempts a where a.place_id=p.id and a.outcome='error')" if args.retry_errors else ''
            rows = db.execute('''select p.id,p.name,p.status,p.manually_edited_fields,
                coalesce(s.raw_data->'sourceDetails'->'raw','{}'::jsonb) as tags,p.city,
                extensions.st_y(p.location::extensions.geometry),extensions.st_x(p.location::extensions.geometry)
                from catalog.places p join catalog.place_sources s on s.place_id=p.id and s.provider='geoapify'
                where p.kind='attraction' and p.status in ('active','awaiting_photo')
                and not exists(select 1 from catalog.place_photos ph where ph.place_id=p.id)
                and not exists(select 1 from catalog.photo_fetch_attempts a where a.place_id=p.id
                    and (a.outcome='pending_review' or a.attempted_at_utc > now() -
                        case when a.outcome='error' then interval '1 day' else interval '30 days' end)
                    and not (%s and a.outcome='error')
                    and not (%s and a.outcome='no_candidate'
                        and a.detail not like 'Expanded:%%'))
                ''' + retry_filter + '''
                order by (s.raw_data->'sourceDetails'->'raw' ?| array['wikidata','wikimedia_commons','image']) desc,
                    p.name,p.id limit %s''', (args.retry_errors,args.retry_missing,args.limit)).fetchall()
            if args.dry_run:
                print(json.dumps({'selected':len(rows),'places':[r[1] for r in rows]},ensure_ascii=False))
                return
            processed = []
            for row in rows:
                processed.append(process(db, row,args.expanded))
                if args.expanded and processed[-1]['outcome']=='no_candidate':
                    save_attempt(db,row[0],'no_candidate','Expanded: '+processed[-1]['detail'])
                print(f"[{len(processed)}/{len(rows)}] {row[1]}: {processed[-1]['outcome']}", flush=True)
            write_report(db, processed)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Nie ujawniamy treści błędów połączenia ani lokalnej konfiguracji.
        print('Nie udało się ukończyć partii: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
