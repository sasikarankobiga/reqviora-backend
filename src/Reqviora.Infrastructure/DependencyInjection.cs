
return services
    .AddScoped<IUserRepository, UserRepository>()
    .AddScoped<IProjectRepository, ProjectRepository>()
    .AddScoped<IRequirementReviewAccessPolicy, ProjectOwnerRequirementReviewAccessPolicy>()
    .AddScoped<IRequirementReviewStore>(_ =>
        new PostgresRequirementReviewStore(connectionString))
    .AddScoped<IUnitOfWork, UnitOfWork>()

    // Rashmina - Task 3: Project Draft Save / Resume
    .AddScoped<ProjectDraftService>()

    // Rashmina - Task 4: Requirement Traceability
    .AddScoped<RequirementFeatureService>()

    // Rashmina - Task 5: Feature and Screen Validation
    .AddScoped<FeatureScreenService>()

    // Rashmina - Task 5: Navigation Validation
    .AddScoped<NavigationEdgeService>()

    // Rashmina - Task 6: Blueprint Version History
    .AddScoped<BlueprintVersionService>()

    // Team Integration - Project Export
    .AddScoped<ProjectExportService>()

    // Authentication
    .AddScoped<ITokenService, JwtTokenService>()

    // Email
    .AddScoped<IEmailService, EmailService>()

    // File Storage
    .AddScoped<IFileStorageService, FileStorageService>();
