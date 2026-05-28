using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Local photo roll paths under persistent data:
/// UrbexSimulator/PhotoRoll/Sessions/{matchId}/Players/{playerFolder}/.
/// </summary>
public static class PhotoRollSession
{
    private const string RootFolderName = "UrbexSimulator";
    private const string PhotoRollName = "PhotoRoll";
    private const string SessionsName = "Sessions";
    private const string PlayersName = "Players";
    private const string ManifestFileName = "manifest.jsonl";

    /// <summary>Server-only match folder id before clients replicate it.</summary>
    private static string s_serverMatchId;

    /// <summary>Client copy of match id from PlayerLocalControls SyncVar.</summary>
    private static string s_clientResolvedMatchId;

    /// <summary>Active match folder id (replicated or solo_local).</summary>
    public static string ActiveMatchId => string.IsNullOrEmpty(s_clientResolvedMatchId) ? "solo_local" : s_clientResolvedMatchId;

    /// <summary>Clears match ids when returning to the lobby.</summary>
    public static void ResetForLobby()
    {
        s_serverMatchId = null;
        s_clientResolvedMatchId = null;
    }

    /// <summary>Server only: assigns a new match id before player SyncVars replicate.</summary>
    public static void EnsureServerMatchId()
    {
        if (!string.IsNullOrEmpty(s_serverMatchId))
            return;
        s_serverMatchId = Guid.NewGuid().ToString("N");
    }

    /// <summary>Server-side match id before replication (may be null).</summary>
    public static string PeekServerMatchId() => s_serverMatchId;

    /// <summary>Applies the match id replicated from the server.</summary>
    public static void ApplyReplicatedMatchId(string matchId) =>
        s_clientResolvedMatchId = string.IsNullOrEmpty(matchId) ? null : matchId;

    /// <summary>Root PhotoRoll directory under persistent data.</summary>
    public static string GetRollRootAbsolute()
    {
        return Path.Combine(Application.persistentDataPath, RootFolderName, PhotoRollName);
    }

    /// <summary>Players root for a session match id.</summary>
    public static string GetSessionPlayersRootAbsolute(string matchId)
    {
        return Path.Combine(GetRollRootAbsolute(), SessionsName, matchId, PlayersName);
    }

    /// <summary>Per-player photo directory for a match.</summary>
    public static string GetPlayerPhotoDirectoryAbsolute(string matchId, string playerFolder)
    {
        string safePlayer = SanitizeFolderSegment(playerFolder);
        return Path.Combine(GetSessionPlayersRootAbsolute(matchId), safePlayer);
    }

    /// <summary>Appends one manifest JSON line and returns the manifest file path.</summary>
    public static string AppendManifestLine(string matchId, string playerFolder, string relativeImagePath, int width, int height)
    {
        string dir = GetPlayerPhotoDirectoryAbsolute(matchId, playerFolder);
        Directory.CreateDirectory(dir);

        string manifestPath = Path.Combine(dir, ManifestFileName);
        // One JSON object per line for append-only manifest parsing.
        string line = "{" +
                      "\"utc\":\"" + DateTime.UtcNow.ToString("o") + "\"," +
                      "\"file\":\"" + EscapeJson(relativeImagePath) + "\"," +
                      "\"w\":" + width + "," +
                      "\"h\":" + height +
                      "}\n";
        File.AppendAllText(manifestPath, line, Encoding.UTF8);
        return manifestPath;
    }

    /// <summary>Strips invalid path characters and caps folder name length.</summary>
    private static string SanitizeFolderSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "unknown";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 64 ? name.Substring(0, 64) : name;
    }

    /// <summary>Escapes backslashes and quotes for manifest JSON lines.</summary>
    private static string EscapeJson(string s)
    {
        if (s == null)
            return string.Empty;
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
