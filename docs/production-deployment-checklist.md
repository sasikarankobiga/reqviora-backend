# Production deployment checklist

1. Require green GitHub Actions for build, regression, contracts, Gitleaks, native PostgreSQL/SQL Server, backup/restore, Docker build, and Compose validation.
2. Apply provider-specific migrations in isolated staging first; verify backup and restore before production.
3. Inject JWT, database, SMTP, storage, PayHere, and Data Protection secrets through the deployment secret store only.
4. Keep billing disabled until PayHere sandbox callback, duplicate notification, refund, and reconciliation tests pass.
5. Run browser E2E against an isolated frontend/backend staging environment.
6. Obtain explicit release and production deployment approval. Do not test against shared Supabase or production data.
