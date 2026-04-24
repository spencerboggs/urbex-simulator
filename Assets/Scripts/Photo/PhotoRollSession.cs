using System;
using System.IO;
using System.Text;
using UnityEngine;

// Local photo storage under persistent data
// UrbexSimulator / PhotoRoll / Sessions / matchId / Players / playerFolder /
// Kept ready for session gallery and optional sharing later
public static class PhotoRollSession
{
    private const string RootFolderName = "UrbexSimulator";
    private const string PhotoRollName = "PhotoRoll";
    private const string SessionsName = "Sessions";
    private const string PlayersName = "Players";
    private const string ManifestFileName = "manifest.jsonl";

    private static string s_serverMatchId;
    private static string s_clientResolvedMatchId;

    // Server-generated match folder name replicated to clients
    // Falls back to solo_local until a value arrives
    public static string ActiveMatchId => string.IsNullOrEmpty(s_clientResolvedMatchId) ? "solo_local" : s_clientResolvedMatchId;

    public static void ResetForLobby()
    {
        s_serverMatchId = null;
        s_clientResolvedMatchId = null;
    }

    // Server only
    // Call once per match before player SyncVars replicate
    public static void EnsureServerMatchId()
    {
        if (!string.IsNullOrEmpty(s_serverMatchId))
            return;
        s_serverMatchId = Guid.NewGuid().ToString("N");
    }

    public static string PeekServerMatchId() => s_serverMatchId;

    public static void ApplyReplicatedMatchId(string matchId) =>
        s_clientResolvedMatchId = string.IsNullOrEmpty(matchId) ? null : matchId;

    public static string GetRollRootAbsolute()
    {
        return Path.Combine(Application.persistentDataPath, RootFolderName, PhotoRollName);
    }

    public static string GetSessionPlayersRootAbsolute(string matchId)
    {
        return Path.Combine(GetRollRootAbsolute(), SessionsName, matchId, PlayersName);
    }

    public static string GetPlayerPhotoDirectoryAbsolute(string matchId, string playerFolder)
    {
        string safePlayer = SanitizeFolderSegment(playerFolder);
        return Path.Combine(GetSessionPlayersRootAbsolute(matchId), safePlayer);
    }

    public static string AppendManifestLine(string matchId, string playerFolder, string relativeImagePath, int width, int height)
    {
        string dir = GetPlayerPhotoDirectoryAbsolute(matchId, playerFolder);
        Directory.CreateDirectory(dir);

        string manifestPath = Path.Combine(dir, ManifestFileName);
        string line = "{" +
                      "\"utc\":\"" + DateTime.UtcNow.ToString("o") + "\"," +
                      "\"file\":\"" + EscapeJson(relativeImagePath) + "\"," +
                      "\"w\":" + width + "," +
                      "\"h\":" + height +
                      "}\n";
        File.AppendAllText(manifestPath, line, Encoding.UTF8);
        return manifestPath;
    }

    private static string SanitizeFolderSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "unknown";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 64 ? name.Substring(0, 64) : name;
    }

    private static string EscapeJson(string s)
    {
        if (s == null)
            return string.Empty;
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
