
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Persistence;

public sealed class ReqvioraDbContext(
    DbContextOptions<ReqvioraDbContext> options) : DbContext(options)
{
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

    // Rashmina - Database Entities
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectDraft> ProjectDrafts => Set<ProjectDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Existing User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
        });

        // Existing RefreshToken Configuration
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

        // Existing Role Configuration
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(role => role.Name).IsUnique();
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
        });

        // Project Configuration
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(project => project.OwnerId);

            entity.Property(project => project.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(project => project.Description)
                .HasMaxLength(4000);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(project => project.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(project => project.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Workspace Configuration
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.HasKey(workspace => workspace.Id);

            entity.Property(workspace => workspace.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(workspace => workspace.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Project Member Configuration
        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(member => member.Id);

            entity.HasIndex(member => new { member.ProjectId, member.UserId })
                .IsUnique();

            entity.Property(member => member.Role)
                .HasMaxLength(30)
                .IsRequired();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(member => member.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Project Draft Configuration - Rashmina Task 3
        modelBuilder.Entity<ProjectDraft>(entity =>
        {
            entity.HasKey(draft => draft.Id);

            // One current draft per project
            entity.HasIndex(draft => draft.ProjectId)
                .IsUnique();

            entity.Property(draft => draft.DraftJson)
                .IsRequired();

            entity.Property(draft => draft.Version)
                .IsRequired();

            entity.Property(draft => draft.ConcurrencyToken)
                .IsConcurrencyToken()
                .IsRequired();

            entity.Property(draft => draft.UpdatedAtUtc)
                .IsRequired();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(draft => draft.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Existing Team Configurations
        modelBuilder.Entity<Approval>()
            .Property(approval => approval.Status)
            .HasMaxLength(50)
            .IsRequired();

        modelBuilder.Entity<Blueprint>()
            .Property(blueprint => blueprint.Name)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<Component>()
            .Property(component => component.Type)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Feature>()
            .Property(feature => feature.Name)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<NavigationRoute>()
            .Property(route => route.Path)
            .HasMaxLength(500)
            .IsRequired();

        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.Property(document => document.FileName)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(document => document.StoragePath)
                .HasMaxLength(1000)
                .IsRequired();
        });

        modelBuilder.Entity<Requirement>()
            .Property(requirement => requirement.Title)
            .HasMaxLength(300)
            .IsRequired();

        modelBuilder.Entity<Screen>()
            .Property(screen => screen.Name)
            .HasMaxLength(200)
            .IsRequired();
    }
}
