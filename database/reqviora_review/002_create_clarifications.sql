-- Apply after 001_create_review_store.sql using the private backend database role.
CREATE TABLE IF NOT EXISTS reqviora_review.clarifications (
    project_id UUID NOT NULL,
    requirement_id VARCHAR(100) NOT NULL,
    question_id UUID NOT NULL,
    question VARCHAR(2000) NOT NULL,
    answer VARCHAR(5000) NULL,
    revision INTEGER NOT NULL DEFAULT 1,
    answered_by UUID NULL,
    answered_at TIMESTAMPTZ NULL,
    PRIMARY KEY (project_id, requirement_id, question_id),
    FOREIGN KEY (project_id, requirement_id) REFERENCES reqviora_review.requirements(project_id, requirement_id) ON DELETE CASCADE,
    CHECK ((answer IS NULL AND revision = 1 AND answered_by IS NULL AND answered_at IS NULL)
        OR (answer IS NOT NULL AND revision = 2 AND answered_by IS NOT NULL AND answered_at IS NOT NULL))
);
REVOKE ALL ON TABLE reqviora_review.clarifications FROM PUBLIC;
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        REVOKE ALL ON TABLE reqviora_review.clarifications FROM anon;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        REVOKE ALL ON TABLE reqviora_review.clarifications FROM authenticated;
    END IF;
END $$;
