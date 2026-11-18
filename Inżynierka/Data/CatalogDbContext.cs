using Inżynierka.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CatalogPlace> Places => Set<CatalogPlace>();
    public DbSet<CatalogCategory> Categories => Set<CatalogCategory>();
    public DbSet<PlaceSource> Sources => Set<PlaceSource>();
    public DbSet<PlacePhoto> Photos => Set<PlacePhoto>();
    // Schemat routing, ale w tym kontekście – klucze obce do catalog.places.
    public DbSet<TravelTime> TravelTimes => Set<TravelTime>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.HasPostgresExtension("extensions", "postgis");

        var place = modelBuilder.Entity<CatalogPlace>();
        place.ToTable("places");
        place.HasKey(p => p.Id);
        place.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        place.Property(p => p.Name).HasColumnName("name").IsRequired();
        place.Property(p => p.Description).HasColumnName("description");
        place.Property(p => p.City).HasColumnName("city");
        place.Property(p => p.Address).HasColumnName("address");
        place.Property(p => p.Location).HasColumnName("location").HasColumnType("geography (point, 4326)").IsRequired();
        place.Property(p => p.Status).HasColumnName("status").IsRequired();
        place.Property(p => p.Kind).HasColumnName("kind").IsRequired();
        place.Property(p => p.ParentId).HasColumnName("parent_id");
        place.Property(p => p.OpeningHours).HasColumnName("opening_hours");
        place.Property(p => p.OpeningHoursVerification).HasColumnName("opening_hours_verification").IsRequired();
        place.Property(p => p.OpeningHoursConfirmedOn).HasColumnName("opening_hours_confirmed_on");
        place.Property(p => p.Website).HasColumnName("website");
        place.Property(p => p.EstimatedVisitMinutes).HasColumnName("estimated_visit_minutes");
        place.Property(p => p.ManuallyEditedFields).HasColumnName("manually_edited_fields").HasColumnType("text[]").IsRequired();
        place.HasOne(p => p.Parent).WithMany(p => p.Components).HasForeignKey(p => p.ParentId).OnDelete(DeleteBehavior.Restrict);
        place.HasIndex(p => p.Location).HasMethod("gist");

        var category = modelBuilder.Entity<CatalogCategory>();
        category.ToTable("categories");
        category.HasKey(c => c.Code);
        category.Property(c => c.Code).HasColumnName("code");
        category.Property(c => c.Name).HasColumnName("name").IsRequired();
        category.Property(c => c.DefaultVisitMinutes).HasColumnName("default_visit_minutes");

        var assignment = modelBuilder.Entity<PlaceCategory>();
        assignment.ToTable("place_categories");
        assignment.HasKey(c => new { c.PlaceId, c.CategoryCode });
        assignment.Property(c => c.PlaceId).HasColumnName("place_id");
        assignment.Property(c => c.CategoryCode).HasColumnName("category_code");
        assignment.HasOne(c => c.Place).WithMany(p => p.Categories).HasForeignKey(c => c.PlaceId);
        assignment.HasOne(c => c.Category).WithMany(c => c.Places).HasForeignKey(c => c.CategoryCode);

        var source = modelBuilder.Entity<PlaceSource>();
        source.ToTable("place_sources");
        source.HasKey(s => s.Id);
        source.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        source.Property(s => s.PlaceId).HasColumnName("place_id");
        source.Property(s => s.Provider).HasColumnName("provider").IsRequired();
        source.Property(s => s.ExternalId).HasColumnName("external_id").IsRequired();
        source.Property(s => s.FetchedAtUtc).HasColumnName("fetched_at_utc");
        source.Property(s => s.RawData).HasColumnName("raw_data").HasColumnType("jsonb").IsRequired();
        source.HasIndex(s => new { s.Provider, s.ExternalId }).IsUnique();
        source.HasOne(s => s.Place).WithMany(p => p.Sources).HasForeignKey(s => s.PlaceId);

        var photo = modelBuilder.Entity<PlacePhoto>();
        photo.ToTable("place_photos");
        photo.HasKey(p => p.Id);
        photo.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        photo.Property(p => p.PlaceId).HasColumnName("place_id");
        photo.Property(p => p.Provider).HasColumnName("provider");
        photo.Property(p => p.SourceFile).HasColumnName("source_file");
        photo.Property(p => p.SourcePageUrl).HasColumnName("source_page_url");
        photo.Property(p => p.OriginalUrl).HasColumnName("original_url");
        photo.Property(p => p.ThumbnailUrl).HasColumnName("thumbnail_url");
        photo.Property(p => p.CachedRelativePath).HasColumnName("cached_relative_path");
        photo.Property(p => p.Author).HasColumnName("author");
        photo.Property(p => p.Credit).HasColumnName("credit");
        photo.Property(p => p.License).HasColumnName("license");
        photo.Property(p => p.LicenseUrl).HasColumnName("license_url");
        photo.Property(p => p.MatchMethod).HasColumnName("match_method");
        photo.Property(p => p.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        photo.Property(p => p.FetchedAtUtc).HasColumnName("fetched_at_utc");
        photo.Property(p => p.Status).HasColumnName("status");
        // Istniejące zdjęcia dostają "place" (wartość domyślna kolumny).
        photo.Property(p => p.Kind).HasColumnName("kind").IsRequired().HasDefaultValue(PhotoKinds.Place);
        photo.Property(p => p.Caption).HasColumnName("caption");
        photo.Property(p => p.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        photo.ToTable(t => t.HasCheckConstraint("place_photos_kind_check", "kind in ('place', 'exhibit')"));
        photo.HasIndex(p => new { p.PlaceId, p.Provider, p.SourceFile }).IsUnique();
        photo.HasOne(p => p.Place).WithMany(p => p.Photos).HasForeignKey(p => p.PlaceId);

        var travel = modelBuilder.Entity<TravelTime>();
        travel.ToTable("travel_times", "routing", t =>
        {
            t.HasCheckConstraint("travel_times_profile_check", "profile in ('driving-car', 'foot-walking')");
            t.HasCheckConstraint("travel_times_not_same_place_check", "from_place_id <> to_place_id");
        });
        travel.HasKey(t => new { t.FromPlaceId, t.ToPlaceId, t.Profile });
        travel.Property(t => t.FromPlaceId).HasColumnName("from_place_id");
        travel.Property(t => t.ToPlaceId).HasColumnName("to_place_id");
        travel.Property(t => t.Profile).HasColumnName("profile");
        travel.Property(t => t.DurationSeconds).HasColumnName("duration_seconds");
        travel.Property(t => t.DistanceMeters).HasColumnName("distance_meters");
        travel.Property(t => t.Provider).HasColumnName("provider").IsRequired();
        travel.Property(t => t.FetchedAt).HasColumnName("fetched_at");
        travel.HasIndex(t => t.ToPlaceId);
        // Usunięcie atrakcji z bazy usuwa jej czasy; zmiana statusu (np. removed) ich nie rusza.
        travel.HasOne<CatalogPlace>().WithMany().HasForeignKey(t => t.FromPlaceId).OnDelete(DeleteBehavior.Cascade);
        travel.HasOne<CatalogPlace>().WithMany().HasForeignKey(t => t.ToPlaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
