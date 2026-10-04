-- Etap 1: schemat katalogu. Wykonaj w SQL Editor Supabase lub przez psycopg.
-- Tworzy wyłącznie schema catalog i PostGIS w extensions; nie usuwa danych.
begin;

create schema if not exists extensions;
create extension if not exists postgis with schema extensions;
-- Jeśli PostGIS jest już zainstalowany w innym schemacie, zatrzymaj się przed DDL.
do $$
begin
    if (select n.nspname from pg_extension e join pg_namespace n on n.oid=e.extnamespace
        where e.extname='postgis') <> 'extensions' then
        raise exception 'PostGIS jest w innym schemacie. Dostosuj mapowanie przed wykonaniem migracji.';
    end if;
end $$;

create schema if not exists catalog;

create table if not exists catalog.categories (
    code text primary key,
    name text not null check (length(trim(name)) > 0)
);

create table if not exists catalog.places (
    id uuid primary key,
    name text not null check (length(trim(name)) > 0),
    description text,
    city text,
    address text,
    location extensions.geography(point, 4326) not null,
    status text not null check (status in ('active', 'awaiting_photo', 'removed', 'component')),
    kind text not null check (kind in ('attraction', 'component')),
    parent_id uuid references catalog.places(id) on delete restrict,
    opening_hours text,
    opening_hours_verification text not null default 'unverified'
        check (opening_hours_verification in ('unverified', 'confirmed_by_user')),
    opening_hours_confirmed_on date,
    website text,
    estimated_visit_minutes integer check (estimated_visit_minutes > 0),
    manually_edited_fields text[] not null default '{}',
    constraint place_not_own_parent check (parent_id is null or parent_id <> id),
    constraint component_status_matches_kind check ((status = 'component') = (kind = 'component')),
    constraint parent_only_for_component check (parent_id is null or kind = 'component')
);

create index if not exists ix_places_location on catalog.places using gist(location);
create index if not exists ix_places_parent_id on catalog.places(parent_id);
create index if not exists ix_places_status on catalog.places(status);

create table if not exists catalog.place_categories (
    place_id uuid not null references catalog.places(id) on delete cascade,
    category_code text not null references catalog.categories(code) on delete cascade,
    primary key (place_id, category_code)
);
create index if not exists ix_place_categories_category_code on catalog.place_categories(category_code);

create table if not exists catalog.place_sources (
    id uuid primary key,
    place_id uuid not null references catalog.places(id) on delete cascade,
    provider text not null,
    external_id text not null,
    fetched_at_utc timestamptz not null,
    raw_data jsonb not null,
    unique (provider, external_id)
);
create index if not exists ix_place_sources_place_id on catalog.place_sources(place_id);

insert into catalog.categories (code, name) values
    ('museums', 'Muzea i galerie'),
    ('heritage', 'Zabytki i historia'),
    ('religious-sites', 'Obiekty sakralne'),
    ('nature', 'Przyroda'),
    ('viewpoints', 'Punkty widokowe'),
    ('recreation', 'Rozrywka i rekreacja'),
    ('culture', 'Kultura'),
    ('public-art', 'Sztuka w przestrzeni publicznej'),
    ('other', 'Pozostałe atrakcje')
on conflict (code) do nothing;

-- Katalog obsługuje backend przez PostgreSQL. Brak publicznych polityk zapisu.
alter table catalog.categories enable row level security;
alter table catalog.places enable row level security;
alter table catalog.place_categories enable row level security;
alter table catalog.place_sources enable row level security;
revoke all on schema catalog from public, anon, authenticated;
revoke all on all tables in schema catalog from public, anon, authenticated;

commit;
