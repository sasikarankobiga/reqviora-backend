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
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Screen> Screens => Set<Screen>();
    public DbSet<User> Users => Set<User>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();
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
    public DbSet<ValidationFinding> ValidationFindings => Set<ValidationFinding>();
    public DbSet<ApprovedRevision> ApprovedRevisions => Set<ApprovedRevision>();
    public DbSet<ExportRecord> ExportRecords => Set<ExportRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        DraftPersistenceConfiguration.Configure(modelBuilder, Database.IsSqlServer());

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt });
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);
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
        });

        modelBuilder.Entity<Approval>().Property(approval => approval.Status).HasMaxLength(50).IsRequired();
        modelBuilder.Entity<Blueprint>().Property(blueprint => blueprint.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<Component>().Property(component => component.Type).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<Feature>().Property(feature => feature.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<NavigationRoute>().Property(route => route.Path).HasMaxLength(500).IsRequired();
        modelBuilder.Entity<ProjectDocument>().Property(document => document.FileName).HasMaxLength(255).IsRequired();
        modelBuilder.Entity<ProjectDocument>().Property(document => document.StoragePath).HasMaxLength(1000).IsRequired();
        modelBuilder.Entity<Requirement>().Property(requirement => requirement.Title).HasMaxLength(300).IsRequired();
        modelBuilder.Entity<Screen>().Property(screen => screen.Name).HasMaxLength(200).IsRequired();
    }
}
