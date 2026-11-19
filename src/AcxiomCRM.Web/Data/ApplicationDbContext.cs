using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AcxiomCRM.Web.Data;

/// <summary>
/// EF Core context (Data Access Layer). Besides persistence it centralises three cross-cutting rules:
///  1. creation/modification stamps on <see cref="ITrackedEntity"/>;
///  2. soft delete for <see cref="ISoftDelete"/> (Remove() becomes a flag update + global query filter);
///  3. automatic, transactional audit entries (old/new values) for <see cref="IAuditableEntity"/>,
///     while keeping <see cref="AuditLog"/> rows append-only.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private static readonly HashSet<string> UtcDateProperties =
    [
        nameof(ITrackedEntity.CreatedDate), nameof(ITrackedEntity.ModifiedDate), nameof(ISoftDelete.DeletedDate),
        nameof(Opportunity.ClosedDate), nameof(FollowUp.CompletedDate), nameof(Lead.ConvertedDate),
        nameof(ApplicationUser.LastLoginDate)
    ];

    private static readonly HashSet<string> AuditIgnoredProperties =
    [
        nameof(ITrackedEntity.CreatedDate), nameof(ITrackedEntity.CreatedById),
        nameof(ITrackedEntity.ModifiedDate), nameof(ITrackedEntity.ModifiedById),
        nameof(ISoftDelete.IsDeleted), nameof(ISoftDelete.DeletedDate), nameof(ISoftDelete.DeletedById)
    ];

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly ICurrentUser? _currentUser;
    private readonly TimeProvider _time;
    private readonly Dictionary<object, AuditHint> _hints = new(ReferenceEqualityComparer.Instance);

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUser? currentUser = null, TimeProvider? time = null)
        : base(options)
    {
        _currentUser = currentUser;
        _time = time ?? TimeProvider.System;
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordHistory> PasswordHistories => Set<PasswordHistory>();

    /// <summary>Disable automatic auditing (used by the seeder).</summary>
    public bool AutoAuditEnabled { get; set; } = true;

    /// <summary>Give the next automatic audit entry for <paramref name="entity"/> a semantic action/description.</summary>
    public void SetAuditHint(object entity, AuditAction action, string? details = null) =>
        _hints[entity] = new AuditHint(action, details);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite has no native decimal; store as REAL so SUM/ORDER BY translate to SQL.
        builder.Properties<decimal>().HaveConversion<double>();

        // Persist enums as readable strings.
        builder.Properties<CustomerStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<LeadStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<LeadSource>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<LeadPriority>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<OpportunityStage>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<OpportunityStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<FollowUpType>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<FollowUpStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ActivityType>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ActivityStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<AuditModule>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<AuditAction>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<AuditResult>().HaveConversion<string>().HaveMaxLength(10);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            e.HasOne(u => u.Manager).WithMany(u => u.DirectReports).HasForeignKey(u => u.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(u => u.IsActive);
        });

        b.Entity<PasswordHistory>(e =>
        {
            e.Property(p => p.UserId).HasMaxLength(450).IsRequired();
            e.Property(p => p.PasswordHash).IsRequired();
            e.HasIndex(p => new { p.UserId, p.CreatedDate });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Customer>(e =>
        {
            e.Property(c => c.CustomerCode).HasMaxLength(20).IsRequired();
            e.Property(c => c.CustomerName).HasMaxLength(100).IsRequired();
            e.Property(c => c.Email).HasMaxLength(150).IsRequired();
            e.Property(c => c.Phone).HasMaxLength(15).IsRequired();
            e.Property(c => c.CompanyName).HasMaxLength(150);
            e.Property(c => c.Address).HasMaxLength(250);
            e.Property(c => c.City).HasMaxLength(100);
            e.Property(c => c.State).HasMaxLength(100);
            e.Property(c => c.Notes).HasMaxLength(2000);
            e.Property(c => c.AssignedToId).HasMaxLength(450).IsRequired();
            e.HasIndex(c => c.CustomerCode).IsUnique();
            e.HasIndex(c => c.Email).IsUnique().HasFilter("\"IsDeleted\" = 0");
            e.HasIndex(c => c.Phone).IsUnique().HasFilter("\"IsDeleted\" = 0");
            e.HasIndex(c => c.CustomerName);
            e.HasIndex(c => c.AssignedToId);
            e.HasIndex(c => c.Status);
            e.HasOne(c => c.AssignedTo).WithMany().HasForeignKey(c => c.AssignedToId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Lead>(e =>
        {
            e.Property(l => l.LeadCode).HasMaxLength(20).IsRequired();
            e.Property(l => l.LeadName).HasMaxLength(100).IsRequired();
            e.Property(l => l.Email).HasMaxLength(150);
            e.Property(l => l.Phone).HasMaxLength(15);
            e.Property(l => l.CompanyName).HasMaxLength(150);
            e.Property(l => l.Notes).HasMaxLength(2000);
            e.Property(l => l.AssignedToId).HasMaxLength(450).IsRequired();
            e.HasIndex(l => l.LeadCode).IsUnique();
            e.HasIndex(l => l.AssignedToId);
            e.HasIndex(l => l.Status);
            e.HasIndex(l => l.LeadName);
            e.HasOne(l => l.AssignedTo).WithMany().HasForeignKey(l => l.AssignedToId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.ConvertedCustomer).WithMany().HasForeignKey(l => l.ConvertedCustomerId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Lead_ExpectedValue", "\"ExpectedValue\" IS NULL OR \"ExpectedValue\" >= 0"));
        });

        b.Entity<Opportunity>(e =>
        {
            e.Property(o => o.OpportunityName).HasMaxLength(150).IsRequired();
            e.Property(o => o.Notes).HasMaxLength(2000);
            e.Property(o => o.CloseReason).HasMaxLength(500);
            e.Property(o => o.AssignedToId).HasMaxLength(450).IsRequired();
            e.HasIndex(o => o.AssignedToId);
            e.HasIndex(o => o.Stage);
            e.HasIndex(o => o.ExpectedCloseDate);
            e.HasOne(o => o.Customer).WithMany(c => c.Opportunities).HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Lead).WithMany().HasForeignKey(o => o.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.AssignedTo).WithMany().HasForeignKey(o => o.AssignedToId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Opportunity_Probability", "\"Probability\" >= 0 AND \"Probability\" <= 100");
                t.HasCheckConstraint("CK_Opportunity_Amount", "\"Amount\" >= 0");
            });
        });

        b.Entity<FollowUp>(e =>
        {
            e.Property(f => f.Subject).HasMaxLength(200).IsRequired();
            e.Property(f => f.Remarks).HasMaxLength(1000);
            e.Property(f => f.Outcome).HasMaxLength(1000);
            e.Property(f => f.AssignedToId).HasMaxLength(450).IsRequired();
            e.Ignore(f => f.RelatedType);
            e.Ignore(f => f.RelatedId);
            e.HasIndex(f => f.AssignedToId);
            e.HasIndex(f => new { f.Status, f.FollowUpDate });
            e.HasOne(f => f.Customer).WithMany(c => c.FollowUps).HasForeignKey(f => f.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.Lead).WithMany(l => l.FollowUps).HasForeignKey(f => f.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.Opportunity).WithMany(o => o.FollowUps).HasForeignKey(f => f.OpportunityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.AssignedTo).WithMany().HasForeignKey(f => f.AssignedToId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Activity>(e =>
        {
            e.Property(a => a.Subject).HasMaxLength(200).IsRequired();
            e.Property(a => a.Description).HasMaxLength(2000);
            e.Property(a => a.AssignedToId).HasMaxLength(450).IsRequired();
            e.HasIndex(a => a.AssignedToId);
            e.HasIndex(a => a.ActivityDate);
            e.HasOne(a => a.Customer).WithMany(c => c.Activities).HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Lead).WithMany(l => l.Activities).HasForeignKey(a => a.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Opportunity).WithMany().HasForeignKey(a => a.OpportunityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.AssignedTo).WithMany().HasForeignKey(a => a.AssignedToId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(a => a.UserId).HasMaxLength(450);
            e.Property(a => a.UserName).HasMaxLength(256);
            e.Property(a => a.EntityName).HasMaxLength(50);
            e.Property(a => a.RecordId).HasMaxLength(64);
            e.Property(a => a.Details).HasMaxLength(1000);
            e.Property(a => a.IpAddress).HasMaxLength(64);
            e.Property(a => a.UserAgent).HasMaxLength(256);
            e.HasIndex(a => a.CreatedDate);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => new { a.Module, a.Action });
            e.HasIndex(a => new { a.EntityName, a.RecordId });
        });

        // Soft-delete query filters + UTC kind for timestamp columns.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (typeof(ISoftDelete).IsAssignableFrom(clr))
            {
                var p = Expression.Parameter(clr, "e");
                var body = Expression.Equal(Expression.Property(p, nameof(ISoftDelete.IsDeleted)), Expression.Constant(false));
                b.Entity(clr).HasQueryFilter(Expression.Lambda(body, p));
            }

            foreach (var prop in entityType.GetProperties())
            {
                var isUtc = UtcDateProperties.Contains(prop.Name) || (clr == typeof(AuditLog) && prop.Name == nameof(AuditLog.CreatedDate))
                            || (clr == typeof(PasswordHistory) && prop.Name == nameof(PasswordHistory.CreatedDate));
                if (!isUtc) continue;
                if (prop.ClrType == typeof(DateTime))
                    prop.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                else if (prop.ClrType == typeof(DateTime?))
                    prop.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v));
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnly();
        var now = _time.GetUtcNow().UtcDateTime;
        ApplyTrackingAndSoftDelete(now);

        var pending = AutoAuditEnabled ? CaptureAuditEntries(now) : [];
        if (pending.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        var ownTransaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            foreach (var p in pending)
            {
                p.Log.RecordId ??= KeyOf(p.Entry);
                AuditLogs.Add(p.Log);
            }
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownTransaction is not null)
                await ownTransaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            if (ownTransaction is not null)
                await ownTransaction.DisposeAsync();
        }
    }

    private void EnforceAppendOnly()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit log entries are append-only and cannot be modified or deleted.");
        if (ChangeTracker.Entries<PasswordHistory>().Any(e => e.State == EntityState.Modified))
            throw new InvalidOperationException("Password history entries cannot be modified.");
    }

    private void ApplyTrackingAndSoftDelete(DateTime now)
    {
        var userId = _currentUser?.UserId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ISoftDelete soft && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedDate = now;
                soft.DeletedById = userId;
            }

            if (entry.Entity is not ITrackedEntity tracked) continue;
            if (entry.State == EntityState.Added)
            {
                if (tracked.CreatedDate == default) tracked.CreatedDate = now; // seeded/imported rows may carry their own date
                tracked.CreatedById ??= userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                tracked.ModifiedDate = now;
                tracked.ModifiedById = userId;
            }
        }
    }

    private List<PendingAudit> CaptureAuditEntries(DateTime now)
    {
        var list = new List<PendingAudit>();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is IAuditableEntity))
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var hint = _hints.GetValueOrDefault(entry.Entity);
            AuditAction action;
            Dictionary<string, object?>? oldValues = null, newValues = null;

            if (entry.State == EntityState.Added)
            {
                action = AuditAction.Create;
                newValues = Snapshot(entry, current: true);
            }
            else
            {
                var deletedNow = entry.Entity is ISoftDelete
                                 && entry.Property(nameof(ISoftDelete.IsDeleted)) is { IsModified: true } del
                                 && del.CurrentValue is true && del.OriginalValue is false;
                if (deletedNow)
                {
                    action = AuditAction.Delete;
                    oldValues = Snapshot(entry, current: false);
                }
                else
                {
                    action = AuditAction.Update;
                    oldValues = new();
                    newValues = new();
                    foreach (var prop in entry.Properties)
                    {
                        if (!prop.IsModified || AuditIgnoredProperties.Contains(prop.Metadata.Name)) continue;
                        if (Equals(prop.OriginalValue, prop.CurrentValue)) continue;
                        oldValues[prop.Metadata.Name] = prop.OriginalValue;
                        newValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                    if (newValues.Count == 0 && hint is null) continue;
                }
            }

            list.Add(new PendingAudit(entry, new AuditLog
            {
                CreatedDate = now,
                UserId = _currentUser?.UserId,
                UserName = _currentUser?.UserName,
                Module = ModuleFor(entry.Entity),
                Action = hint?.Action ?? action,
                EntityName = entry.Metadata.ClrType.Name,
                RecordId = entry.State == EntityState.Added ? null : KeyOf(entry),
                Result = AuditResult.Success,
                OldValue = oldValues is { Count: > 0 } ? JsonSerializer.Serialize(oldValues, AuditJson) : null,
                NewValue = newValues is { Count: > 0 } ? JsonSerializer.Serialize(newValues, AuditJson) : null,
                Details = Truncate(hint?.Details, 1000),
                IpAddress = _currentUser?.IpAddress,
                UserAgent = _currentUser?.UserAgent
            }));
            _hints.Remove(entry.Entity);
        }
        return list;
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry, bool current) =>
        entry.Properties
            .Where(p => !AuditIgnoredProperties.Contains(p.Metadata.Name) && !p.Metadata.IsPrimaryKey())
            .ToDictionary(p => p.Metadata.Name, p => current ? p.CurrentValue : p.OriginalValue);

    private static string? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null ? null : string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue));
    }

    private static AuditModule ModuleFor(object entity) => entity switch
    {
        Customer => AuditModule.Customer,
        Lead => AuditModule.Lead,
        Opportunity => AuditModule.Opportunity,
        FollowUp => AuditModule.FollowUp,
        Activity => AuditModule.Activity,
        _ => AuditModule.System
    };

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    private sealed record AuditHint(AuditAction Action, string? Details);

    private sealed record PendingAudit(EntityEntry Entry, AuditLog Log);
}
