ALTER TABLE SourceRevision ADD COLUMN SizeBytes INTEGER NOT NULL DEFAULT 0 CHECK(SizeBytes >= 0);
ALTER TABLE SourceRevision ADD COLUMN ErrorMessage TEXT;
CREATE INDEX IX_Source_Project_Added ON StudySource(ProjectId, IsArchived, CreatedAtUtc DESC, Id);
CREATE INDEX IX_SourceRevision_Status ON SourceRevision(Status);
