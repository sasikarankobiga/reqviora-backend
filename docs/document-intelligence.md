# Persistent document intelligence

Project document uploads now persist their validated media type, byte length, SHA-256 digest, parsing state, warnings, and source-addressable text segments. The digest has a filtered unique index per project, so legacy rows without a digest remain valid while duplicate new uploads are rejected.

`POST /api/projects/{projectId}/documents/{documentId}/parse` claims a five-minute database-backed parsing lease with an optimistic concurrency token. A successful parse replaces the document's segments and completes the state change in one database transaction. Repeated calls return the stored result, and concurrent callers receive a conflict instead of running the same parse in parallel.

Parsing states are `pending`, `parsing`, `completed`, `requires_ocr`, and `failed`. Empty parser output is persisted as `requires_ocr`; this release does not add an OCR provider. Stored error codes are deliberately non-sensitive, and document metadata responses do not expose storage paths or content hashes.

Forward-only migrations are provided for PostgreSQL and SQL Server. The PostgreSQL migration enables row-level security on `ProjectDocumentSegments` and revokes public and known client-role table access. Deployment scripts under `database/deployment` are generated artifacts for controlled review and execution; they must not be applied automatically to shared databases.
