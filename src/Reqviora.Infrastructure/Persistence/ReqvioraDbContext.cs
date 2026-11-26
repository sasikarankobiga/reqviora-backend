using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Persistence;

public class ReqvioraDbContext : DbContext
{
    public ReqvioraDbContext(DbContextOptions<ReqvioraDbContext> options) : base(options) { }

    protected ReqvioraDbContext(DbContextOptions options) : base(options) { }

    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Blueprint> Blueprints => Set<Blueprint>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<NavigationRoute> NavigationRoutes => Set<NavigationRoute>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<ProjectDocumentSegment> ProjectDocumentSegments => Set<ProjectDocumentSegment>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AccountActionToken> AccountActionTokens => Set<AccountActionToken>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Screen> Screens => Set<Screen>();
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AiJob> AiJobs => Set<AiJob>();
    public DbSet<VoiceTranscript> VoiceTranscripts => Set<VoiceTranscript>();
    public DbSet<VoiceTranscriptSegment> VoiceTranscriptSegments => Set<VoiceTranscriptSegment>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectDraft> ProjectDrafts => Set<ProjectDraft>();
    public DbSet<DesignDraftVersion> DesignDraftVersions => Set<DesignDraftVersion>();
    public DbSet<DraftRequirement> DraftRequirements => Set<DraftRequirement>();
    public DbSet<RequirementSourceEvidence> RequirementSourceEvidence => Set<RequirementSourceEvidence>();
    public DbSet<DraftFeature> DraftFeatures => Set<DraftFeature>();
    public DbSet<DraftFeatureRequirement> DraftFeatureRequirements => Set<DraftFeatureRequirement>();
    public DbSet<DraftScreen> DraftScreens => Set<DraftScreen>();
    public DbSet<DraftScreenFeature> DraftScreenFeatures => Set<DraftScreenFeature>();
    public DbSet<ScreenAction> ScreenActions => Set<ScreenAction>();
    public DbSet<NavigationEdge> NavigationEdges => Set<NavigationEdge>();
    public DbSet<ValidationRun> ValidationRuns => Set<ValidationRun>();
    public DbSet<ValidationTelemetry> ValidationTelemetry => Set<ValidationTelemetry>();
    public DbSet<PlatformSettingRevision> PlatformSettingRevisions => Set<PlatformSettingRevision>();
    public DbSet<ValidationFinding> ValidationFindings => Set<ValidationFinding>();
    public DbSet<ApprovedRevision> ApprovedRevisions => Set<ApprovedRevision>();
    public DbSet<ExportRecord> ExportRecords => Set<ExportRecord>();
    public DbSet<ExportJobDiagnostic> ExportJobDiagnostics => Set<ExportJobDiagnostic>();
    public DbSet<McpAccessGrant> McpAccessGrants => Set<McpAccessGrant>();
    public DbSet<SupportIncident> SupportIncidents => Set<SupportIncident>();
    public DbSet<SupportIncidentHistory> SupportIncidentHistory => Set<SupportIncidentHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        DraftPersistenceConfiguration.Configure(modelBuilder, Database.IsSqlServer());
        modelBuilder.Entity<PlatformSettingRevision>(entity =>
        {
            entity.HasKey(x => new { x.Key, x.Revision });
            entity.Property(x => x.Key).HasMaxLength(80);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<SupportIncident>(entity =>
        {
            entity.HasIndex(x => x.Reference).IsUnique();
            entity.HasIndex(x => new { x.Status, x.Priority, x.UpdatedAtUnixTimeMilliseconds });
            entity.HasIndex(x => x.AssignedAdminId);
            entity.Property(x => x.Reference).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.Property(x => x.ResolutionNotes).HasMaxLength(1000);
            entity.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedAdminId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<SupportIncidentHistory>(entity =>
        {
            entity.HasIndex(x => new { x.IncidentId, x.ChangedAtUnixTimeMilliseconds });
            entity.Property(x => x.Action).HasMaxLength(40).IsRequired();
            entity.Property(x => x.PreviousValue).HasMaxLength(100);
            entity.Property(x => x.NewValue).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasOne<SupportIncident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.EmailVerified).HasDefaultValue(false);
            entity.Property(user => user.SessionVersion).HasDefaultValue(0);
            entity.Property(user => user.IsDisabled).HasDefaultValue(false);
            entity.Property(user => user.DisabledReason).HasMaxLength(500);
            entity.Property(user => user.RecoveryCodeHashes).IsConcurrencyToken();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt });
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);
            entity.Property(token => token.RevokedAt).IsConcurrencyToken();
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountActionToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.Purpose, token.ExpiresAt });
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.TargetValue).HasMaxLength(320);
            entity.Property(token => token.ConsumedAt).IsConcurrencyToken();
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(role => role.Name).IsUnique();
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(project => project.OwnerId);
            entity.Property(project => project.Name).HasMaxLength(200).IsRequired();
            entity.Property(project => project.Description).HasMaxLength(4000);
            entity.Property(project => project.Industry).HasMaxLength(120);
            entity.Property(project => project.TargetAudience).HasMaxLength(500);
            entity.Property(project => project.ProjectType).HasMaxLength(120);
            entity.Property(project => project.PrimaryPlatform).HasMaxLength(50).IsRequired();
            entity.Property(project => project.TechnologyPreferences).HasMaxLength(2000);
            entity.Property(project => project.Constraints).HasMaxLength(4000);
            entity.Property(project => project.Integrations).HasMaxLength(4000);
        });

        modelBuilder.Entity<Approval>().Property(approval => approval.Status).HasMaxLength(50).IsRequired();
        modelBuilder.Entity<Blueprint>().Property(blueprint => blueprint.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<Component>().Property(component => component.Type).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<Feature>().Property(feature => feature.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<NavigationRoute>().Property(route => route.Path).HasMaxLength(500).IsRequired();
        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.HasIndex(document => new { document.ProjectId, document.Sha256 }).IsUnique()
                .HasFilter(Database.IsSqlServer() ? "[Sha256] IS NOT NULL" : "\"Sha256\" IS NOT NULL");
            entity.HasIndex(document => new { document.ProjectId, document.ParsingStatus });
            entity.Property(document => document.FileName).HasMaxLength(255).IsRequired();
            entity.Property(document => document.StoragePath).HasMaxLength(1000).IsRequired();
            entity.Property(document => document.MediaType).HasMaxLength(150).IsRequired();
            entity.Property(document => document.Sha256).HasMaxLength(64);
            entity.Property(document => document.ParsingStatus).HasMaxLength(30).HasDefaultValue("pending").IsRequired();
            entity.Property(document => document.ParseErrorCode).HasMaxLength(100);
            entity.Property(document => document.ParsingWarningsJson).HasDefaultValue("[]").IsRequired();
            entity.Property(document => document.ConcurrencyToken).IsConcurrencyToken();
        });
        modelBuilder.Entity<ProjectDocumentSegment>(entity =>
        {
            entity.HasIndex(segment => new { segment.ProjectDocumentId, segment.Sequence }).IsUnique();
            entity.Property(segment => segment.Text).IsRequired();
            entity.Property(segment => segment.SourceId).HasMaxLength(100).IsRequired();
            entity.Property(segment => segment.Locator).HasMaxLength(500).IsRequired();
            entity.HasOne<ProjectDocument>().WithMany().HasForeignKey(segment => segment.ProjectDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Requirement>().Property(requirement => requirement.Title).HasMaxLength(300).IsRequired();
        modelBuilder.Entity<Screen>().Property(screen => screen.Name).HasMaxLength(200).IsRequired();

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.OccurredAt });
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceId).HasMaxLength(200).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => x.OccurredAt);
            entity.Property(x => x.Method).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Path).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
        });
        modelBuilder.Entity<AiJob>(entity =>
        {
            entity.HasIndex(job => new { job.OwnerId, job.ProjectId, job.IdempotencyKey }).IsUnique();
            entity.HasIndex(job => new
            {
                job.State,
                job.AvailableAtUnixTimeMilliseconds,
                job.LeaseExpiresAtUnixTimeMilliseconds
            });
            entity.Property(job => job.Prompt).IsRequired();
            entity.Property(job => job.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(job => job.State).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(job => job.LeaseOwner).HasMaxLength(300);
            entity.Property(job => job.ErrorCode).HasMaxLength(100);
            entity.Property(job => job.ErrorMessage).HasMaxLength(500);
            entity.Property(job => job.ConcurrencyToken).IsConcurrencyToken();
            entity.HasOne<Project>().WithMany().HasForeignKey(job => job.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(job => job.OwnerId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<VoiceTranscript>(entity =>
        {
            entity.HasIndex(transcript => new { transcript.ProjectId, transcript.CreatedAt });
            entity.HasIndex(transcript => transcript.AudioFileId).IsUnique();
            entity.Property(transcript => transcript.AudioStoragePath).HasMaxLength(1000).IsRequired();
            entity.Property(transcript => transcript.MediaType).HasMaxLength(100).IsRequired();
            entity.Property(transcript => transcript.LanguageCode).HasMaxLength(10).IsRequired();
            entity.Property(transcript => transcript.OriginalText).IsRequired();
            entity.Property(transcript => transcript.EditableText).IsRequired();
            entity.Property(transcript => transcript.Status).HasMaxLength(30).HasDefaultValue("draft").IsRequired();
            entity.Property(transcript => transcript.ConcurrencyToken).IsConcurrencyToken();
            entity.HasOne<Project>().WithMany().HasForeignKey(transcript => transcript.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(transcript => transcript.ReviewedBy)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<VoiceTranscriptSegment>(entity =>
        {
            entity.HasIndex(segment => new { segment.VoiceTranscriptId, segment.Sequence }).IsUnique();
            entity.Property(segment => segment.Text).IsRequired();
            entity.HasOne<VoiceTranscript>().WithMany().HasForeignKey(segment => segment.VoiceTranscriptId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<McpAccessGrant>(entity =>
        {
            entity.HasIndex(grant => grant.SecretHash).IsUnique();
            entity.HasIndex(grant => new { grant.ProjectId, grant.CreatedAt });
            entity.HasIndex(grant => new { grant.RevokedAt, grant.ExpiresAt });
            entity.Property(grant => grant.SecretHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(grant => grant.RevocationReason).HasMaxLength(500);
            entity.Property(grant => grant.ConcurrencyToken).IsConcurrencyToken();
            entity.HasOne<Project>().WithMany().HasForeignKey(grant => grant.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApprovedRevision>().WithMany()
                .HasForeignKey(grant => new { grant.ApprovedRevisionId, grant.ProjectId })
                .HasPrincipalKey(revision => new { revision.Id, revision.ProjectId })
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(grant => grant.OwnerId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(grant => grant.RevokedBy)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
