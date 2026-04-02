using Microsoft.Data.Sqlite;
using System.Text.Json;
using Argus.Core;

namespace Argus.Engine.Learning;

/// <summary>
/// Persists known-good behavior profiles as signatures.
/// DB location: C:\ProgramData\Argus\Engine\signatures.db
/// Also stores prevalence data (Sightings table).
/// </summary>
public sealed class SignatureDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

    public SignatureDatabase(string? dbPath = null)
    {
        dbPath ??= Path.Combine(ArgusConstants.DataRoot, "Engine", "signatures.db");
        if (dbPath != ":memory:")
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = ArgusConstants.SqlitePragmas;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Signatures (
                ProcessName TEXT PRIMARY KEY,
                ProfileJson TEXT NOT NULL,
                UpdatedAt   TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Prevalence (
                ProcessName TEXT    PRIMARY KEY,
                ImagePath   TEXT    NOT NULL,
                SightCount  INTEGER NOT NULL DEFAULT 0,
                FirstSeen   TEXT    NOT NULL,
                LastSeen    TEXT    NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void UpsertSignature(BehaviorProfile profile)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Signatures (ProcessName, ProfileJson, UpdatedAt)
            VALUES ($name, $json, $now)
            ON CONFLICT(ProcessName) DO UPDATE SET ProfileJson=$json, UpdatedAt=$now
            """;
        cmd.Parameters.AddWithValue("$name", profile.ProcessName);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(profile));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public BehaviorProfile? GetSignature(string processName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT ProfileJson FROM Signatures WHERE ProcessName=$name LIMIT 1";
        cmd.Parameters.AddWithValue("$name", processName);
        var json = cmd.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<BehaviorProfile>(json);
    }

    // ── Prevalence tracking ─────────────────────────────────────────────────

    public void IncrementSighting(string processName, string imagePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Prevalence (ProcessName, ImagePath, SightCount, FirstSeen, LastSeen)
            VALUES ($name, $path, 1, $now, $now)
            ON CONFLICT(ProcessName) DO UPDATE SET
                SightCount = SightCount + 1,
                LastSeen = $now
            """;
        cmd.Parameters.AddWithValue("$name", processName);
        cmd.Parameters.AddWithValue("$path", imagePath);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public int GetSightingCount(string processName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT SightCount FROM Prevalence WHERE ProcessName=$name";
        cmd.Parameters.AddWithValue("$name", processName);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public void Dispose() => _conn.Dispose();
}
