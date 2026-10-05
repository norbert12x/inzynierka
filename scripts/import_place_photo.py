"""Przypisuje własne/udostępnione zdjęcie do atrakcji jako propozycję do przeglądu."""
import argparse
import hashlib
import json
import pathlib
import shutil
import tempfile
import uuid
from PIL import Image
from fetch_place_photos import ROOT, CACHE, psycopg, Jsonb, write_report


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--place-id',type=uuid.UUID,required=True)
    parser.add_argument('--file',type=pathlib.Path,required=True)
    parser.add_argument('--author',required=True)
    parser.add_argument('--license',required=True,choices=['CC0','CC BY 4.0','CC BY-SA 4.0','permission'])
    parser.add_argument('--rights-note',required=True,help='Opis autorstwa lub uzyskanej zgody na wykorzystanie.')
    parser.add_argument('--source-url',default='')
    parser.add_argument('--apply-schema',action='store_true')
    args=parser.parse_args()
    if not args.author.strip() or not args.rights_note.strip():parser.error('Autor i opis praw nie mogą być puste.')
    if args.source_url and not args.source_url.startswith('https://'):parser.error('Źródło musi być adresem HTTPS.')
    payload=args.file.read_bytes()
    if len(payload)>8*1024*1024:parser.error('Maksymalny rozmiar zdjęcia to 8 MB.')
    with Image.open(args.file) as image:
        extension={'JPEG':'.jpg','PNG':'.png','WEBP':'.webp'}.get(image.format)
        if not extension:parser.error('Obsługiwane formaty to JPEG, PNG i WebP.')
        image.verify()
    digest=hashlib.sha256(payload).hexdigest()
    photo_id=uuid.uuid5(uuid.NAMESPACE_URL,str(args.place_id)+'/provided/'+digest)
    relative=pathlib.Path('.local/photo-cache')/(str(args.place_id)+'-provided-'+digest[:20]+extension)
    license_url={'CC0':'https://creativecommons.org/publicdomain/zero/1.0/',
        'CC BY 4.0':'https://creativecommons.org/licenses/by/4.0/',
        'CC BY-SA 4.0':'https://creativecommons.org/licenses/by-sa/4.0/','permission':''}[args.license]
    cfg=json.loads((ROOT/'.local/supabase.json').read_text(encoding='utf-8'))['Database']
    with tempfile.TemporaryDirectory(prefix='provided-photo-') as temp:
        cert=pathlib.Path(temp)/'ca.crt';shutil.copyfile(ROOT/'.local/supabase-ca.crt',cert)
        with psycopg.connect(host=cfg['Host'],port=cfg['Port'],dbname=cfg['Name'],user=cfg['Username'],
            password=cfg['Password'],sslmode='verify-full',sslrootcert=str(cert),connect_timeout=15,autocommit=True) as db:
            if args.apply_schema:
                db.execute((ROOT/'database/004_user_provided_photos.sql').read_text(encoding='utf-8'),prepare=False)
            with db.transaction():
                db.execute('select pg_advisory_xact_lock(718240503)')
                row=db.execute("select name from catalog.places where id=%s and kind='attraction' and status in ('active','awaiting_photo') for share",(args.place_id,)).fetchone()
                if not row:parser.error('Nie znaleziono aktywnej atrakcji lub muralu dla podanego ID.')
                CACHE.mkdir(parents=True,exist_ok=True)
                destination=ROOT/relative
                if not destination.exists():
                    temporary=destination.with_suffix('.tmp');temporary.write_bytes(payload);temporary.replace(destination)
                db.execute('''insert into catalog.place_photos
                    (id,place_id,provider,source_file,source_page_url,original_url,thumbnail_url,cached_relative_path,
                    author,credit,license,license_url,match_method,metadata,fetched_at_utc)
                    values(%s,%s,'user_provided',%s,%s,'','',%s,%s,%s,%s,%s,'user_provided',%s,now())
                    on conflict(place_id,provider,source_file) do nothing''',
                    (photo_id,args.place_id,'sha256:'+digest,args.source_url,relative.as_posix(),args.author.strip(),
                     args.rights_note.strip(),args.license,license_url,Jsonb({'rightsNote':args.rights_note.strip()})))
                saved=db.execute('select id,status from catalog.place_photos where place_id=%s and provider=\'user_provided\' and source_file=%s',
                    (args.place_id,'sha256:'+digest)).fetchone()
            write_report(db,[])
            print(json.dumps({'photoId':str(saved[0]),'placeName':row[0],'status':saved[1]},ensure_ascii=False))


if __name__=='__main__':
    try:main()
    except Exception as error:raise SystemExit('Import zdjęcia nie powiódł się: '+type(error).__name__)
