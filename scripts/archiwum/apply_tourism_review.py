"""Stosuje jawne decyzje selekcji beta z 2026-10-05; bez builda i kasowania źródeł."""
import argparse
import hashlib
import json
import pathlib
import shutil
import tempfile
import uuid
from collections import Counter
from datetime import datetime, timezone

from fetch_place_photos import ROOT, psycopg, write_report
from prepare_catalog_import import internal_id

DATA = ROOT / 'data/curated-beta-v1'
REVIEW = ROOT / 'data/tourism-review-2026-10-05'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true', help='Zastosuj zapisane decyzje w Supabase i kopii lokalnej.')
    args = parser.parse_args()
    plan = read(REVIEW / 'decisions.json')
    marker = plan['reviewId']
    catalog, removed, history = [read(DATA / name) for name in ['catalog.json', 'removed.json', 'changes.json']]
    if any(c.get('reviewId') == marker for c in history['changes']):
        print('Ta selekcja jest już zapisana; brak ponownych zmian.')
        return
    assert hashlib.sha256((DATA / 'catalog.json').read_bytes()).hexdigest() == plan['beforeCatalogSha256'], 'Katalog zmienił się od przeglądu.'
    places = {p['sourceRow']: p for p in catalog['places']}
    decisions = plan['decisions']
    assert len(decisions) == len(places) == 515
    assert {d['sourceRow'] for d in decisions} == places.keys()
    for d in decisions:
        p = places[d['sourceRow']]
        assert internal_id(p['id']) == d['id'] and p['name'] == d['name'] and p['catalogStatus'] == d['beforeStatus']
    kept = {d['sourceRow'] for d in decisions if d['action'] == 'keep'}
    for d in decisions:
        if d['action'] == 'keep':
            continue
        p = places[d['sourceRow']]
        p['catalogStatus'] = d['afterStatus']
        if d['action'] == 'component':
            assert d['parentSourceRow'] in kept
            parent = places[d['parentSourceRow']]
            p.update(parentId=parent['id'], parentAssignmentStatus='assigned', standaloneStopAllowed=False)
            parent['components'].append(p)
        else:
            removed.append(p)
        history['changes'].append(dict(id=p['id'], sourceRow=p['sourceRow'],
            action='make-component' if d['action'] == 'component' else 'remove-from-working-catalog',
            parentId=p.get('parentId'), reason=d['reason'], reviewId=marker))
    catalog['places'] = [p for p in catalog['places'] if p['sourceRow'] in kept]
    catalog['count'] = len(catalog['places'])
    catalog['curatedAtUtc'] = datetime.now(timezone.utc).isoformat()
    history['summary'].update(standaloneCount=len(catalog['places']),
        activeCount=sum(p['catalogStatus'] == 'active' for p in catalog['places']),
        awaitingPhotoCount=sum(p['catalogStatus'] == 'awaiting_photo' for p in catalog['places']),
        removedCount=len(removed), linkedComponentCount=sum(len(p['components']) for p in catalog['places']))
    history['unansweredGroups'] = []
    history['tourismReview'] = {'reviewId': marker, 'decisionsFile': 'data/tourism-review-2026-10-05/decisions.json',
        'note': 'Selekcja redakcyjna beta, nie niezależne potwierdzenie dostępności wszystkich miejsc.'}
    all_places = catalog['places'] + [c for p in catalog['places'] for c in p['components']] + removed + read(DATA / 'pending-components.json')
    assert len(all_places) == len({p['id'] for p in all_places}) == 600
    assert history['summary']['activeCount'] == 152 and len(removed) == 326
    if not args.apply:
        print(json.dumps(history['summary'], ensure_ascii=False))
        return

    backup = ROOT / '.local/backups' / marker
    backup.mkdir(parents=True, exist_ok=True)
    for filename in ['catalog.json', 'removed.json', 'changes.json', 'pending-components.json']:
        if not (backup / filename).exists():
            shutil.copyfile(DATA / filename, backup / filename)
    cfg = read(ROOT / '.local/supabase.json')['Database']
    with tempfile.TemporaryDirectory(prefix='tourism-review-') as temp:
        cert = pathlib.Path(temp) / 'ca.crt'
        shutil.copyfile(ROOT / '.local/supabase-ca.crt', cert)
        with psycopg.connect(host=cfg['Host'], port=cfg['Port'], dbname=cfg['Name'], user=cfg['Username'],
            password=cfg['Password'], sslmode='verify-full', sslrootcert=str(cert), connect_timeout=15, autocommit=True) as db:
            with db.transaction():
                db.execute('select pg_advisory_xact_lock(1791100001)')
                db.execute('select pg_advisory_xact_lock(718240503)')
                ids = [uuid.UUID(d['id']) for d in decisions]
                before = db.execute('select id,name,kind,status,parent_id,manually_edited_fields from catalog.places where id=any(%s) for update', (ids,)).fetchall()
                actual = {str(r[0]): r for r in before}
                assert len(actual) == 515
                for d in decisions:
                    row = actual[d['id']]
                    # Dopuszczamy stan po tej samej operacji na wypadek przerwania zapisu lokalnych plików.
                    assert row[1] == d['name'] and row[3] in [d['beforeStatus'], d['afterStatus']]
                    assert row[2] in ['attraction', 'component']
                photo_before = db.execute('select id,place_id,status from catalog.place_photos where place_id=any(%s)', (ids,)).fetchall()
                if not (backup / 'database-before.json').exists():
                    (backup / 'database-before.json').write_text(json.dumps({'places': before, 'photos': photo_before}, default=str, ensure_ascii=False, indent=2), encoding='utf-8')
                remove_ids = [uuid.UUID(d['id']) for d in decisions if d['action'] == 'remove']
                db.execute("""update catalog.places set status='removed',
                    manually_edited_fields=array(select distinct unnest(manually_edited_fields || array['status']))
                    where id=any(%s)""", (remove_ids,))
                db.execute("update catalog.place_photos set status='rejected' where place_id=any(%s) and status<>'rejected'", (remove_ids,))
                with db.cursor() as cursor:
                    cursor.executemany("""update catalog.places set kind='component',status='component',parent_id=%s,
                        manually_edited_fields=array(select distinct unnest(manually_edited_fields || array['status','kind','parent_id']))
                        where id=%s""", [(uuid.UUID(d['parentId']), uuid.UUID(d['id'])) for d in decisions if d['action'] == 'component'])
                after = {str(r[0]): r for r in db.execute('select id,kind,status,parent_id from catalog.places where id=any(%s)', (ids,)).fetchall()}
                for d in decisions:
                    row = after[d['id']]
                    assert row[2] == d['afterStatus']
                    assert row[1] == ('component' if d['action'] == 'component' else 'attraction')
                    if d['action'] == 'component':
                        assert str(row[3]) == d['parentId']
                groups = db.execute('select kind,status,count(*) from catalog.places group by kind,status order by kind,status').fetchall()
                assert sum(g[2] for g in groups) == 600
                assert db.execute("select count(*) from catalog.places where parent_id is not null and parent_id in (select id from catalog.places where status='removed')").fetchone()[0] == 0
                assert db.execute("select count(*) from catalog.place_photos ph join catalog.places p on p.id=ph.place_id where p.status='removed' and ph.status<>'rejected'").fetchone()[0] == 0
            # Pliki lokalne można odtworzyć z planu, gdyby zapis przerwał się po transakcji DB.
            for filename, value in [('catalog.json', catalog), ('removed.json', removed), ('changes.json', history)]:
                staged = DATA / (filename + '.tmp')
                staged.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
                staged.replace(DATA / filename)
            report = {'reviewId': marker, 'appliedAtUtc': catalog['curatedAtUtc'], 'databaseApplied': True,
                'summary': history['summary'], 'decisionsApplied': plan['summary'],
                'databaseGroups': [{'kind': k, 'status': s, 'count': n} for k,s,n in groups],
                'all600SourceIdsPreserved': True, 'photosOfRemovedPlacesRejected': True,
                'componentPhotosNotAutomaticallyApprovedOrTransferred': True}
            (REVIEW / 'applied.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
            write_report(db, [])
            print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        raise SystemExit('Nie udało się zastosować selekcji: ' + type(error).__name__)
