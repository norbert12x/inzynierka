"""Generuje SQL importu aktualnego katalogu; bez klucza, sieci i builda."""
import hashlib
import json
import re
import uuid
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data/curated-beta-v1"
OUTPUT = ROOT / "database"
CATEGORY_CODES = {"museums", "heritage", "religious-sites", "nature", "viewpoints", "recreation", "culture", "public-art", "other"}


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def internal_id(external_id):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "https://api.geoapify.com/places/" + external_id))


def categories(place):
    if "categoryCodes" in place:
        result = set(place["categoryCodes"])
    else:
        tags = set(place.get("sourceCategories", []))
        result = set()
        if "entertainment.museum" in tags or "entertainment.culture.gallery" in tags:
            result.add("museums")
        if "heritage" in tags or "tourism.sights" in tags:
            result.add("heritage")
        if any("place_of_worship" in tag for tag in tags):
            result.add("religious-sites")
        if "leisure.park" in tags or "natural" in tags:
            result.add("nature")
        if "tourism.attraction.viewpoint" in tags:
            result.add("viewpoints")
        if any(tag in tags for tag in ("entertainment.theme_park", "entertainment.zoo", "entertainment.water_park", "entertainment.activity_park", "entertainment.planetarium")):
            result.add("recreation")
        if "entertainment.culture" in tags:
            result.add("culture")
        if "tourism.attraction.artwork" in tags:
            result.add("public-art")
        if not result:
            result.add("other")
    assert result and result <= CATEGORY_CODES, (place["name"], result)
    return sorted(result)


def main():
    document = read(DATA / "catalog.json")
    standalone = document["places"]
    rows = []
    for p in standalone:
        rows.append(p)
        rows.extend(p["components"])
    rows.extend(read(DATA / "pending-components.json"))
    rows.extend(read(DATA / "removed.json"))
    assert len(rows) == 600 and len({p["id"] for p in rows}) == 600
    manual = {}
    history = read(DATA / "changes.json")
    for change in history["changes"]:
        field = change.get("field") or {"await-photo": "status", "make-component": "parent_id", "remove-from-working-catalog": "status"}.get(change.get("action"))
        if field:
            manual.setdefault(change["id"], set()).add(field)
    normalized = []
    ids = {p["id"] for p in rows}
    for p in rows:
        parent = p.get("parentId")
        assert parent is None or parent in ids
        assert -90 <= p['latitude'] <= 90 and -180 <= p['longitude'] <= 180
        confirmation = p.get("openingHoursConfirmation") or {}
        normalized.append({
            "id": internal_id(p['id']), "name": p['name'], "description": None,
            "city": p.get('city'), "address": p.get('address'),
            "latitude": p['latitude'], "longitude": p['longitude'],
            "status": p['catalogStatus'],
            "kind": 'component' if p['catalogStatus'] == 'component' else 'attraction',
            "parentId": internal_id(parent) if parent else None,
            "openingHours": p.get('openingHours'),
            "openingHoursVerification": p.get('openingHoursVerification', 'unverified'),
            "openingHoursConfirmedOn": confirmation.get('confirmedOn'),
            "website": p.get('website'), "estimatedVisitMinutes": p.get('estimatedVisitMinutes'),
            "manuallyEditedFields": sorted(manual.get(p['id'], set())),
            "categories": categories(p),
            "sourceId": str(uuid.uuid5(uuid.NAMESPACE_URL, 'geoapify-source:' + p['id'])),
            "externalId": p['id'], "fetchedAtUtc": document['fetchedAtUtc'],
            "rawData": {"sourceDetails": p.get('sourceDetails'), "sourceCategories": p.get('sourceCategories', []),
                        "attribution": document['attribution'], "providerUrl": document['providerUrl'], "licenseUrl": document['licenseUrl']}
        })
    # Długa wartość JSON jako literał dolarowy; klucze i hasła nigdy tu nie trafiają.
    payload = json.dumps(normalized, ensure_ascii=False, separators=(',', ':'))
    assert '$catalog_import$' not in payload
    sql = '''-- Wykonaj po 001_catalog_schema.sql. Import 600 rekordów z lokalnej kopii.
-- Ponowienie nie nadpisuje ręcznych edycji miejsc ani ich kategorii.
begin;
select pg_advisory_xact_lock(1791100001);
create temporary table import_payload (value jsonb) on commit drop;
insert into import_payload values ($catalog_import$''' + payload + '''$catalog_import$::jsonb);
create temporary table newly_imported (id uuid primary key) on commit drop;

with inserted as (
    insert into catalog.places (
        id, name, description, city, address, location, status, kind, parent_id,
        opening_hours, opening_hours_verification, opening_hours_confirmed_on,
        website, estimated_visit_minutes, manually_edited_fields)
    select
        (p->>'id')::uuid, p->>'name', p->>'description', p->>'city', p->>'address',
        extensions.st_setsrid(extensions.st_makepoint((p->>'longitude')::float8,
            (p->>'latitude')::float8), 4326)::extensions.geography,
        p->>'status', p->>'kind', (p->>'parentId')::uuid,
        p->>'openingHours', p->>'openingHoursVerification', (p->>'openingHoursConfirmedOn')::date,
        p->>'website', (p->>'estimatedVisitMinutes')::integer,
        array(select jsonb_array_elements_text(p->'manuallyEditedFields'))
    from import_payload, lateral jsonb_array_elements(value) p
    on conflict (id) do nothing
    returning id
)
insert into newly_imported select id from inserted;

insert into catalog.place_categories (place_id, category_code)
select (p->>'id')::uuid, category
from import_payload, lateral jsonb_array_elements(value) p,
    lateral jsonb_array_elements_text(p->'categories') category
where (p->>'id')::uuid in (select id from newly_imported)
on conflict do nothing;

insert into catalog.place_sources (id, place_id, provider, external_id, fetched_at_utc, raw_data)
select (p->>'sourceId')::uuid, (p->>'id')::uuid, 'geoapify', p->>'externalId',
       (p->>'fetchedAtUtc')::timestamptz, p->'rawData'
from import_payload, lateral jsonb_array_elements(value) p
on conflict (provider, external_id) do update
    set fetched_at_utc = excluded.fetched_at_utc, raw_data = excluded.raw_data
    where excluded.fetched_at_utc >= catalog.place_sources.fetched_at_utc;

select count(*) as newly_inserted_places from newly_imported;
select status, count(*) as count from catalog.places group by status order by status;
commit;
'''
    OUTPUT.mkdir(exist_ok=True)
    (OUTPUT/'002_import_beta_catalog.sql').write_text(sql, encoding='utf-8')
    report = {
        "records": len(normalized), "statuses": dict(Counter(p['status'] for p in normalized)),
        "categories": dict(Counter(c for p in normalized for c in p['categories'])),
        "linkedComponents": sum(p['parentId'] is not None for p in normalized),
        "unassignedComponents": sum(p['kind']=='component' and p['parentId'] is None for p in normalized),
        "confirmedHours": sum(p['openingHoursVerification']=='confirmed_by_user' for p in normalized),
        "catalogSha256": hashlib.sha256((DATA/'catalog.json').read_bytes()).hexdigest(),
        "note": "To raport przygotowania, nie dowód wykonania importu w Supabase. Brak ponownego pobierania danych."
    }
    (OUTPUT/'import-preview.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
