using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PaGetto.Core.Entities;

public abstract class AbstractContext<TContext> : DbContext, IContext where TContext : DbContext
{
    public const int DefaultMaxStringLength = 4000;

    public const int MaxPackageIdLength = 128;
    public const int MaxPackageVersionLength = 64;
    public const int MaxPackageMinClientVersionLength = 44;
    public const int MaxPackageLanguageLength = 20;
    public const int MaxPackageTitleLength = 256;
    public const int MaxPackageTypeNameLength = 512;
    public const int MaxPackageTypeVersionLength = 64;
    public const int MaxRepositoryTypeLength = 100;
    public const int MaxTargetFrameworkLength = 256;
    public const int MaxLicenseExpressionLength = 500;

    public const int MaxPackageDependencyVersionRangeLength = 256;

    public const int MaxUsernameLength = 256;
    public const int MaxDisplayNameLength = 256;
    public const int MaxEntraObjectIdLength = 128;
    public const int MaxEmailLength = 256;
    public const int MaxPasswordHashLength = 256;
    public const int MaxTokenNameLength = 256;
    public const int MaxTokenHashLength = 128;
    public const int MaxTokenPrefixLength = 8;
    public const int MaxGroupNameLength = 256;
    public const int MaxAppRoleValueLength = 128;
    public const int MaxFeedIdLength = 128;

    /// <summary>
    /// The name suffix of the migration that adds the normalized username and group name columns.
    /// </summary>
    public const string NormalizedNamesMigrationSuffix = "_AddNormalizedUserAndGroupNames";

    /// <summary>
    /// The name suffix of the migration that creates the Users and Groups tables.
    /// </summary>
    public const string AuthEntitiesMigrationSuffix = "_AddAuthEntities";

    protected AbstractContext(DbContextOptions<TContext> efOptions)
        : base(efOptions)
    { }

    public DbSet<Feed> Feeds { get; set; }
    public DbSet<FeedMirror> FeedMirrors { get; set; }
    public DbSet<Package> Packages { get; set; }
    public DbSet<PackageDependency> PackageDependencies { get; set; }
    public DbSet<PackageType> PackageTypes { get; set; }
    public DbSet<TargetFramework> TargetFrameworks { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<PersonalAccessToken> PersonalAccessTokens { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<UserGroup> UserGroups { get; set; }
    public DbSet<FeedPermission> FeedPermissions { get; set; }

    public Task<int> SaveChangesAsync() => SaveChangesAsync(default);

    public virtual async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        // The migration that makes usernames and group names unique regardless of case fills the
        // normalized columns with SQL UPPER(), which only folds ASCII on some databases. Names that
        // differ only in case are reported before it runs, and the columns are normalized in .NET after.
        // Both steps need the Users and Groups tables, which a database from before AddAuthEntities
        // doesn't have yet; it has no names to check either.
        var pending = await Database.GetPendingMigrationsAsync(cancellationToken);
        var addsNormalizedNames = pending.Any(m => m.EndsWith(NormalizedNamesMigrationSuffix, StringComparison.Ordinal))
            && (await Database.GetAppliedMigrationsAsync(cancellationToken))
                .Any(m => m.EndsWith(AuthEntitiesMigrationSuffix, StringComparison.Ordinal));

        if (addsNormalizedNames)
        {
            await ThrowOnNamesDifferingOnlyInCaseAsync(cancellationToken);
        }

        await Database.MigrateAsync(cancellationToken);

        if (addsNormalizedNames)
        {
            await NormalizeNamesAsync(cancellationToken);
        }
    }

    private async Task ThrowOnNamesDifferingOnlyInCaseAsync(CancellationToken cancellationToken)
    {
        var usernames = await Users.Select(u => u.Username).ToListAsync(cancellationToken);
        var groupNames = await Groups.Select(g => g.Name).ToListAsync(cancellationToken);

        var conflicts = FindConflicts("Usernames", usernames, User.NormalizeUsername)
            .Concat(FindConflicts("Group names", groupNames, Group.NormalizeName))
            .ToList();

        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException(
                "Usernames and group names must be unique regardless of case. Rename or delete all but one of each of these, " +
                "then start PaGetto again: " + string.Join("; ", conflicts) + ".");
        }
    }

    private static IEnumerable<string> FindConflicts(string kind, IEnumerable<string> names, Func<string, string> normalize)
    {
        return names
            .GroupBy(normalize)
            .Where(g => g.Count() > 1)
            .Select(g => $"{kind} {string.Join(", ", g.Select(n => $"'{n}'"))}");
    }

    private async Task NormalizeNamesAsync(CancellationToken cancellationToken)
    {
        foreach (var user in await Users.ToListAsync(cancellationToken))
        {
            var normalized = User.NormalizeUsername(user.Username);
            if (user.NormalizedUsername != normalized) user.NormalizedUsername = normalized;
        }

        foreach (var group in await Groups.ToListAsync(cancellationToken))
        {
            var normalized = Group.NormalizeName(group.Name);
            if (group.NormalizedName != normalized) group.NormalizedName = normalized;
        }

        await SaveChangesAsync(cancellationToken);
    }

    public abstract bool IsUniqueConstraintViolationException(DbUpdateException exception);

    public virtual bool SupportsLimitInSubqueries => true;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Feed>(BuildFeedEntity);
        builder.Entity<FeedMirror>(BuildFeedMirrorEntity);
        builder.Entity<Package>(BuildPackageEntity);
        builder.Entity<PackageDependency>(BuildPackageDependencyEntity);
        builder.Entity<PackageType>(BuildPackageTypeEntity);
        builder.Entity<TargetFramework>(BuildTargetFrameworkEntity);
        builder.Entity<User>(BuildUserEntity);
        builder.Entity<PersonalAccessToken>(BuildPersonalAccessTokenEntity);
        builder.Entity<Group>(BuildGroupEntity);
        builder.Entity<UserGroup>(BuildUserGroupEntity);
        builder.Entity<FeedPermission>(BuildFeedPermissionEntity);
    }

    private void BuildFeedEntity(EntityTypeBuilder<Feed> feed)
    {
        feed.HasKey(f => f.Id);
        feed.HasIndex(f => f.Slug).IsUnique();

        feed.Property(f => f.Slug)
            .HasMaxLength(MaxFeedIdLength)
            .IsRequired();

        feed.Property(f => f.Name)
            .HasMaxLength(MaxPackageTitleLength)
            .IsRequired();

        feed.Property(f => f.Description)
            .HasMaxLength(DefaultMaxStringLength);

        feed.Property(f => f.SortOrder).IsRequired().HasDefaultValue(0);

        feed.Property(f => f.CreatedAtUtc).IsRequired();
        feed.Property(f => f.UpdatedAtUtc).IsRequired();

        feed.HasMany(f => f.Packages)
            .WithOne(p => p.Feed)
            .HasForeignKey(p => p.FeedId)
            .OnDelete(DeleteBehavior.Restrict);

        feed.HasMany(f => f.Mirrors)
            .WithOne(m => m.Feed)
            .HasForeignKey(m => m.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildFeedMirrorEntity(EntityTypeBuilder<FeedMirror> mirror)
    {
        mirror.HasKey(m => m.Id);
        mirror.HasIndex(m => new { m.FeedId, m.SortOrder });

        mirror.Property(m => m.PackageSource).IsRequired();
    }

    private void BuildPackageEntity(EntityTypeBuilder<Package> package)
    {
        package.HasKey(p => p.Key);
        package.HasIndex(p => new { p.FeedId, p.Id });
        package.HasIndex(p => new { p.FeedId, p.Id, p.NormalizedVersionString })
            .IsUnique();

        package.Property(p => p.Id)
            .HasMaxLength(MaxPackageIdLength)
            .IsRequired();

        package.Property(p => p.NormalizedVersionString)
            .HasColumnName("Version")
            .HasMaxLength(MaxPackageVersionLength)
            .IsRequired();

        package.Property(p => p.OriginalVersionString)
            .HasColumnName("OriginalVersion")
            .HasMaxLength(MaxPackageVersionLength);

        package.Property(p => p.ReleaseNotes)
            .HasColumnName("ReleaseNotes");

        package.Property(p => p.Authors)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata.SetValueComparer(StringArrayComparer.Instance);

        package.Property(p => p.IconUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package.Property(p => p.LicenseUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package.Property(p => p.ProjectUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package.Property(p => p.RepositoryUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package.Property(p => p.Tags)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata.SetValueComparer(StringArrayComparer.Instance);

        package.Property(p => p.Description).HasMaxLength(DefaultMaxStringLength);
        package.Property(p => p.Language).HasMaxLength(MaxPackageLanguageLength);
        package.Property(p => p.MinClientVersion).HasMaxLength(MaxPackageMinClientVersionLength);
        package.Property(p => p.Summary).HasMaxLength(DefaultMaxStringLength);
        package.Property(p => p.Title).HasMaxLength(MaxPackageTitleLength);
        package.Property(p => p.Copyright).HasMaxLength(DefaultMaxStringLength);
        package.Property(p => p.LicenseExpression).HasMaxLength(MaxLicenseExpressionLength);
        package.Property(p => p.RepositoryType).HasMaxLength(MaxRepositoryTypeLength);
        package.Property(p => p.CachedFrom).HasMaxLength(DefaultMaxStringLength);

        package.Ignore(p => p.Version);
        package.Ignore(p => p.IconUrlString);
        package.Ignore(p => p.LicenseUrlString);
        package.Ignore(p => p.ProjectUrlString);
        package.Ignore(p => p.RepositoryUrlString);

        // TODO: This is needed to make the dependency to package relationship required.
        // Unfortunately, this would generate a migration that drops a foreign key, which
        // isn't supported by SQLite. The migrations will be need to be recreated for this.
        // Consumers will need to recreate their database and reindex all their packages.
        //package.HasMany(p => p.Dependencies)
        //    .WithOne(d => d.Package)
        //    .IsRequired();

        package.HasMany(p => p.PackageTypes)
            .WithOne(d => d.Package)
            .IsRequired();

        package.HasMany(p => p.TargetFrameworks)
            .WithOne(d => d.Package)
            .IsRequired();

        package.Property(p => p.RowVersion).IsRowVersion();
    }

    private void BuildPackageDependencyEntity(EntityTypeBuilder<PackageDependency> dependency)
    {
        dependency.HasKey(d => d.Key);
        dependency.HasIndex(d => d.Id);

        dependency.Property(d => d.Id).HasMaxLength(MaxPackageIdLength);
        dependency.Property(d => d.VersionRange).HasMaxLength(MaxPackageDependencyVersionRangeLength);
        dependency.Property(d => d.TargetFramework).HasMaxLength(MaxTargetFrameworkLength);
    }

    private void BuildPackageTypeEntity(EntityTypeBuilder<PackageType> type)
    {
        type.HasKey(d => d.Key);
        type.HasIndex(d => d.Name);

        type.Property(d => d.Name).HasMaxLength(MaxPackageTypeNameLength);
        type.Property(d => d.Version).HasMaxLength(MaxPackageTypeVersionLength);
    }

    private void BuildTargetFrameworkEntity(EntityTypeBuilder<TargetFramework> targetFramework)
    {
        targetFramework.HasKey(f => f.Key);
        targetFramework.HasIndex(f => f.Moniker);

        targetFramework.Property(f => f.Moniker).HasMaxLength(MaxTargetFrameworkLength);
    }

    private void BuildUserEntity(EntityTypeBuilder<User> user)
    {
        user.HasKey(u => u.Id);
        user.HasIndex(u => u.NormalizedUsername).IsUnique();
        user.HasIndex(u => u.EntraObjectId).IsUnique();

        user.Property(u => u.Username)
            .HasMaxLength(MaxUsernameLength)
            .IsRequired();

        user.Property(u => u.NormalizedUsername)
            .HasMaxLength(MaxUsernameLength);

        user.Property(u => u.DisplayName)
            .HasMaxLength(MaxDisplayNameLength)
            .IsRequired();

        user.Property(u => u.AuthProvider)
            .IsRequired();

        user.Property(u => u.EntraObjectId)
            .HasMaxLength(MaxEntraObjectIdLength);

        user.Property(u => u.Email)
            .HasMaxLength(MaxEmailLength);

        user.Property(u => u.PasswordHash)
            .HasMaxLength(MaxPasswordHashLength);

        user.Property(u => u.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        user.Property(u => u.IsAdmin)
            .IsRequired()
            .HasDefaultValue(false);

        user.Property(u => u.CanLoginToUI)
            .IsRequired();

        user.Property(u => u.MustChangePassword)
            .IsRequired()
            .HasDefaultValue(false);

        user.Property(u => u.FailedLoginCount)
            .IsRequired()
            .HasDefaultValue(0);

        user.Property(u => u.CreatedAtUtc).IsRequired();
        user.Property(u => u.UpdatedAtUtc).IsRequired();

        user.HasOne(u => u.CreatedByUser)
            .WithMany()
            .HasForeignKey(u => u.CreatedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        user.HasMany(u => u.PersonalAccessTokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        user.HasMany(u => u.UserGroups)
            .WithOne(ug => ug.User)
            .HasForeignKey(ug => ug.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildPersonalAccessTokenEntity(EntityTypeBuilder<PersonalAccessToken> token)
    {
        token.HasKey(t => t.Id);
        token.HasIndex(t => t.TokenHash).IsUnique();
        token.HasIndex(t => new { t.UserId, t.Name }).IsUnique();

        token.Property(t => t.Name)
            .HasMaxLength(MaxTokenNameLength)
            .IsRequired();

        token.Property(t => t.TokenHash)
            .HasMaxLength(MaxTokenHashLength)
            .IsRequired();

        token.Property(t => t.TokenPrefix)
            .HasMaxLength(MaxTokenPrefixLength)
            .IsRequired();

        token.Property(t => t.ExpiresAtUtc).IsRequired();
        token.Property(t => t.CreatedAtUtc).IsRequired();
        token.Property(t => t.IsRevoked).IsRequired().HasDefaultValue(false);
    }

    private void BuildGroupEntity(EntityTypeBuilder<Group> group)
    {
        group.HasKey(g => g.Id);
        group.HasIndex(g => g.NormalizedName).IsUnique();

        group.Property(g => g.Name)
            .HasMaxLength(MaxGroupNameLength)
            .IsRequired();

        group.Property(g => g.NormalizedName)
            .HasMaxLength(MaxGroupNameLength);

        group.Property(g => g.AppRoleValue)
            .HasMaxLength(MaxAppRoleValueLength);

        group.HasIndex(g => g.AppRoleValue)
            .IsUnique();

        group.Property(g => g.Description)
            .HasMaxLength(DefaultMaxStringLength);

        group.Property(g => g.CreatedAtUtc).IsRequired();

        group.HasMany(g => g.UserGroups)
            .WithOne(ug => ug.Group)
            .HasForeignKey(ug => ug.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildUserGroupEntity(EntityTypeBuilder<UserGroup> userGroup)
    {
        userGroup.HasKey(ug => new { ug.UserId, ug.GroupId });
    }

    private void BuildFeedPermissionEntity(EntityTypeBuilder<FeedPermission> permission)
    {
        permission.HasKey(p => p.Id);
        permission.HasIndex(p => new { p.FeedId, p.PrincipalType, p.PrincipalId }).IsUnique();

        permission.Property(p => p.FeedId).IsRequired();
        permission.Property(p => p.PrincipalType).IsRequired();
        permission.Property(p => p.PrincipalId).IsRequired();
        permission.Property(p => p.CanPush).IsRequired().HasDefaultValue(false);
        permission.Property(p => p.CanPull).IsRequired().HasDefaultValue(false);
        permission.Property(p => p.CanDelete).IsRequired().HasDefaultValue(false);
        permission.Property(p => p.Source).IsRequired().HasDefaultValue(PermissionSource.Manual);

        permission.HasOne(p => p.Feed)
            .WithMany(f => f.Permissions)
            .HasForeignKey(p => p.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
