begin;
alter table catalog.place_photos drop constraint if exists place_photos_provider_check;
alter table catalog.place_photos add constraint place_photos_provider_check
    check(provider in ('wikimedia_commons','user_provided'));
commit;
