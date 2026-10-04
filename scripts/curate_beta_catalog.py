"""Stosuje decyzje użytkownika do kopii katalogu. Bez API i uruchamiania .NET."""
import copy
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/geoapify/20261004T095555262149Z/batch-02/beta-places-600.json"
OUTPUT = ROOT / "data/curated-beta-v1"


def main():
    document = json.loads(SOURCE.read_text(encoding="utf-8"))
    originals = document["places"]
    assert len(originals) == 600
    review = json.loads((SOURCE.parent / "review-decisions.json").read_text(encoding="utf-8"))
    # Sprawdzamy numery względem trwałych identyfikatorów z wcześniejszego przeglądu.
    for issue in review["items"]:
        for reference in issue["records"]:
            assert originals[reference["row"] - 1]["id"] == reference["id"]
    records = {i: copy.deepcopy(p) for i, p in enumerate(originals, 1)}
    removed_rows = {222, 284, 4, 241, 6, 12, 24, 30, 36, 42, 52, 104, 152,
                    197, 221, 260, 224, 227, 230, 118, 126, 162, 594}
    parents = {294: 297, 300: 297, 8: 20, 86: 102, 216: 219, 170: 92, 285: 75}
    changes = []

    def edit(row, field, value, reason):
        p = records[row]
        changes.append({"id": p["id"], "sourceRow": row, "field": field,
                        "before": p.get(field), "after": value, "reason": reason})
        p[field] = value

    for row, p in records.items():
        p["sourceRow"] = row
        p["catalogStatus"] = "active"
        p["components"] = []

    edit(177, "name", "Teatr Narodowy w Rzeszowie", "R01: nazwa i charakter miejsca wskazane przez użytkownika.")
    edit(177, "categoryCodes", ["culture"], "R01: ręczna kategoria; źródłowy tag tablicy zachowany w sourceDetails.")
    edit(177, "recordType", "theatre", "R01: korekta użytkownika, bez niezależnej weryfikacji.")
    edit(506, "categoryCodes", ["nature", "culture"], "R06: park i ośrodek kultury.")
    edit(81, "categoryCodes", ["museums", "religious-sites"], "R17: muzeum i obiekt sakralny.")
    edit(148, "openingHoursVerification", "confirmed_by_user", "R18: użytkownik potwierdził godziny zwiedzania pon.–pt. 07:30–15:30, 2026-10-04.")
    edit(148, "openingHoursConfirmation", {"confirmedOn": "2026-10-04", "source": "user", "scope": "visiting_hours"}, "R18: potwierdzenie użytkownika.")

    removed, pending, standalone = [], [], []
    for row, p in records.items():
        if row in removed_rows:
            p["catalogStatus"] = "removed"
            removed.append(p)
            changes.append({"id": p["id"], "sourceRow": row, "action": "remove-from-working-catalog", "reason": "Decyzja użytkownika."})
            continue
        raw = (p.get("sourceDetails") or {}).get("raw") or {}
        is_component = row in parents or (raw.get("memorial") == "plaque" and row != 177)
        if is_component:
            p["catalogStatus"] = "component"
            p["standaloneStopAllowed"] = False
            parent_row = parents.get(row)
            p["parentId"] = records[parent_row]["id"] if parent_row else None
            p["parentAssignmentStatus"] = "assigned" if parent_row else "pending"
            if parent_row:
                records[parent_row]["components"].append(p)
            else:
                pending.append(p)
            changes.append({"id": p["id"], "sourceRow": row, "action": "make-component", "parentId": p["parentId"], "reason": "Tablice jako elementy obiektów, nie osobne przystanki; R11–R16."})
            continue
        if "tourism.attraction.artwork.mural" in p["sourceCategories"]:
            p["catalogStatus"] = "awaiting_photo"
            p["automaticPlanningAllowed"] = False
            p["photoStatus"] = "required"
            changes.append({"id": p["id"], "sourceRow": row, "action": "await-photo", "reason": "R07/R08: wszystkie murale pozostają warunkowo, wymagane zdjęcie."})
        standalone.append(p)

    linked = [c for p in standalone for c in p["components"]]
    all_records = standalone + linked + pending + removed
    assert len(all_records) == 600 and len({p['id'] for p in all_records}) == 600
    assert not ({p['id'] for p in removed} & {p['id'] for p in standalone})
    assert all(p['id'] != c['id'] for p in standalone for c in p['components'])
    assert not any((p.get('sourceDetails') or {}).get('raw', {}).get('memorial') == 'plaque' for p in standalone if p['sourceRow'] != 177)
    summary = {"sourceCount": 600, "standaloneCount": len(standalone),
               "activeCount": sum(p['catalogStatus'] == 'active' for p in standalone),
               "awaitingPhotoCount": sum(p['catalogStatus'] == 'awaiting_photo' for p in standalone),
               "removedCount": len(removed), "linkedComponentCount": len(linked),
               "pendingComponentCount": len(pending)}
    OUTPUT.mkdir(exist_ok=False)
    def save(name, value):
        (OUTPUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    metadata = {k: document[k] for k in ('fetchedAtUtc', 'attribution', 'providerUrl', 'licenseUrl', 'center', 'maxRadiusMeters')}
    save('catalog.json', {**metadata, "curatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "count": len(standalone), "categoryOverrides": {"culture": "Kultura"},
        "note": "Ręczne korekty oddzielone od tagów dostawcy. active nie oznacza zweryfikowanych godzin ani gotowości planera. Brak integracji tego pliku z API.", "places": standalone})
    save('removed.json', removed)
    save('pending-components.json', pending)
    save('changes.json', {"sourceFile": str(SOURCE.relative_to(ROOT)), "sourceSha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(), "summary": summary, "changes": changes,
        "pendingDecisions": ["Przypisanie tablic bez rozpoznanego obiektu nadrzędnego."],
        "unansweredGroups": ["G1 (poza zasadą tablic)", "G2", "G3", "G4 (poza muralami)", "G5"]})
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    main()
