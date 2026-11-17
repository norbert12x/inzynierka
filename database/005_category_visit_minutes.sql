-- Zadanie 1.2: domyślny czas wizyty dla kategorii (w minutach).
-- Używany, gdy atrakcja nie ma własnego estimated_visit_minutes; przy kilku kategoriach planer bierze najdłuższy.
-- Wartości to szacunki do edycji (później w panelu administratora). Skrypt można wykonać ponownie:
-- nie nadpisuje już ustawionych wartości.
begin;

alter table catalog.categories add column if not exists default_visit_minutes integer;

update catalog.categories c
set default_visit_minutes = v.minutes
from (values
    ('museums', 90),
    ('heritage', 30),
    ('religious-sites', 20),
    ('nature', 60),
    ('viewpoints', 20),
    ('recreation', 120),
    ('culture', 60),
    ('public-art', 10),
    ('other', 30)
) as v(code, minutes)
where c.code = v.code and c.default_visit_minutes is null;

-- Kategorie spoza listy powyżej dostają 30 minut, żeby kolumna mogła być wymagana.
update catalog.categories set default_visit_minutes = 30 where default_visit_minutes is null;

alter table catalog.categories alter column default_visit_minutes set not null;
alter table catalog.categories drop constraint if exists categories_default_visit_minutes_check;
alter table catalog.categories add constraint categories_default_visit_minutes_check
    check (default_visit_minutes between 5 and 600);

commit;

-- Sprawdzenie: 9 wierszy, każdy z wartością.
select code, name, default_visit_minutes from catalog.categories order by code;
