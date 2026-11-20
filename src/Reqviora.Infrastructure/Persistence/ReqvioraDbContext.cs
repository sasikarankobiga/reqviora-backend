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
        modelBuilder.Entity<ProjectDocument>().Property(document => document.FileName).HasMaxLength(255).IsRequired();
        modelBuilder.Entity<ProjectDocument>().Property(document => document.StoragePath).HasMaxLength(1000).IsRequired();
        modelBuilder.Entity<Requirement>().Property(requirement => requirement.Title).HasMaxLength(300).IsRequired();
        modelBuilder.Entity<Screen>().Property(screen => screen.Name).HasMaxLength(200).IsRequired();

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ProjectMember and ProjectDraft relationships are already configured
        // by DraftPersistenceConfiguration. Only add non-conflicting fields.
        modelBuilder.Entity<ProjectDraft>(entity =>
        {
            entity.Property(draft => draft.DraftJson).IsRequired();
            entity.Property(draft => draft.Version).IsRequired();
            entity.Property(draft => draft.ConcurrencyToken).IsRequired();
            entity.Property(draft => draft.UpdatedAtUtc).IsRequired();
        });

        // Rashmina - Task 4: requirement <-> feature traceability.
        modelBuilder.Entity<RequirementFeature>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => new { link.RequirementId, link.FeatureId }).IsUnique();
            entity.HasOne<Requirement>()
                .WithMany()
                .HasForeignKey(link => link.RequirementId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Feature>()
                .WithMany()
                .HasForeignKey(link => link.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Rashmina - Task 5: feature <-> screen relationship.
        modelBuilder.Entity<FeatureScreen>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => new { link.FeatureId, link.ScreenId }).IsUnique();
            entity.HasOne<Feature>()
                .WithMany()
                .HasForeignKey(link => link.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Screen>()
                .WithMany()
                .HasForeignKey(link => link.ScreenId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Rashmina - Task 6: version history for the legacy Blueprint entity.
        modelBuilder.Entity<BlueprintVersion>(entity =>
        {
            entity.HasKey(version => version.Id);
            entity.HasIndex(version => new { version.BlueprintId, version.VersionNumber }).IsUnique();
            entity.Property(version => version.VersionNumber).IsRequired();
            entity.Property(version => version.DefinitionJson).IsRequired();
            entity.Property(version => version.CreatedAtUtc).IsRequired();
            entity.Property(version => version.ChangeSummary).HasMaxLength(1000);
            entity.HasOne<Blueprint>()
                .WithMany()
                .HasForeignKey(version => version.BlueprintId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.OccurredAt });
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceId).HasMaxLength(200).IsRequired();
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => x.OccurredAt);