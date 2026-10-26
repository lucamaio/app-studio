ALTER TABLE StudyProject ADD COLUMN SearchText TEXT NOT NULL DEFAULT '';
CREATE INDEX IX_Project_Favorite_Modified ON StudyProject(IsFavorite, UpdatedAtUtc DESC, Id);
CREATE INDEX IX_Tag_Normalized ON Tag(NormalizedName, ProjectId);
CREATE INDEX IX_ProjectTag_Tag ON ProjectTag(ProjectId, TagId);

