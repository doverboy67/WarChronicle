namespace WarChronicle.Web.Services;

/// <summary>
/// GitLab Pages runs entirely in the player's browser and has no writable
/// application-server filesystem. Completed logs are still archived in
/// browser localStorage and remain available through Download Game Log.
/// A future hosted backend can replace this implementation without changing
/// the game-log schema.
/// </summary>
public sealed class GameLogFileStore
{
    public Task<string> SaveCompletedGameAsync(string json, DateTime endedUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}
