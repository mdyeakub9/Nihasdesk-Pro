using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Infrastructure;

/// <summary>AI data in schema <c>ai</c>; every table is tenant-owned (query filters + RLS).</summary>
internal sealed class AiDbContext(DbContextOptions<AiDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantDbContext
{
    public const string Schema = "ai";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<KnowledgeGap> Gaps => Set<KnowledgeGap>();

    public DbSet<TaughtAnswer> Answers => Set<TaughtAnswer>();

    public DbSet<AiRun> Runs => Set<AiRun>();

    public DbSet<AiSettings> Settings => Set<AiSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<KnowledgeGap>(gap =>
        {
            gap.ToTable("knowledge_gaps");
            gap.Property(g => g.Id).ValueGeneratedNever();
            gap.Property(g => g.Question).HasMaxLength(KnowledgeGap.QuestionMaxLength);
            LowerCaseText(gap.Property(g => g.Status));
            gap.HasIndex(g => new { g.TenantId, g.Status, g.CreatedAt });
        });

        modelBuilder.Entity<TaughtAnswer>(answer =>
        {
            answer.ToTable("taught_answers");
            answer.Property(a => a.Id).ValueGeneratedNever();
            answer.Property(a => a.Question).HasMaxLength(TaughtAnswer.QuestionMaxLength);
            answer.Property(a => a.Answer).HasMaxLength(TaughtAnswer.AnswerMaxLength);
            answer.HasIndex(a => new { a.TenantId, a.Active });
        });

        modelBuilder.Entity<AiRun>(run =>
        {
            run.ToTable("runs");
            run.Property(r => r.Id).ValueGeneratedNever();
            run.Property(r => r.Model).HasMaxLength(AiRun.ModelMaxLength);
            run.Property(r => r.Detail).HasMaxLength(AiRun.DetailMaxLength);
            LowerCaseText(run.Property(r => r.Outcome));
            // Limits count replies per workspace per day and per conversation per hour.
            run.HasIndex(r => new { r.TenantId, r.CreatedAt });
            run.HasIndex(r => new { r.ConversationId, r.CreatedAt });
        });

        modelBuilder.Entity<AiSettings>(settings =>
        {
            settings.ToTable("settings");
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.Instructions).HasMaxLength(AiSettings.InstructionsMaxLength);
            settings.Property(s => s.Disclosure).HasMaxLength(AiSettings.DisclosureMaxLength);
            LowerCaseText(settings.Property(s => s.Tone));
            // One row per workspace.
            settings.HasIndex(s => s.TenantId).IsUnique();
        });

        modelBuilder.ApplyTenantQueryFilters(this);
    }

    private static void LowerCaseText<TEnum>(PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property
            .HasConversion(value => value.ToString().ToLowerInvariant(), value => Enum.Parse<TEnum>(value, true))
            .HasMaxLength(20);
}

/// <summary>Used by <c>dotnet ef</c> with the schema-owner connection (docs/standards/database.md).</summary>
internal sealed class AiDbContextFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    public AiDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static AiDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<AiDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, AiDbContext.Schema);
        return new AiDbContext(options.Options, new TenantContext());
    }
}
