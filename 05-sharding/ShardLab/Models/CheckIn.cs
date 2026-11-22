namespace ShardLab.Models;

// Record equality matters: the gather step de-duplicates on it. Id makes every instance unique.
public sealed record CheckIn(long Id, long UserId, long VenueId, DateTimeOffset At);
