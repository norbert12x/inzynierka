"""Wykonuje schema SQL i import, bez builda i uruchamiania aplikacji .NET.

python scripts/apply_catalog_database.py --check-only
python scripts/apply_catalog_database.py --apply
"""
import argparse
import json
import pathlib
import shutil
import sys
import tempfile
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".local/python-packages"))
import psycopg


def execute_script(connection, path):
    with connection.cursor() as cursor:
        cursor.execute(path.read_text(encoding="utf-8"), prepare=False)
        results = []
        while True:
            if cursor.description:
                results.append({"columns": [c.name for c in cursor.description], "rows": cursor.fetchall()})
            if not cursor.nextset():
                break
    return results


def main():
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check-only", action="store_true")
    mode.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    cfg = json.loads((ROOT / ".local/supabase.json").read_text(encoding="utf-8"))["Database"]
    # libpq na Windows nie czyta poprawnie ścieżki certyfikatu z literą ż.
    with tempfile.TemporaryDirectory(prefix="catalog-db-") as temp:
        cert = pathlib.Path(temp) / "supabase-ca.crt"
        shutil.copyfile(ROOT / ".local/supabase-ca.crt", cert)
        try:
            with psycopg.connect(host=cfg["Host"], port=cfg["Port"], dbname=cfg["Name"],
                user=cfg["Username"], password=cfg["Password"], sslmode="verify-full",
                sslrootcert=str(cert), connect_timeout=15, autocommit=True) as connection:
                tables = [row[0] for row in connection.execute(
                    "select tablename from pg_tables where schemaname='catalog' order by tablename")]
                print("Połączenie z Supabase: OK. Tabele catalog:", tables)
                if args.check_only:
                    return 0
                expected = {"categories", "places", "place_categories", "place_sources",
                            "place_photos", "photo_fetch_attempts"}
                if set(tables) - expected:
                    raise ValueError("Schema catalog zawiera inne tabele; import przerwany do przeglądu.")
                schema = execute_script(connection, ROOT / "database/001_catalog_schema.sql")
                execute_script(connection, ROOT / "database/003_place_photos.sql")
                execute_script(connection, ROOT / "database/004_user_provided_photos.sql")
                first_import = execute_script(connection, ROOT / "database/002_import_beta_catalog.sql")
                before = connection.execute("""select md5(string_agg(row_to_json(p)::text, '' order by id))
                    from catalog.places p""").fetchone()[0]
                second_import = execute_script(connection, ROOT / "database/002_import_beta_catalog.sql")
                after = connection.execute("""select md5(string_agg(row_to_json(p)::text, '' order by id))
                    from catalog.places p""").fetchone()[0]
                assert before == after, "Ponowny import zmienił dane katalogu."
                second_added = next(result['rows'][0][0] for result in second_import
                    if result['columns'] == ['newly_inserted_places'])
                assert second_added == 0, "Ponowny import dodał nowe rekordy."
                statuses = dict(connection.execute("select status, count(*) from catalog.places group by status").fetchall())
                counts = dict(connection.execute("""select 'places', count(*) from catalog.places
                    union all select 'categories', count(*) from catalog.categories
                    union all select 'sources', count(*) from catalog.place_sources""").fetchall())
                parents = connection.execute("select count(*) from catalog.places where parent_id is not null").fetchone()[0]
                confirmed = connection.execute("select count(*) from catalog.places where opening_hours_verification='confirmed_by_user'").fetchone()[0]
                invalid = connection.execute("""select count(*) from catalog.places where
                    extensions.st_srid(location::extensions.geometry) <> 4326
                    or extensions.st_x(location::extensions.geometry) not between -180 and 180
                    or extensions.st_y(location::extensions.geometry) not between -90 and 90""").fetchone()[0]
                assert counts['places'] >= 600 and counts['sources'] >= 600
                assert parents >= 7 and confirmed >= 1 and invalid == 0
                report = {"verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
                    "databaseApplied": True, "counts": counts, "statuses": statuses,
                    "linkedComponents": parents, "confirmedOpeningHours": confirmed,
                    "invalidCoordinates": invalid, "repeatImportAddedPlaces": 0,
                    "repeatImportPreservedCatalog": True, "firstImport": first_import}
                (ROOT / "database/database-verification.json").write_text(
                    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
                print(json.dumps(report, ensure_ascii=False, indent=2))
                return 0
        except Exception as error:
            # Nigdy nie wypisujemy wartości konfiguracji ani hasła.
            message = str(error).replace(cfg.get("Password", ""), "[REDACTED]") if cfg.get("Password") else str(error)
            print("Błąd bazy:", type(error).__name__, message)
            return 1


if __name__ == "__main__":
    raise SystemExit(main())
