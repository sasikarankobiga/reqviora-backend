-- Apply after 001_create_review_store.sqlserver.sql.
IF OBJECT_ID(N'reqviora_review.clarifications', N'U') IS NULL
BEGIN
    CREATE TABLE reqviora_review.clarifications (
        project_id UNIQUEIDENTIFIER NOT NULL,
        requirement_id VARCHAR(100) NOT NULL,
        question_id UNIQUEIDENTIFIER NOT NULL,
        question NVARCHAR(2000) NOT NULL,
        answer NVARCHAR(MAX) NULL,
        revision INT NOT NULL DEFAULT 1,
        answered_by UNIQUEIDENTIFIER NULL,
        answered_at DATETIMEOFFSET NULL,
        PRIMARY KEY (project_id, requirement_id, question_id),
        FOREIGN KEY (project_id, requirement_id) REFERENCES reqviora_review.requirements(project_id, requirement_id) ON DELETE CASCADE,
        CHECK ((answer IS NULL AND revision = 1 AND answered_by IS NULL AND answered_at IS NULL)
            OR (answer IS NOT NULL AND revision = 2 AND answered_by IS NOT NULL AND answered_at IS NOT NULL))
    );
END;
GO
