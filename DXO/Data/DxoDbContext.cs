using Microsoft.EntityFrameworkCore;
using DXO.Models;

namespace DXO.Data;

/// <summary>
/// Entity Framework Core database context for DXO
/// </summary>
public class DxoDbContext : DbContext
{
    public DxoDbContext(DbContextOptions<DxoDbContext> options) : base(options)
    {
    }

    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<ConfiguredModel> ConfiguredModels { get; set; } = null!;
    public DbSet<FeedbackRound> FeedbackRounds { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Session configuration
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.StopMarker).HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.StopReason).HasConversion<string>();
            entity.Property(e => e.RunMode).HasConversion<string>();
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Persona).HasConversion<string>();
            entity.Property(e => e.Role).HasConversion<string>();
            entity.Property(e => e.ModelUsed).HasMaxLength(100);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.SessionId, e.Iteration });

            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Messages)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ConfiguredModel configuration
        modelBuilder.Entity<ConfiguredModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Endpoint).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ApiKey).HasMaxLength(1000); // Encrypted keys are longer
            entity.Property(e => e.UserEmail).IsRequired().HasMaxLength(200);
            
            // Remove unique constraint on ModelName alone - multiple users can have same model name
            // Add composite unique index on UserEmail + ModelName
            entity.HasIndex(e => new { e.UserEmail, e.ModelName }).IsUnique();
            entity.HasIndex(e => e.UserEmail); // For efficient user filtering
            entity.HasIndex(e => e.CreatedAt);
        });

        // FeedbackRound configuration
        modelBuilder.Entity<FeedbackRound>(entity =>
        {
            entity.HasKey(e => e.FeedbackRoundId);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => new { e.SessionId, e.Iteration });
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Session)
                  .WithMany(s => s.FeedbackRounds)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}