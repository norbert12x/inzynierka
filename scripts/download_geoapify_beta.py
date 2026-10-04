"""Jednorazowe pobranie katalogu beta; nie uruchamia aplikacji .NET.

Klucz: ../.local/geoapify-key.txt. Uruchom: python download_geoapify_beta.py
Biblioteka standardowa Python, bez dodatkowych pakietów.
"""
import json
import math
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KEY_FILE = ROOT / ".local" / "geoapify-key.txt"
CENTER_LAT, CENTER_LON = 50.037, 22.004
TARGET = 300
# Oddzielne grupy pozwalają dobrać różnorodny zestaw zamiast samych pomników.
GROUPS = {
    "museums": "entertainment.museum,entertainment.culture.gallery",
    "heritage": "tourism.sights",
    "nature": "leisure.park,natural.mountain.peak,natural.mountain.cave_entrance",
    "viewpoints": "tourism.attraction.viewpoint",
    "recreation": "entertainment.theme_park,entertainment.zoo,entertainment.water_park,entertainment.activity_park,entertainment.planetarium",
    "attractions": "tourism.attraction",
}


def distance_km(lat, lon):
    lat1, lat2 = math.radians(CENTER_LAT), math.radians(lat)
    dlat, dlon = lat2 - lat1, math.radians(lon - CENTER_LON)
    a = math.sin(dlat / 2) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin(dlon / 2) ** 2
    return 6371 * 2 * math.asin(min(1, math.sqrt(a)))


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def normalize(feature):
    p = feature.get("properties") or {}
    name, place_id = p.get("name"), p.get("place_id")
    lat, lon = p.get("lat"), p.get("lon")
    if not name or not place_id or not isinstance(lat, (float, int)) or not isinstance(lon, (float, int)):
        return None
    if not math.isfinite(lat) or not math.isfinite(lon) or not -90 <= lat <= 90 or not -180 <= lon <= 180:
        return None
    raw = (p.get("datasource") or {}).get("raw") or {}
    return {
        "id": place_id, "name": name, "latitude": lat, "longitude": lon,
        "distanceFromCenterKm": round(distance_km(lat, lon), 3),
        "sourceCategories": p.get("categories", []),
        "address": p.get("formatted"), "city": p.get("city") or p.get("town") or p.get("village"),
        "openingHours": p.get("opening_hours") or raw.get("opening_hours"),
        "website": p.get("website") or raw.get("website") or raw.get("contact:website"),
        "source": "Geoapify", "sourceDetails": p.get("datasource"),
        "estimatedVisitMinutes": None, "reviewStatus": "unverified",
    }


def main():
    key = KEY_FILE.read_text(encoding="utf-8-sig").strip() if KEY_FILE.exists() else ""
    if not key:
        print("Brak klucza. Zapisz sam klucz w .local/geoapify-key.txt. Nie pobrano danych.")
        return 1

    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output = ROOT / "data" / "geoapify" / stamp
    output.mkdir(parents=True, exist_ok=False)
    places, buckets, requests = {}, {group: [] for group in GROUPS}, []
    skipped = 0
    try:
        for radius in (30000, 50000):
            for group, categories in GROUPS.items():
                for offset in (0, 100, 200):
                    params = {
                        "categories": categories,
                        "filter": f"circle:{CENTER_LON},{CENTER_LAT},{radius}",
                        "bias": f"proximity:{CENTER_LON},{CENTER_LAT}",
                        "limit": 100, "offset": offset, "lang": "pl",
                    }
                    url = "https://api.geoapify.com/v2/places?" + urllib.parse.urlencode({**params, "apiKey": key})
                    request = urllib.request.Request(url, headers={"User-Agent": "PodkarpacieThesis/0.1"})
                    # Nie logujemy URL ani wyjątków zawierających klucz API.
                    with urllib.request.urlopen(request, timeout=60) as response:
                        payload = json.load(response)
                    if payload.get("type") != "FeatureCollection" or not isinstance(payload.get("features"), list):
                        raise ValueError("Niepoprawny format odpowiedzi Geoapify.")
                    filename = f"raw-{radius}-{group}-{offset}.geojson"
                    save_json(output / filename, payload)
                    requests.append({**params, "file": filename, "returnedCount": len(payload["features"])})
                    for feature in payload["features"]:
                        item = normalize(feature)
                        if item is None:
                            skipped += 1
                            continue
                        if item["distanceFromCenterKm"] > radius / 1000 + 0.1:
                            skipped += 1
                            continue
                        places[item["id"]] = item
                        if item["id"] not in buckets[group]:
                            buckets[group].append(item["id"])
                    print(f"Promien {radius // 1000} km, grupa {group}, strona {offset // 100 + 1}: {len(payload['features'])} rekordow.")
                    time.sleep(0.3)
                    if len(payload["features"]) < 100:
                        break
            if len(places) >= TARGET:
                break

        # Dobór na zmianę z grup; w każdej najbliższe miejsca jako pierwsze.
        for group in buckets:
            buckets[group].sort(key=lambda pid: (places[pid]["distanceFromCenterKm"], pid))
        selected = {}
        while len(selected) < TARGET and any(buckets.values()):
            for group, ids in buckets.items():
                while ids and ids[0] in selected:
                    ids.pop(0)
                if ids and len(selected) < TARGET:
                    pid = ids.pop(0)
                    selected[pid] = {**places[pid], "selectionGroup": group}

        metadata = {
            "fetchedAtUtc": datetime.now(timezone.utc).isoformat(), "targetCount": TARGET,
            "count": len(selected), "uniqueCandidateCount": len(places), "skippedOccurrences": skipped,
            "center": {"latitude": CENTER_LAT, "longitude": CENTER_LON}, "maxRadiusMeters": radius,
            "attribution": "Powered by Geoapify | © OpenStreetMap contributors",
            "providerUrl": "https://www.geoapify.com/", "licenseUrl": "https://www.openstreetmap.org/copyright",
            "requests": requests,
            "limitations": ["Dane nieweryfikowane; identyfikatory usuwają tylko powtórzenia tego samego wpisu.",
                "Różne wpisy mogą dotyczyć tego samego miejsca. Godziny i czasy wizyt wymagają przeglądu.",
                "To zróżnicowana próbka do bety, nie kompletny katalog ani ranking atrakcji.",
                "Brak macierzy czasów przejazdów. Odległość od centrum to odległość w linii prostej."],
        }
        save_json(output / "beta-places.json", {**metadata, "places": list(selected.values())})
        save_json(output / "candidates.json", list(places.values()))
        print(f"Zapisano {len(selected)} miejsc w {output / 'beta-places.json'}")
        if len(selected) < TARGET:
            print("Znaleziono mniej niż 300 poprawnych rekordów. Nie dodawano fikcyjnych miejsc.")
        return 0
    except (urllib.error.URLError, TimeoutError, ValueError, OSError) as error:
        status = error.code if isinstance(error, urllib.error.HTTPError) else None
        save_json(output / "incomplete.json", {"status": "incomplete", "httpStatus": status, "requests": requests})
        print(f"Pobieranie przerwane. HTTP: {status or 'brak'}. Zachowano pobrane pliki; nie utworzono katalogu beta.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
