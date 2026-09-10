using IOTSnap.Hmi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IOTSnap.Hmi.Data;

public sealed class HmiDbContext(DbContextOptions<HmiDbContext> options) : DbContext(options)
{
    public DbSet<LocalUser> LocalUsers => Set<LocalUser>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<OpcUaConnectionProfile> OpcUaConnectionProfiles => Set<OpcUaConnectionProfile>();
    public DbSet<OpcUaNodeMapping> OpcUaNodeMappings => Set<OpcUaNodeMapping>();
    public DbSet<OpcUaAlarmState> OpcUaAlarmStates => Set<OpcUaAlarmState>();
    public DbSet<HmiScreen> HmiScreens => Set<HmiScreen>();
    public DbSet<HmiWidget> HmiWidgets => Set<HmiWidget>();
    public DbSet<HmiWidgetBinding> HmiWidgetBindings => Set<HmiWidgetBinding>();
    public DbSet<HmiScreenPublication> HmiScreenPublications => Set<HmiScreenPublication>();
    public DbSet<OperatorCommand> OperatorCommands => Set<OperatorCommand>();
    public DbSet<OperatorAuditEntry> OperatorAuditEntries => Set<OperatorAuditEntry>();
    public DbSet<OpcUaTrendSample> OpcUaTrendSamples => Set<OpcUaTrendSample>();
    public DbSet<OpcUaAlarmTransition> OpcUaAlarmTransitions => Set<OpcUaAlarmTransition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LocalUser>(entity =>
        {
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(64);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.Property(x => x.DisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(128);
            entity.Property(x => x.Category).HasMaxLength(64);
        });

        modelBuilder.Entity<OpcUaConnectionProfile>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.EndpointUrl).HasMaxLength(512);
            entity.Property(x => x.SecurityPolicy).HasMaxLength(64);
            entity.Property(x => x.SecurityMode).HasMaxLength(64);
            entity.Property(x => x.AuthenticationMode).HasMaxLength(32);
            entity.Property(x => x.Username).HasMaxLength(128);
            entity.Property(x => x.ProtectedPassword).HasMaxLength(2048);
        });

        modelBuilder.Entity<OpcUaNodeMapping>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(128);
            entity.Property(x => x.NodeId).HasMaxLength(256);
            entity.Property(x => x.DataType).HasMaxLength(32);
            entity.Property(x => x.Area).HasMaxLength(64);
        });

        modelBuilder.Entity<OpcUaAlarmState>(entity =>
        {
            entity.HasIndex(x => x.NodeId).IsUnique();
            entity.Property(x => x.NodeId).HasMaxLength(256);
            entity.Property(x => x.DisplayName).HasMaxLength(128);
            entity.Property(x => x.Area).HasMaxLength(64);
            entity.Property(x => x.DataType).HasMaxLength(32);
            entity.Property(x => x.StatusCode).HasMaxLength(64);
            entity.Property(x => x.AlarmText).HasMaxLength(256);
            entity.Property(x => x.Severity).HasDefaultValue(500);
            entity.Property(x => x.AcknowledgedBy).HasMaxLength(128);
        });

        modelBuilder.Entity<HmiScreen>(entity =>
        {
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Slug).HasMaxLength(64);
        });

        modelBuilder.Entity<HmiWidget>(entity =>
        {
            entity.HasIndex(x => new { x.HmiScreenId, x.Key }).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(64);
            entity.Property(x => x.WidgetType).HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(128);
            entity.Property(x => x.PropertiesJson).HasMaxLength(4000);
        });

        modelBuilder.Entity<HmiWidgetBinding>(entity =>
        {
            entity.HasIndex(x => new { x.HmiWidgetId, x.BindingRole }).IsUnique();
            entity.Property(x => x.BindingRole).HasMaxLength(64);
            entity.Property(x => x.SourceType).HasMaxLength(32);
            entity.Property(x => x.SourceKey).HasMaxLength(256);
            entity.Property(x => x.MinRole).HasMaxLength(32);
        });

        modelBuilder.Entity<HmiScreenPublication>(entity =>
        {
            entity.HasIndex(x => new { x.HmiScreenId, x.PublishedUtc });
            entity.Property(x => x.Slug).HasMaxLength(64);
            entity.Property(x => x.SnapshotJson).HasMaxLength(20000);
            entity.Property(x => x.PublishedBy).HasMaxLength(64);
        });

        modelBuilder.Entity<OperatorCommand>(entity =>
        {
            entity.HasIndex(x => x.CommandId).IsUnique();
            entity.HasIndex(x => new { x.ActorUsername, x.IdempotencyKey }).IsUnique();
            entity.Property(x => x.CommandId).HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.ActorUsername).HasMaxLength(64);
            entity.Property(x => x.ActorRole).HasMaxLength(32);
            entity.Property(x => x.ScreenSlug).HasMaxLength(64);
            entity.Property(x => x.WidgetKey).HasMaxLength(64);
            entity.Property(x => x.BindingRole).HasMaxLength(64);
            entity.Property(x => x.NodeId).HasMaxLength(256);
            entity.Property(x => x.RequestedValue).HasMaxLength(256);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.OpcUaResponse).HasMaxLength(512);
            entity.Property(x => x.ObservedValue).HasMaxLength(256);
        });

        modelBuilder.Entity<OperatorAuditEntry>(entity =>
        {
            entity.HasIndex(x => x.OccurredUtc);
            entity.Property(x => x.ActorUsername).HasMaxLength(64);
            entity.Property(x => x.ActorRole).HasMaxLength(32);
            entity.Property(x => x.ActionType).HasMaxLength(64);
            entity.Property(x => x.Resource).HasMaxLength(256);
            entity.Property(x => x.Result).HasMaxLength(32);
            entity.Property(x => x.CorrelationId).HasMaxLength(64);
            entity.Property(x => x.Detail).HasMaxLength(512);
        });

        modelBuilder.Entity<OpcUaTrendSample>(entity =>
        {
            entity.HasIndex(x => new { x.NodeId, x.SampledUtc });
            entity.Property(x => x.NodeId).HasMaxLength(256);
            entity.Property(x => x.ValueText).HasMaxLength(256);
            entity.Property(x => x.StatusCode).HasMaxLength(64);
        });

        modelBuilder.Entity<OpcUaAlarmTransition>(entity =>
        {
            entity.HasIndex(x => new { x.NodeId, x.OccurredUtc });
            entity.Property(x => x.NodeId).HasMaxLength(256);
            entity.Property(x => x.Transition).HasMaxLength(32);
            entity.Property(x => x.ActorUsername).HasMaxLength(64);
            entity.Property(x => x.Detail).HasMaxLength(256);
        });
    }
}
