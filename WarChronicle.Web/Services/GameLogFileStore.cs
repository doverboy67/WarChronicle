using System.Text;

namespace WarChronicle.Web.Services;

public sealed class GameLogFileStore(IWebHostEnvironment environment)
{
    private readonly string _logDirectory = Path.Combine(environment.ContentRootPath, "GameLogs");

    public async Task<string> SaveCompletedGameAsync(string json, DateTime endedUtc, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_logDirectory);

        // For the current local playtest build, the app server is the player's PC,
        // so local server time is also the desired playtest filename time.
        var endedLocal = endedUtc.ToLocalTime();
        var baseName = $"WC_GameLog_{endedLocal:yyyy-MM-dd_HH-mm-ss}";
        var filename = baseName + ".json";
        var path = Path.Combine(_logDirectory, filename);

        // Never overwrite a completed playtest. A same-second collision is rare,
        // but retain both logs if it happens.
        var suffix = 2;
        while (File.Exists(path))
        {
            filename = $"{baseName}_{suffix}.json";
            path = Path.Combine(_logDirectory, filename);
            suffix++;
        }

        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        return filename;
    }
}
