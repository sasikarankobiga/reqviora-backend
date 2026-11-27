using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.SecondBrain;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authorization;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectExportService(
    ReqvioraDbContext db,
    Reqviora.Application.Common.Interfaces.IFileStorageService? storage = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<ExportRecord> GetOrCreateAsync(
        Guid projectId, Guid revisionId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireAsync(db, projectId, actorId, cancellationToken);
        var revision = await db.ApprovedRevisions.AsNoTracking().SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.Id == revisionId, cancellationToken)
            ?? throw new KeyNotFoundException("Approved revision was not found.");

        var started = _clock.GetUtcNow();
        var diagnostic = new ExportJobDiagnostic
        {
            ProjectId = projectId,
            ApprovedRevisionId = revisionId,
            CreatedAt = started,
            StartedAt = started
        };
        db.ExportJobDiagnostics.Add(diagnostic);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var existing = await FindAsync(revisionId, cancellationToken);
            if (existing is not null)
            {
                var verified = EnsureValid(existing, projectId, revisionId);
                await CompleteAsync(diagnostic, verified, "Verified", cancellationToken);
                return verified;
            }

            var snapshot = JsonSerializer.Deserialize<ApprovedProjectSnapshot>(revision.SnapshotJson, DraftBlueprint.Json)
                ?? throw new InvalidOperationException("The approved snapshot is unavailable.");
            if (snapshot.ProjectId != projectId || snapshot.RevisionId != revisionId)
                throw new InvalidDataException("The persisted snapshot identity is inconsistent.");

            var package = SecondBrainPackageBuilder.Build(snapshot);
            var record = new ExportRecord
            {
                ApprovedRevisionId = revisionId,
                GeneratorVersion = SecondBrainPackageBuilder.GeneratorVersion,
                CreatedBy = actorId,
                CreatedAt = _clock.GetUtcNow(),
                ManifestJson = package.ManifestJson,
                Archive = package.Archive,
                ArchiveSha256 = package.ArchiveSha256
            };
            EnsureValid(record, projectId, revisionId);
            if (storage is not null)
            {
                await using var archive = new MemoryStream(package.Archive, writable: false);
                record.StoragePath = await storage.SaveAsync(archive, $"{projectId}-{revisionId}.zip", cancellationToken);
            }
            db.ExportRecords.Add(record);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
            {
                db.Entry(record).State = EntityState.Detached;
                var winner = await FindAsync(revisionId, cancellationToken);
                if (winner is null) throw;
                await CompleteAsync(diagnostic, winner, "Verified", cancellationToken);
                return EnsureValid(winner, projectId, revisionId);
            }

            db.Entry(record).State = EntityState.Detached;
            await CompleteAsync(diagnostic, record, "Verified", cancellationToken);
            return record;
        }
        catch (Exception exception)
        {
            await FailAsync(diagnostic, exception, cancellationToken);
            throw;
        }
    }

    private Task<ExportRecord?> FindAsync(Guid revisionId, CancellationToken cancellationToken) =>
        db.ExportRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ApprovedRevisionId == revisionId &&
            x.GeneratorVersion == SecondBrainPackageBuilder.GeneratorVersion, cancellationToken);

    private static ExportRecord EnsureValid(ExportRecord record, Guid projectId, Guid revisionId)
    {
        if (!SecondBrainPackageBuilder.Verify(new(record.ManifestJson, record.Archive, record.ArchiveSha256), projectId, revisionId))
            throw new InvalidDataException("The export failed its manifest or SHA-256 checks.");

        return record;
    }

    private async Task CompleteAsync(ExportJobDiagnostic job, ExportRecord record, string integrity,
        CancellationToken cancellationToken)
    {
        var completed = _clock.GetUtcNow();
        job.ExportRecordId = record.Id;
        job.Status = "Succeeded";
        job.CompletedAt = completed;
        job.DurationMilliseconds = Math.Max(0, (long)(completed - job.StartedAt).TotalMilliseconds);
        job.IntegrityStatus = integrity;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FailAsync(ExportJobDiagnostic job, Exception exception, CancellationToken cancellationToken)
    {
        var completed = _clock.GetUtcNow();
        job.Status = "Failed";
        job.CompletedAt = completed;
        job.DurationMilliseconds = Math.Max(0, (long)(completed - job.StartedAt).TotalMilliseconds);
        (job.FailureCode, job.FailureCategory, job.IntegrityStatus) = exception switch
        {
            InvalidDataException => ("EXPORT_INTEGRITY_FAILED", "Integrity", "Failed"),
            IOException => ("EXPORT_STORAGE_FAILED", "Storage", "Unknown"),
            DbUpdateException => ("EXPORT_PERSISTENCE_FAILED", "Persistence", "Unknown"),
            _ => ("EXPORT_GENERATION_FAILED", "Generation", "Unknown")
        };
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { /* diagnostics must never replace the original export failure */ }
    }
}
