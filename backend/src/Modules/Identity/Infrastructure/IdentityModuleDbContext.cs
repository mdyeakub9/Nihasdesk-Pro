using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Infrastructure;

/// <summary>
/// Identity module data in schema <c>identity</c>. Users (and their Identity claims, logins and
/// tokens) are global; workspaces and memberships are tenant-owned and isolated by query filters
/// and Row-Level Security.
/// </summary>
internal sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options, ITenantContext tenantContext)
    : IdentityUserContext<User, Guid>(options), ITenantDbContext, IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>ASP.NET Core Data Protection key ring (cookies, tokens), shared by all API instances.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
            user.Property(u => u.Language).HasMaxLength(User.LanguageMaxLength);
            // One account per email, enforced by the database too.
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email").IsUnique();
            user.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name").IsUnique();
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<Organization>(organization =>
        {
            organization.ToTable("organizations");
            organization.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength);
        });

        builder.Entity<Workspace>(workspace =>
        {
            workspace.ToTable("workspaces", table =>
                table.HasCheckConstraint("ck_workspaces_tenant_is_id", "tenant_id = id"));
            workspace.Property(w => w.Name).HasMaxLength(Workspace.NameMaxLength);
            workspace.HasOne<Organization>().WithMany().HasForeignKey(w => w.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            workspace.HasIndex(w => w.TenantId).IsUnique();
        });

        builder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships");
            membership.Property(m => m.Role).HasConversion(
                role => role.ToString().ToLowerInvariant(),
                value => Enum.Parse<Role>(value, ignoreCase: true));
            membership.Property(m => m.Role).HasMaxLength(20);
            membership.HasOne<Workspace>().WithMany().HasForeignKey(m => m.TenantId).HasPrincipalKey(w => w.Id)
                .OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            membership.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
            membership.HasIndex(m => m.UserId);
        });

        builder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("invitations");
            invitation.Property(i => i.Email).HasMaxLength(Invitation.EmailMaxLength);
            invitation.Property(i => i.Role).HasConversion(
                role => role.ToString().ToLowerInvariant(),
                value => Enum.Parse<Role>(value, ignoreCase: true));
            invitation.Property(i => i.Role).HasMaxLength(20);
            invitation.HasOne<Workspace>().WithMany().HasForeignKey(i => i.TenantId).HasPrincipalKey(w => w.Id)
                .OnDelete(DeleteBehavior.Cascade);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasIndex(i => new { i.TenantId, i.Email });
        });

        builder.Entity<UserSession>(session =>
        {
            session.ToTable("sessions");
            session.Property(s => s.UserAgent).HasMaxLength(UserSession.UserAgentMaxLength);
            session.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(s => s.UserId);
            session.HasIndex(s => s.AbsoluteExpiresAt);
        });

        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.Entity<RecoveryCode>(code =>
        {
            code.ToTable("recovery_codes");
            code.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            code.HasIndex(c => c.UserId);
        });

        builder.Entity<AuditEvent>(audit =>
        {
            audit.ToTable("audit_events");
            audit.Property(a => a.Action).HasMaxLength(AuditEvent.ActionMaxLength);
            audit.Property(a => a.IpAddress).HasMaxLength(AuditEvent.IpAddressMaxLength);
            audit.HasIndex(a => a.OccurredAt);
            audit.HasIndex(a => a.UserId);
        });

        builder.MapEmailOutbox();

        builder.ApplyTenantQueryFilters(this);
    }
}
