using Agitprop.Sinks.Newsfeed.Database.Models;

using Microsoft.EntityFrameworkCore;

namespace Agitprop.Sinks.Newsfeed.Database;

public class AppDbContext : DbContext
{
  public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

  public DbSet<PostgresArticle> Articles => Set<PostgresArticle>();
  public DbSet<PostgresEntity> Entities => Set<PostgresEntity>();
  public DbSet<PostgresMention> Mentions => Set<PostgresMention>();

  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
  {
    optionsBuilder.EnableSensitiveDataLogging();
  }
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
      entity.HasIndex(e => e.Name).IsUnique();
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
