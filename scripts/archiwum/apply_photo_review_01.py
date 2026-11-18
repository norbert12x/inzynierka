"""Zapisuje decyzje użytkownika Z04/Z05/Z10 z 2026-10-05. Bez builda."""
import json
import pathlib
import shutil
import tempfile
from datetime import datetime, timezone

from fetch_place_photos import ROOT, psycopg, write_report
from prepare_catalog_import import internal_id

DATA = ROOT / 'data/curated-beta-v1'
MARKER = 'photo-review-01-Z04-Z05-Z10'


def load(name):
    return json.loads((DATA / name).read_text(encoding='utf-8'))


def main():
    catalog, removed, history = load('catalog.json'), load('removed.json'), load('changes.json')
    all_places = catalog['places'] + removed + [c for p in catalog['places'] for c in p['components']]
    by_row = {p['sourceRow']: p for p in all_places}
    gate, parent = by_row[327], by_row[109]
    excluded = [by_row[306], by_row[165]]
    assert gate['name'] == 'Brama Główna' and parent['name'] == 'Muzeum - Zamek w Łańcucie'
    assert [p['name'] for p in excluded] == ['Dawna synagoga w Czudcu', 'Izba Pamięci Sługi Bożego ks. Stanisława Sudoła']
    ids = {p['sourceRow']: internal_id(p['id']) for p in [gate, parent, *excluded]}
    cfg = json.loads((ROOT / '.local/supabase.json').read_text(encoding='utf-8'))['Database']
    with tempfile.TemporaryDirectory(prefix='photo-review-') as tmp:
        cert = pathlib.Path(tmp) / 'ca.crt'
        shutil.copyfile(ROOT / '.local/supabase-ca.crt', cert)
        with psycopg.connect(host=cfg['Host'],port=cfg['Port'],dbname=cfg['Name'],user=cfg['Username'],
            password=cfg['Password'],sslmode='verify-full',sslrootcert=str(cert),connect_timeout=15,autocommit=True) as db:
            with db.transaction():
                db.execute('select pg_advisory_xact_lock(718240503)')
                for p in [gate, parent, *excluded]:
                    row = db.execute('select name from catalog.places where id=%s for update', (ids[p['sourceRow']],)).fetchone()
                    assert row and row[0] == p['name']
                db.execute("""update catalog.places set kind='component',status='component',parent_id=%s,
                    manually_edited_fields=array(select distinct unnest(manually_edited_fields || array['parent_id','status','kind']))
                    where id=%s""", (ids[109], ids[327]))
                db.execute("""update catalog.place_photos set place_id=%s,match_method='user_review_Z04:castle_gate'
                    where id='7b0894c3-b615-56c2-8418-4702ca37bfa8' and place_id in (%s,%s)""",
                    (ids[109], ids[327], ids[109]))
                for p in excluded:
                    place_id = ids[p['sourceRow']]
                    db.execute("""update catalog.places set status='removed',
                        manually_edited_fields=array(select distinct unnest(manually_edited_fields || array['status']))
                        where id=%s""", (place_id,))
                    db.execute("update catalog.place_photos set status='rejected' where place_id=%s", (place_id,))
                assert db.execute("select count(*) from catalog.place_photos where place_id=%s and id='7b0894c3-b615-56c2-8418-4702ca37bfa8'", (ids[109],)).fetchone()[0] == 1

            if not any(c.get('reviewId') == MARKER for c in history['changes']):
                catalog['places'] = [p for p in catalog['places'] if p['sourceRow'] not in (327, 306, 165)]
                gate.update(catalogStatus='component', standaloneStopAllowed=False,
                    parentId=parent['id'], parentAssignmentStatus='assigned')
                parent['components'].append(gate)
                for p in excluded:
                    p['catalogStatus'] = 'removed'
                    removed.append(p)
                    history['changes'].append(dict(id=p['id'],sourceRow=p['sourceRow'],
                        action='remove-from-working-catalog',reason='Decyzja użytkownika podczas przeglądu zdjęć Z05/Z10.',reviewId=MARKER))
                history['changes'].append(dict(id=gate['id'],sourceRow=327,action='make-component',
                    parentId=parent['id'],reason='Z04: brama jako element zamku; zdjęcie przypisane zamkowi.',reviewId=MARKER))
            catalog['count'] = len(catalog['places'])
            catalog['curatedAtUtc'] = datetime.now(timezone.utc).isoformat()
            summary = history['summary']
            summary.update(standaloneCount=len(catalog['places']),
                activeCount=sum(p['catalogStatus']=='active' for p in catalog['places']),
                awaitingPhotoCount=sum(p['catalogStatus']=='awaiting_photo' for p in catalog['places']),
                removedCount=len(removed),linkedComponentCount=sum(len(p['components']) for p in catalog['places']))
            assert summary['activeCount']==482 and summary['removedCount']==25 and summary['linkedComponentCount']==8
            for name, value in [('catalog.json',catalog),('removed.json',removed),('changes.json',history)]:
                (DATA/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
            decisions = dict(reviewId=MARKER, confirmedAtUtc=catalog['curatedAtUtc'],
                actions=[dict(label='Z04',action='move_photo_to_parent_and_make_component',placeId=ids[327],parentId=ids[109]),
                    *[dict(label=label,action='remove_from_catalog_and_reject_photo',placeId=ids[p['sourceRow']])
                      for label,p in zip(['Z05','Z10'],excluded)]])
            (ROOT/'data/photos/review-decisions-01.json').write_text(json.dumps(decisions,ensure_ascii=False,indent=2),encoding='utf-8')
            write_report(db, [])
            print(json.dumps(summary,ensure_ascii=False))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        raise SystemExit('Nie udało się zastosować decyzji: ' + type(error).__name__)
