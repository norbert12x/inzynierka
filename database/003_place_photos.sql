-- Automatycznie znalezione zdjęcia pozostają propozycjami do przeglądu.
begin;
create table if not exists catalog.place_photos (
    id uuid primary key,
    place_id uuid not null references catalog.places(id) on delete cascade,
    provider text not null check (provider = 'wikimedia_commons'),
    source_file text not null,
    source_page_url text not null,
    original_url text not null,
    thumbnail_url text not null,
    cached_relative_path text not null,
    author text not null,
    credit text not null,
    license text not null,
    license_url text not null,
    match_method text not null,
    metadata jsonb not null,
    fetched_at_utc timestamptz not null,
    status text not null default 'pending_review' check (status in ('pending_review','approved','rejected')),
    unique(place_id, provider, source_file)
);
create index if not exists ix_place_photos_place_status on catalog.place_photos(place_id,status);
create table if not exists catalog.photo_fetch_attempts (
    place_id uuid primary key references catalog.places(id) on delete cascade,
    attempted_at_utc timestamptz not null,
    outcome text not null check (outcome in ('pending_review','no_candidate','error')),
    detail text not null
);
alter table catalog.place_photos enable row level security;
alter table catalog.photo_fetch_attempts enable row level security;
revoke all on catalog.place_photos, catalog.photo_fetch_attempts from public, anon, authenticated;
commit;
