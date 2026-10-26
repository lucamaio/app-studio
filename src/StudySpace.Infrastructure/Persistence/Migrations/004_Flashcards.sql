ALTER TABLE FlashCard ADD COLUMN SearchText TEXT NOT NULL DEFAULT '';
ALTER TABLE FlashcardCitation ADD COLUMN Quote TEXT NOT NULL DEFAULT '';
CREATE TABLE GenerationRun (Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, ProviderKey TEXT NOT NULL, ProviderName TEXT NOT NULL, IsMock INTEGER NOT NULL CHECK(IsMock IN (0,1)), RequestedCount INTEGER NOT NULL CHECK(RequestedCount BETWEEN 1 AND 30), CreatedAtUtc TEXT NOT NULL, PromptVersion TEXT NOT NULL, SelectionHash TEXT NOT NULL, UNIQUE(ProjectId,Id), FOREIGN KEY(ProjectId) REFERENCES StudyProject(Id) ON DELETE CASCADE);
CREATE TABLE GenerationInputChunk (ProjectId TEXT NOT NULL, GenerationRunId TEXT NOT NULL, ChunkId TEXT NOT NULL, PRIMARY KEY(ProjectId,GenerationRunId,ChunkId), FOREIGN KEY(ProjectId,GenerationRunId) REFERENCES GenerationRun(ProjectId,Id) ON DELETE CASCADE, FOREIGN KEY(ProjectId,ChunkId) REFERENCES SourceChunk(ProjectId,Id));
CREATE TABLE FlashcardDetails (ProjectId TEXT NOT NULL, FlashcardId TEXT NOT NULL, SourceId TEXT, GenerationRunId TEXT, IsUserEdited INTEGER NOT NULL DEFAULT 0 CHECK(IsUserEdited IN (0,1)), PRIMARY KEY(ProjectId,FlashcardId), FOREIGN KEY(ProjectId,FlashcardId) REFERENCES FlashCard(ProjectId,Id) ON DELETE CASCADE, FOREIGN KEY(ProjectId,SourceId) REFERENCES StudySource(ProjectId,Id), FOREIGN KEY(ProjectId,GenerationRunId) REFERENCES GenerationRun(ProjectId,Id));
CREATE INDEX IX_Flashcard_Project_Modified ON FlashCard(ProjectId,UpdatedAtUtc DESC,Id);
CREATE INDEX IX_Flashcard_Project_Favorite ON FlashCard(ProjectId,IsFavorite,UpdatedAtUtc DESC);
CREATE INDEX IX_FlashcardTag_Tag ON FlashcardTag(ProjectId,TagId);
CREATE INDEX IX_FlashcardDetails_Generation ON FlashcardDetails(ProjectId,GenerationRunId);
