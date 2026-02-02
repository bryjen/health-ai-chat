using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using WebApi.Models;

namespace WebApi.Data;

public class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// Fallback configuration for design-time scenarios when options aren't provided via DI.
    /// In runtime, options are configured via ServiceConfiguration.ConfigureDatabase().
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Build configuration from appsettings.json for design-time scenarios
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.UseVector(); // Required for Vector type mapping
                });
            }
        }
    }

    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Symptom> Symptoms { get; set; }
    public DbSet<Episode> Episodes { get; set; }
    public DbSet<NegativeFinding> NegativeFindings { get; set; }
    public DbSet<Assessment> Assessments { get; set; }
    public DbSet<AssessmentEpisodeLink> AssessmentEpisodeLinks { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<MessageEmbedding> MessageEmbeddings { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeDateTimeValues();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        NormalizeDateTimeValues();
        return base.SaveChanges();
    }

    private void NormalizeDateTimeValues()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.ClrType == typeof(DateTime) || property.Metadata.ClrType == typeof(DateTime?))
                {
                    if (property.CurrentValue is DateTime dateTime)
                    {
                        if (dateTime.Kind == DateTimeKind.Unspecified)
                        {
                            // Treat Unspecified as UTC (assume incoming dates are already in UTC)
                            property.CurrentValue = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                        }
                        else if (dateTime.Kind == DateTimeKind.Local)
                        {
                            // Convert Local to UTC
                            property.CurrentValue = dateTime.ToUniversalTime();
                        }
                        // Already UTC, no change needed
                    }
                }
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // sets the default schema for PostgreSQL
        modelBuilder.HasDefaultSchema("conuhacks");

        // Configure Vector type mapping for pgvector
        // This ensures EF Core recognizes Vector as mappable to vector type
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.Provider, e.Email }).IsUnique();

            entity.HasIndex(e => new { e.Provider, e.ProviderUserId })
                .IsUnique()
                .HasFilter("\"ProviderUserId\" IS NOT NULL");

            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PasswordHash)
                .IsRequired(false)
                .HasMaxLength(255);
            entity.Property(e => e.Provider).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ProviderUserId).HasMaxLength(255);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.PhoneCountryCode).HasMaxLength(8);
            entity.Property(e => e.PhoneNumber).HasMaxLength(30);
            entity.Property(e => e.Country).HasMaxLength(64);
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.PostalCode).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasMany(e => e.RefreshTokens)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(rt => rt.ReplacedByToken)
                .IsRequired(false)
                .HasMaxLength(255);
            entity.Property(rt => rt.RevocationReason)
                .IsRequired(false)
                .HasMaxLength(255);

            entity.HasOne(e => e.User)
                .WithMany(e => e.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(e => e.PasswordResetRequests)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Messages)
                .WithOne(e => e.Conversation)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.StatusInformationJson)
                .HasColumnType("jsonb")
                .IsRequired(false);

            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });
        });

        modelBuilder.Entity<Symptom>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd(); // Auto-increment

            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Name });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Episodes)
                .WithOne(e => e.Symptom)
                .HasForeignKey(e => e.SymptomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Episode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd(); // Auto-increment

            entity.Property(e => e.Stage).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.StartedAt).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.Frequency).HasMaxLength(50);
            entity.Property(e => e.Pattern).HasMaxLength(500);
            
            // JSON conversions for collections
            entity.Property(e => e.Triggers)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v,
                            (System.Text.Json.JsonSerializerOptions?)null));
            
            entity.Property(e => e.Relievers)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v,
                            (System.Text.Json.JsonSerializerOptions?)null));
            
            entity.Property(e => e.Timeline)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<List<EpisodeTimelineEntry>>(v,
                            (System.Text.Json.JsonSerializerOptions?)null));
            
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.SymptomId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartedAt);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Symptom)
                .WithMany(s => s.Episodes)
                .HasForeignKey(e => e.SymptomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NegativeFinding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd(); // Auto-increment

            entity.Property(e => e.SymptomName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ReportedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.EpisodeId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Episode)
                .WithMany()
                .HasForeignKey(e => e.EpisodeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Assessment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd(); // Auto-increment

            entity.Property(e => e.Hypothesis).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Confidence).IsRequired().HasColumnType("decimal(3,2)");
            entity.Property(e => e.Reasoning).IsRequired();
            entity.Property(e => e.RecommendedAction).IsRequired().HasMaxLength(50);
            
            // JSON conversions for collections
            entity.Property(e => e.Differentials)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v,
                            (System.Text.Json.JsonSerializerOptions?)null));
            
            entity.Property(e => e.NegativeFindingIds)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<List<int>>(v,
                            (System.Text.Json.JsonSerializerOptions?)null));
            
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ConversationId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.LinkedEpisodes)
                .WithOne(l => l.Assessment)
                .HasForeignKey(l => l.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssessmentEpisodeLink>(entity =>
        {
            entity.HasKey(e => new { e.AssessmentId, e.EpisodeId });

            entity.Property(e => e.Weight).IsRequired().HasColumnType("decimal(3,2)");
            entity.Property(e => e.Reasoning).HasMaxLength(1000);

            entity.HasIndex(e => e.AssessmentId);
            entity.HasIndex(e => e.EpisodeId);

            entity.HasOne(e => e.Assessment)
                .WithMany(a => a.LinkedEpisodes)
                .HasForeignKey(e => e.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Episode)
                .WithMany()
                .HasForeignKey(e => e.EpisodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ClinicName).HasMaxLength(200);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Urgency).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.DateTime });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageEmbedding>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Configure vector column - UseVector() extension handles Vector type mapping automatically
            // Specify dimension constraint for vector(1536) - text-embedding-3-small
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(1536)")
                .IsRequired();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Message)
                .WithMany()
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

