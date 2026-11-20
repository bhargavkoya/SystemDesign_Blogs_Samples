namespace StampedeLab.Data;

public abstract class DatabaseException(string message) : Exception(message);

/// <summary>The query waited too long for a free slot. This is what overload looks like to a caller.</summary>
public sealed class DatabaseOverloadedException()
    : DatabaseException("Database overloaded: no query slot became free in time.");

/// <summary>The query reached the database and the database could not answer.</summary>
public sealed class DatabaseUnavailableException()
    : DatabaseException("Database unavailable.");
