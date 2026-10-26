CREATE INDEX IX_QuizAttempt_Completed ON QuizAttempt(ProjectId,Status,CompletedAtUtc DESC,Id DESC);
