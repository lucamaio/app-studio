namespace StudySpace.Application.Common;

public sealed class StudyValidationException(string message) : Exception(message);
public sealed class StorageUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
public sealed class ProjectConflictException() : Exception("Il progetto è stato modificato o eliminato. Ricaricalo prima di riprovare.");
public sealed class ProjectNotFoundException() : Exception("Questo progetto non è più disponibile. Torna all'elenco dei progetti.");

