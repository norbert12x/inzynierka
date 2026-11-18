"""Raport wszystkich braków zdjęć, bez zmian w bazie i bez builda."""
import csv
import json
import pathlib
import shutil
import tempfile
from datetime import datetime, timezone
from fetch_place_photos import ROOT, REPORTS, psycopg


def main():
    cfg = json.loads((ROOT/'.local/supabase.json').read_text(encoding='utf-8'))['Database']
    with tempfile.TemporaryDirectory(prefix='photo-coverage-') as temp:
        cert = pathlib.Path(temp)/'ca.crt'
        shutil.copyfile(ROOT/'.local/supabase-ca.crt',cert)
        with psycopg.connect(host=cfg['Host'],port=cfg['Port'],dbname=cfg['Name'],user=cfg['Username'],
            password=cfg['Password'],sslmode='verify-full',sslrootcert=str(cert),connect_timeout=15) as db:
            db.execute('set transaction read only')
            rows = db.execute('''select p.id,p.name,p.city,p.status,p.website,
                coalesce(a.outcome,'not_checked'),coalesce(a.detail,''),
                (select count(*) from catalog.place_photos ph where ph.place_id=p.id and ph.status='approved'),
                (select count(*) from catalog.place_photos ph where ph.place_id=p.id and ph.status='pending_review')
                from catalog.places p left join catalog.photo_fetch_attempts a on a.place_id=p.id
                where p.kind='attraction' and p.status in ('active','awaiting_photo') order by p.name,p.id''').fetchall()
    entries = []
    for row in rows:
        place_id,name,city,status,website,outcome,detail,approved,pending = row
        entries.append(dict(id=str(place_id),name=name,city=city,attractionStatus=status,
            website=website,attemptOutcome=outcome,detail=detail,approvedPhotos=approved,
            pendingPhotos=pending,photoStatus='approved' if approved else 'needs_review' if pending else 'missing',
            nextStep='Sprawdź propozycję' if pending else 'Własne zdjęcie lub zdjęcie udostępnione przez właściciela' if not approved else 'Gotowe'))
    summary = dict(eligibleAttractions=len(entries),withApprovedPhoto=sum(bool(e['approvedPhotos']) for e in entries),
        withCandidatePhoto=sum(bool(e['pendingPhotos']) for e in entries),
        missingPhoto=sum(not(e['approvedPhotos'] or e['pendingPhotos']) for e in entries),
        unchecked=sum(e['attemptOutcome']=='not_checked' and e['photoStatus']=='missing' for e in entries))
    REPORTS.mkdir(parents=True,exist_ok=True)
    report = dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(),summary=summary,places=entries)
    (REPORTS/'coverage.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    missing = [e for e in entries if e['photoStatus']=='missing']
    with (REPORTS/'BRAKUJACE_ZDJECIA.csv').open('w',encoding='utf-8-sig',newline='') as file:
        writer=csv.DictWriter(file,fieldnames=list(entries[0]) if entries else [],delimiter=';')
        writer.writeheader();writer.writerows(missing)
    lines=['# Pokrycie katalogu zdjęciami','',
        f"Atrakcje aktywne i murale warunkowe: **{summary['eligibleAttractions']}**.",
        f"Zatwierdzone zdjęcie: **{summary['withApprovedPhoto']}**; propozycja do przeglądu: **{summary['withCandidatePhoto']}**; bez zdjęcia: **{summary['missingPhoto']}**.",
        f"Jeszcze niesprawdzone automatycznie: **{summary['unchecked']}**.",'',
        'Propozycja nie gwarantuje poprawnego dopasowania. Brak zdjęcia nie jest zastępowany przypadkową fotografią ani grafiką udającą zdjęcie obiektu.',
        'Dla pozostałych miejsc potrzebna jest fotografia własna lub udostępniona przez uprawnionego autora/właściciela. Google nie jest używane, zgodnie z decyzją użytkownika.',
        '', '## Miejsca bez zdjęcia','']
    for e in missing:
        name=e['name'].replace('\n',' ')
        lines.append(f"- {name} — {e['city'] or 'brak miasta'}; wynik: {e['attemptOutcome']}." +
            (f" Strona obiektu: {e['website']}" if e['website'] else ''))
    (REPORTS/'POKRYCIE.md').write_text('\n'.join(lines),encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False))


if __name__=='__main__':
    try:main()
    except Exception as error:raise SystemExit('Raport zdjęć nie powiódł się: '+type(error).__name__)
