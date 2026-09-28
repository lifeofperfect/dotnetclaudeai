namespace TodoApi.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity tables

        modelBuilder.Entity<TodoItem>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(TodoRules.TitleMaxLength).IsRequired();
            e.Property(t => t.Description).HasMaxLength(TodoRules.DescriptionMaxLength);
            e.Property(t => t.RowVersion).IsConcurrencyToken();

            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.OwnerId, t.CreatedAt });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // New version on every insert/update. The *original* value stays in the UPDATE's WHERE clause,
    // so a save against a stale version throws DbUpdateConcurrencyException.
    private void StampRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries<TodoItem>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RowVersion = Guid.NewGuid();
        }
    }
}
