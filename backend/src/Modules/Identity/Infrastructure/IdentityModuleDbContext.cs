using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Infrastructure;

/// <summary>
/// Identity module data in schema <c>identity</c>. Users (and their Identity claims, logins and
/// tokens) are global; workspaces and memberships are tenant-owned and isolated by query filters
/// and Row-Level Security.
/// </summary>
internal sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options, ITenantContext tenantContext)
    : IdentityUserContext<User, Guid>(options), ITenantDbContext
{
    public const string Schema = "identity";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
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

        builder.ApplyTenantQueryFilters(this);
    }
}
