using System.Text.Json;
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

    private static readonly JsonSerializerOptions TraceJson = new(JsonSerializerDefaults.Web);

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<KnowledgeGap> Gaps => Set<KnowledgeGap>();

    public DbSet<TaughtAnswer> Answers => Set<TaughtAnswer>();

    public DbSet<AiRun> Runs => Set<AiRun>();

    public DbSet<AiSettings> Settings => Set<AiSettings>();

    public DbSet<ShopPolicies> Policies => Set<ShopPolicies>();

    public DbSet<ReplyFeedback> Feedback => Set<ReplyFeedback>();

    public DbSet<KnowledgeSource> Sources => Set<KnowledgeSource>();

    public DbSet<KnowledgeSection> Sections => Set<KnowledgeSection>();

    public DbSet<ConversationMemory> Memory => Set<ConversationMemory>();

    public DbSet<KnowledgeSite> Sites => Set<KnowledgeSite>();

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
            run.Property(r => r.InstructionVersion).HasMaxLength(AiRun.InstructionVersionMaxLength);
            run.Property(r => r.Trace)
                .HasColumnType("jsonb")
                .HasConversion(
                    trace => JsonSerializer.Serialize(trace, TraceJson),
                    json => JsonSerializer.Deserialize<RunTrace>(json, TraceJson));
            // "Why this answer" and 👍/👎 find the run by the message it sent.
            run.HasIndex(r => new { r.TenantId, r.MessageId });
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

        modelBuilder.Entity<ShopPolicies>(policies =>
        {
            policies.ToTable("shop_policies", table =>
                table.HasCheckConstraint("ck_shop_policies_return_days", $"return_days IS NULL OR return_days BETWEEN 0 AND {ShopPolicies.MaxReturnDays}"));
            policies.Property(p => p.Id).ValueGeneratedNever();
            policies.Property(p => p.ShippingNotes).HasMaxLength(ShopPolicies.ShippingNotesMaxLength);
            policies.Property(p => p.ReturnPolicy).HasMaxLength(ShopPolicies.ReturnPolicyMaxLength);
            policies.Property(p => p.PaymentNotes).HasMaxLength(ShopPolicies.PaymentNotesMaxLength);
            policies.Property(p => p.ContactPhone).HasMaxLength(ShopPolicies.PhoneMaxLength);
            policies.Property(p => p.ContactEmail).HasMaxLength(ShopPolicies.EmailMaxLength);
            policies.Property(p => p.ContactAddress).HasMaxLength(ShopPolicies.AddressMaxLength);
            policies.Property(p => p.OpeningHours).HasMaxLength(ShopPolicies.OpeningHoursMaxLength);
            // One row per workspace.
            policies.HasIndex(p => p.TenantId).IsUnique();
        });

        modelBuilder.Entity<ReplyFeedback>(feedback =>
        {
            feedback.ToTable("reply_feedback");
            feedback.Property(f => f.Id).ValueGeneratedNever();
            LowerCaseText(feedback.Property(f => f.Rating));
            feedback.HasIndex(f => new { f.TenantId, f.MessageId, f.UserId }).IsUnique();
            feedback.HasIndex(f => new { f.TenantId, f.Rating, f.CreatedAt });
        });

        modelBuilder.Entity<KnowledgeSource>(source =>
        {
            source.ToTable("knowledge_sources");
            source.Property(s => s.Id).ValueGeneratedNever();
            source.Property(s => s.Title).HasMaxLength(KnowledgeSource.TitleMaxLength);
            source.Property(s => s.Origin).HasMaxLength(KnowledgeSource.OriginMaxLength);
            source.Property(s => s.StorageKey).HasMaxLength(KnowledgeSource.StorageKeyMaxLength);
            source.Property(s => s.ContentHash).HasMaxLength(KnowledgeSource.HashLength);
            source.Property(s => s.FailureReason).HasMaxLength(KnowledgeSource.FailureMaxLength);
            LowerCaseText(source.Property(s => s.Kind));
            LowerCaseText(source.Property(s => s.Status));
            source.HasIndex(s => new { s.TenantId, s.CreatedAt });
            // The same file or page twice in a workspace is noticed, not stored again (PRD 4.7).
            source.HasIndex(s => new { s.TenantId, s.ContentHash }).IsUnique().HasFilter("content_hash IS NOT NULL");
            source.HasIndex(s => new { s.TenantId, s.Origin }).IsUnique().HasFilter("kind = 'webpage'");
        });

        modelBuilder.Entity<KnowledgeSection>(section =>
        {
            section.ToTable("knowledge_sections");
            section.Property(s => s.Id).ValueGeneratedNever();
            section.Property(s => s.HeadingPath).HasMaxLength(KnowledgeSection.HeadingMaxLength);
            section.Property(s => s.Text).HasMaxLength(KnowledgeSection.TextMaxLength);
            section.Property(s => s.Language).HasMaxLength(KnowledgeSection.LanguageMaxLength);
            section.Property(s => s.EmbeddingModel).HasMaxLength(KnowledgeSection.ModelMaxLength);
            section.Property(s => s.TextHash).HasMaxLength(KnowledgeSource.HashLength);
            section.HasIndex(s => new { s.TenantId, s.TextHash });
            LowerCaseText(section.Property(s => s.Kind));
            section.HasIndex(s => new { s.TenantId, s.SourceId, s.Position });
            // Deleting a source deletes its sections and search data (SEC-45).
            section.HasOne<KnowledgeSource>().WithMany().HasForeignKey(s => s.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeSite>(site =>
        {
            site.ToTable("knowledge_sites");
            site.Property(s => s.Id).ValueGeneratedNever();
            site.Property(s => s.Host).HasMaxLength(KnowledgeSite.HostMaxLength);
            site.Property(s => s.RootUrl).HasMaxLength(KnowledgeSite.HostMaxLength + 10);
            site.Property(s => s.FailureReason).HasMaxLength(KnowledgeSite.FailureMaxLength);
            LowerCaseText(site.Property(s => s.Status));
            site.Property(s => s.Pages)
                .HasColumnType("jsonb")
                .HasConversion(
                    pages => JsonSerializer.Serialize(pages, TraceJson),
                    json => JsonSerializer.Deserialize<List<FoundPage>>(json, TraceJson) ?? new List<FoundPage>());
            // The same site once per workspace.
            site.HasIndex(s => new { s.TenantId, s.Host }).IsUnique();
            // The job looks for waiting sites every few seconds; almost none are.
            site.HasIndex(s => s.Status).HasFilter("status = 'finding'");
            // Deleting a site deletes its pages and their search data (SEC-45).
            site.HasMany<KnowledgeSource>().WithOne().HasForeignKey(s => s.SiteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationMemory>(memory =>
        {
            memory.ToTable("conversation_memory");
            memory.Property(m => m.Id).ValueGeneratedNever();
            memory.Property(m => m.Summary).HasMaxLength(ConversationMemory.SummaryMaxLength);
            // One summary per conversation.
            memory.HasIndex(m => new { m.TenantId, m.ConversationId }).IsUnique();
            // Erasure finds a contact's summaries (SEC-45).
            memory.HasIndex(m => new { m.TenantId, m.ContactId });
            // The job looks for due summaries every few seconds; almost none are.
            memory.HasIndex(m => m.SummaryDueAt).HasFilter("summary_due_at IS NOT NULL");
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
