"""Pobiera z Overture Maps Places kandydatów aktywnej rozrywki i atrakcji dla obszarów z appsettings.json.

Bez builda i bez bazy: czyta publiczne pliki Parquet z S3 (DuckDB pobiera tylko fragmenty dla prostokąta obszaru)
i zapisuje data/overture/<wydanie>/candidates.json. Przegląd i dodawanie: strona /dev/atrakcje → „Z Overture”.

    python scripts/fetch_overture_places.py              # najnowsze wydanie, wszystkie obszary
    python scripts/fetch_overture_places.py --release 2026-10-21.0 --region rzeszow

Licencje (zapis dozwolony z oznaczeniem źródła): Meta, Microsoft i inni – CDLA Permissive 2.0, Foursquare – Apache 2.0,
AllThePlaces – CC0. Szczegóły i porównanie źródeł: docs/zrodla-danych.md.
"""
import argparse
import json
import math
import pathlib
import re
import sys
import urllib.request
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".local/python-packages"))
import duckdb  # noqa: E402  (pakiet z .local/python-packages)

BUCKET = "https://overturemaps-us-west-2.s3.amazonaws.com"
OUT_DIR = ROOT / "data/overture"
MIN_CONFIDENCE = 0.5

# Kategorie Overture → proponowana kategoria naszego katalogu (użytkownik może zmienić przy dodawaniu).
CATEGORY_MAP = {
    "escape_room": "active-entertainment", "paintball": "active-entertainment",
    "canoe_and_kayak_hire_service": "active-entertainment", "boat_rental_and_training": "active-entertainment",
    "atv_rental_tour": "active-entertainment", "bike_rental": "active-entertainment",
    "horse_riding": "active-entertainment", "equestrian_facility": "active-entertainment",
    "horseback_riding_service": "active-entertainment", "bowling_alley": "active-entertainment",
    "go_kart_club": "active-entertainment", "race_track": "active-entertainment",
    "rock_climbing_spot": "active-entertainment", "adventure_sports_center": "active-entertainment",
    "shooting_range": "active-entertainment", "archery_range": "active-entertainment",
    "ice_skating_rink": "active-entertainment", "roller_skating_rink": "active-entertainment",
    "pool_billiards": "active-entertainment", "karaoke_venue": "active-entertainment",
    "arcade": "active-entertainment", "trampoline_park": "active-entertainment", "laser_tag": "active-entertainment",
    "ropes_course": "active-entertainment", "zipline": "active-entertainment", "rafting": "active-entertainment",
    "kayaking": "active-entertainment", "horseback_riding": "active-entertainment",
    "amusement_park": "recreation", "water_park": "recreation", "mini_golf": "recreation", "golf_course": "recreation",
    "driving_range": "recreation", "zoo": "recreation", "petting_zoo": "recreation", "planetarium": "culture",
    "wildlife_sanctuary": "nature",
}
# Nazwy pomagają złapać źle skategoryzowane miejsca (np. kręgielnia jako „dance_club”, park trampolin jako „park”).
NAME_PATTERNS = [
    r"laser\s*(tag|arena|game)|laserow\w* twierdz", r"escape", r"kajak|spływ|splyw", r"trampolin|happy jump",
    r"gokart|karting|reskart", r"kręgiel|kregiel|bowling", r"park linowy|linoskocz|wspinacz|ścianka|scianka",
    r"paintball|airsoft", r"park rozrywki|sala zabaw", r"alpak", r"stadnin|jazda konna", r"quad",
]
# Fałszywe trafienia nazw: usługi i sklepy, nie atrakcje.
EXCLUDE = r"wypożyczalnia (samochod|busów|sprzętu|strojów)|klinik|clinic|depilac|kosmet|sklep|serwis|salon"

ATTRIBUTION = {
    "Foursquare": "Copyright 2024 Foursquare Labs, Inc. All rights reserved. (Apache 2.0)",
    "AllThePlaces": "Data from AllThePlaces. Available under CC0 1.0.",
}


def attribution_for(dataset):
    return ATTRIBUTION.get(dataset, f"Data from {dataset}. Available under CDLA Permissive 2.0.")


def latest_release():
    with urllib.request.urlopen(f"{BUCKET}/?list-type=2&prefix=release/&delimiter=/", timeout=30) as response:
        xml = response.read().decode("utf-8")
    releases = re.findall(r"<Prefix>release/([0-9]{4}-[0-9]{2}-[0-9]{2}\.[0-9]+)/</Prefix>", xml)
    if not releases:
        raise SystemExit("Nie znaleziono wydań Overture na S3.")
    return sorted(releases)[-1]


def regions(selected):
    settings = json.loads((ROOT / "Inżynierka/appsettings.json").read_text(encoding="utf-8-sig"))
    items = settings["Catalog"]["Regions"]
    if selected:
        items = [r for r in items if r["Code"] == selected]
        if not items:
            raise SystemExit(f"Nieznany obszar: {selected}")
    return items


def bbox(region):
    lat, lon, radius = region["CenterLatitude"], region["CenterLongitude"], region["RadiusKm"]
    d_lat = radius / 111.32
    d_lon = radius / (111.32 * math.cos(math.radians(lat)))
    return lat - d_lat, lat + d_lat, lon - d_lon, lon + d_lon


def distance_km(lat1, lon1, lat2, lon2):
    r = math.radians
    h = math.sin(r(lat2 - lat1) / 2) ** 2 + math.cos(r(lat1)) * math.cos(r(lat2)) * math.sin(r(lon2 - lon1) / 2) ** 2
    return 2 * 6371.0 * math.asin(min(1.0, math.sqrt(h)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--release", help="np. 2026-10-21.0; domyślnie najnowsze")
    parser.add_argument("--region", help="kod obszaru z appsettings; domyślnie wszystkie")
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")

    release = args.release or latest_release()
    con = duckdb.connect()
    con.execute("INSTALL httpfs; LOAD httpfs; SET s3_region='us-west-2'; SET enable_progress_bar=false;")
    source = f"s3://overturemaps-us-west-2/release/{release}/theme=places/type=place/*"
    categories = ", ".join(f"'{c}'" for c in CATEGORY_MAP)
    names = " OR ".join(f"regexp_matches(lower(names.primary), '{p}')" for p in NAME_PATTERNS)

    items, seen = [], set()
    for region in regions(args.region):
        lat_min, lat_max, lon_min, lon_max = bbox(region)
        rows = con.execute(f"""
            SELECT id, names.primary, taxonomy.primary, taxonomy.hierarchy, confidence, operating_status,
                   websites, phones, to_json(addresses), to_json(sources),
                   (bbox.ymin + bbox.ymax) / 2 AS lat, (bbox.xmin + bbox.xmax) / 2 AS lon
            FROM read_parquet('{source}', hive_partitioning=1)
            WHERE bbox.xmin BETWEEN {lon_min} AND {lon_max} AND bbox.ymin BETWEEN {lat_min} AND {lat_max}
              AND names.primary IS NOT NULL
              AND (taxonomy.primary IN ({categories}) OR ({names}))
              AND NOT regexp_matches(lower(names.primary), '{EXCLUDE}')
              AND coalesce(operating_status, 'open') <> 'permanently_closed'
              AND coalesce(confidence, 0) >= {MIN_CONFIDENCE}
        """).fetchall()
        for (place_id, name, category, hierarchy, confidence, status, websites, phones, addresses, sources,
             lat, lon) in rows:
            if place_id in seen or distance_km(region["CenterLatitude"], region["CenterLongitude"], lat, lon) > region["RadiusKm"]:
                continue
            seen.add(place_id)
            sources = [s for s in json.loads(sources) if s["dataset"] != "Overture"]
            address = (json.loads(addresses) or [{}])[0] or {}
            items.append({
                "id": place_id,
                "name": name.strip(),
                "category": category,
                "hierarchy": hierarchy or [],
                "suggestedCategory": CATEGORY_MAP.get(category, "active-entertainment"),
                "confidence": round(confidence or 0, 2),
                "operatingStatus": status,
                "latitude": round(lat, 6),
                "longitude": round(lon, 6),
                "address": address.get("freeform"),
                "city": address.get("locality"),
                "postcode": address.get("postcode"),
                "website": (websites or [None])[0],
                "phone": (phones or [None])[0],
                "region": region["Code"],
                "sources": [{"dataset": s["dataset"], "license": s.get("license"), "recordId": s.get("record_id")}
                            for s in sources],
                "attribution": " ".join(dict.fromkeys(attribution_for(s["dataset"]) for s in sources)),
            })
        print(f"{region['Code']}: {sum(1 for i in items if i['region'] == region['Code'])} kandydatów")

    items.sort(key=lambda i: (i["suggestedCategory"], i["category"] or "", i["name"]))
    target = OUT_DIR / release
    target.mkdir(parents=True, exist_ok=True)
    report = {
        "release": release,
        "fetchedAtUtc": datetime.now(timezone.utc).isoformat(),
        "source": "Overture Maps Foundation – Places",
        "licenseInfo": "https://docs.overturemaps.org/attribution/",
        "minConfidence": MIN_CONFIDENCE,
        "count": len(items),
        "items": items,
    }
    (target / "candidates.json").write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"Zapisano {len(items)} kandydatów: {(target / 'candidates.json').relative_to(ROOT)}")


if __name__ == "__main__":
    main()
