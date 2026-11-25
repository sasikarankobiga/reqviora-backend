# Persistent document intelligence

Project document uploads now persist their validated media type, byte length, SHA-256 digest, parsing state, warnings, and source-addressable text segments. The digest has a filtered unique index per project, so legacy rows without a digest remain valid while duplicate new uploads are rejected.

`POST /api/projects/{projectId}/documents/{documentId}/parse` claims a five-minute database-backed parsing lease with an optimistic concurrency token. A successful parse replaces the document's segments and completes the state change in one database transaction. Repeated calls return the stored result, and concurrent callers receive a conflict instead of running the same parse in parallel.

Parsing states are `pending`, `parsing`, `completed`, `requires_ocr`, and `failed`. Empty parser output is persisted as `requires_ocr`; this release does not add an OCR provider. Stored error codes are deliberately non-sensitive, and document metadata responses do not expose storage paths or content hashes.

Uploads pass through a configurable ClamAV threat-scanner abstraction before any file is written. Infection is always rejected. Scanner unavailability is retried with bounded settings and fails closed when `Storage:RequireAntivirus` is enabled; local non-production environments may explicitly allow an unavailable scanner. No uploaded bytes or malware signatures are logged.

Voice input requires explicit consent and an enabled AI provider. Audio is threat-scanned and stored privately before it is sent to the configured transcription provider. Successful Tamil or English transcripts and their ordered timestamp segments are persisted. Provider failures, invalid provider source metadata, and database failures trigger best-effort deletion of the newly stored audio.

The existing `POST /api/projects/{projectId}/input-intelligence/voice/transcribe` response remains a `TranscriptDraft`. Additive endpoints provide restart-safe lifecycle control:

- `GET /api/projects/{projectId}/input-intelligence/voice/transcripts/{transcriptId}` returns the stored draft and status.
- `PUT /api/projects/{projectId}/input-intelligence/voice/transcripts/{transcriptId}` edits a draft using `expectedRevision`.
- `POST /api/projects/{projectId}/input-intelligence/voice/transcripts/{transcriptId}/review` requires explicit acknowledgement and atomically makes the reviewed transcript immutable.

All transcript endpoints enforce project membership and write permissions. Storage paths are never included in API responses.

Forward-only migrations are provided for PostgreSQL and SQL Server. The PostgreSQL migrations enable row-level security on document and voice segment tables and revoke public and known client-role table access. Deployment scripts under `database/deployment` are generated artifacts for controlled review and execution; they must not be applied automatically to shared databases.
