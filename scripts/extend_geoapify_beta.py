"""Dobiera drugą partię z zapisanych odpowiedzi, bez wywołań API i klucza."""
import json
import math
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/geoapify/20261004T095555262149Z"
OUTPUT = SOURCE / "batch-02"


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def osm_key(place):
    raw = (place.get("sourceDetails") or {}).get("raw") or {}
    return (raw["osm_type"], str(raw["osm_id"])) if raw.get("osm_type") and raw.get("osm_id") else None


def near_same_name(a, b):
    def name(p):
        return re.sub(r"[\W_]+", "", p["name"].casefold())
    if name(a) != name(b):
        return False
    lat1, lat2 = math.radians(a["latitude"]), math.radians(b["latitude"])
    delta_lon = math.radians(a["longitude"] - b["longitude"])
    h = math.sin((lat1-lat2)/2)**2 + math.cos(lat1)*math.cos(lat2)*math.sin(delta_lon/2)**2
    return 6371000 * 2 * math.asin(min(1, math.sqrt(h))) < 150


def main():
    initial = read(SOURCE / "beta-places.json")
    original = initial["places"]
    candidates = {p["id"]: p for p in read(SOURCE / "candidates.json")}
    used_ids = {p["id"] for p in original}
    used_osm = {osm_key(p) for p in original} - {None}
    groups = {}
    for request in initial["requests"]:
        group = request["file"].split("-")[2]
        bucket = groups.setdefault(group, set())
        for feature in read(SOURCE / request["file"])["features"]:
            pid = feature["properties"].get("place_id")
            if pid in candidates and pid not in used_ids:
                bucket.add(pid)
    groups = {k: sorted(v, key=lambda pid: (candidates[pid]["distanceFromCenterKm"], pid)) for k,v in groups.items()}
    added, excluded, examined = [], [], set()
    while len(added) < 300 and any(groups.values()):
        for group, bucket in groups.items():
            while bucket and bucket[0] in examined:
                bucket.pop(0)
            if not bucket or len(added) == 300:
                continue
            pid = bucket.pop(0)
            examined.add(pid)
            place = candidates[pid]
            source_id = osm_key(place)
            if source_id is not None and source_id in used_osm:
                excluded.append({"id": pid, "name": place["name"], "reason": "same-osm-type-and-id"})
                continue
            match = next((p for p in original + added if near_same_name(place, p)), None)
            if match:
                excluded.append({"id": pid, "name": place["name"], "reason": "same-normalized-name-within-150m", "matchingId": match["id"]})
                continue
            added.append({**place, "selectionGroup": group})
            used_ids.add(pid)
            if source_id:
                used_osm.add(source_id)

    if len(added) != 300:
        raise RuntimeError(f"Po filtrowaniu dostępnych jest tylko {len(added)} nowych miejsc; potrzebne dalsze pobranie.")
    assert len({p['id'] for p in original + added}) == 600
    assert not ({p['id'] for p in original} & {p['id'] for p in added})
    assert not any(near_same_name(a,b) for i,a in enumerate(added) for b in original + added[:i])
    assert all(p['name'].strip() and math.isfinite(p['latitude']) and math.isfinite(p['longitude']) for p in added)
    metadata = {k: initial[k] for k in ("fetchedAtUtc", "center", "maxRadiusMeters", "attribution", "providerUrl", "licenseUrl", "limitations")}
    metadata.update({"selectedAtUtc": datetime.now(timezone.utc).isoformat(), "sourceDataset": "../candidates.json", "newApiRequests": 0})
    report = {
        "newCount": 300, "combinedCount": 600, "duplicateGeoapifyIds": 0,
        "selectionGroups": dict(Counter(p["selectionGroup"] for p in added)),
        "withOpeningHours": sum(bool(p["openingHours"]) for p in added),
        "withWebsite": sum(bool(p["website"]) for p in added),
        "cities": dict(Counter(p["city"] for p in added)),
        "excludedCandidates": excluded,
        "deduplication": "Wykluczono identyfikatory z pierwszej partii, wspólne pary osm_type/osm_id i takie same znormalizowane nazwy w promieniu 150 m. Kontrole również wewnątrz nowej partii.",
        "limitation": "Różne nazwy i identyfikatory nadal mogą opisywać to samo miejsce. Pierwsza partia pozostaje bez zmian, wraz z wcześniej zgłoszonymi kandydatami do sprawdzenia.",
    }
    OUTPUT.mkdir(exist_ok=False)
    write(OUTPUT / "beta-places-next-300.json", {**metadata, "count": 300, "places": added})
    write(OUTPUT / "beta-places-600.json", {**metadata, "sourceDataset": "../beta-places.json + ../candidates.json", "count": 600, "places": original + added})
    write(OUTPUT / "quality-report.json", report)
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
