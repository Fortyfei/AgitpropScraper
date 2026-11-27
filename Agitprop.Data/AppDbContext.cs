using Agitprop.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace Agitprop.Data;

public class AppDbContext : DbContext
{
  public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

  public DbSet<PostgresArticle> Articles => Set<PostgresArticle>();
  public DbSet<PostgresEntity> Entities => Set<PostgresEntity>();
  public DbSet<PostgresEntityAlias> EntityAliases => Set<PostgresEntityAlias>();
  public DbSet<PostgresMention> Mentions => Set<PostgresMention>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<PostgresArticle>(entity =>
    {
      entity.ToTable("articles");
      entity.HasKey(a => a.Id);
      entity.HasIndex(a => a.Url).IsUnique();
    });

    modelBuilder.Entity<PostgresEntity>(entity =>
    {
      entity.ToTable("entities");
      entity.HasKey(e => e.Id);
      entity.Property(e => e.Name).IsRequired();
      entity.Property(e => e.NormalizedName).IsRequired();
      entity.Property(e => e.Type).IsRequired();
      entity.Property(e => e.Source).IsRequired();
      entity.HasIndex(e => new { e.NormalizedName, e.Type }).IsUnique();
      entity.HasIndex(e => e.Source);
    });

    modelBuilder.Entity<PostgresEntityAlias>(alias =>
    {
      alias.ToTable("entity_aliases");
      alias.HasKey(a => a.Id);
      alias.Property(a => a.Alias).IsRequired();
      alias.Property(a => a.NormalizedAlias).IsRequired();
      alias.Property(a => a.Type).IsRequired();
      alias.HasOne(a => a.Entity)
           .WithMany(e => e.Aliases)
           .HasForeignKey(a => a.EntityId);
      alias.HasIndex(a => new { a.NormalizedAlias, a.Type, a.EntityId }).IsUnique();
    });

    modelBuilder.Entity<PostgresMention>(mention =>
    {
      mention.ToTable("mentions");
      mention.HasKey(m => new { m.ArticleId, m.EntityId });

      mention.HasOne(m => m.Article)
                 .WithMany(a => a.Mentions)
                 .HasForeignKey(m => m.ArticleId);

      mention.HasOne(m => m.Entity)
                 .WithMany(e => e.Mentions)
                 .HasForeignKey(m => m.EntityId);
    });
  }
}
