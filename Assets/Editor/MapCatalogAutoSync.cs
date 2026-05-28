using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Keeps the MapCatalog asset and EditorBuildSettings in sync with the contents
// of Assets/Scenes/Gameplay/. Runs automatically whenever a .unity file under
// Assets/Scenes/ is imported, moved, or deleted, and is also available from the
// Tools menu for a manual refresh.
//
// Behaviour:
//   - Discovers every .unity file under Assets/Scenes/Gameplay/ (recursive).
//   - Updates Assets/Resources/MapCatalog.asset (creates the Resources folder
//     and asset if either is missing) with one MapEntry per discovered scene.
//   - Preserves any user-edited displayName by matching on sceneName. New
//     scenes get displayName = sceneName (designers can edit it in the asset).
//   - Updates EditorBuildSettings.scenes: keeps non-gameplay entries as-is
//     (preserving their GUIDs and enabled flags), drops gameplay entries whose
//     file no longer exists, and adds new gameplay scenes as enabled. Existing
//     gameplay entries keep their enabled flag.
//
// Gameplay scenes have to be in EditorBuildSettings or FishNet/Unity can't load
// them by name in a build - this auto-sync ensures designers never forget that
// step when adding a new map.
public sealed class MapCatalogAutoSync : AssetPostprocessor
{
    private const string GameplayScenesRoot = "Assets/Scenes/Gameplay";
    private const string ResourcesFolder = "Assets/Resources";
    private const string MapCatalogAssetPath = ResourcesFolder + "/MapCatalog.asset";

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (AnyScene(importedAssets) || AnyScene(deletedAssets) || AnyScene(movedAssets) || AnyScene(movedFromAssetPaths))
            Resync();
    }

    [MenuItem("Tools/Urbex/Refresh Map Catalog")]
    public static void RefreshFromMenu()
    {
        Resync();
        Debug.Log("[MapCatalogAutoSync] Manual refresh complete.");
    }

    private static bool AnyScene(string[] paths)
    {
        if (paths == null) return false;
        for (int i = 0; i < paths.Length; i++)
        {
            if (!string.IsNullOrEmpty(paths[i]) &&
                paths[i].EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase) &&
                paths[i].Replace('\\', '/').StartsWith("Assets/Scenes/", System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void Resync()
    {
        List<string> gameplayScenePaths = DiscoverGameplayScenes();

        try
        {
            AssetDatabase.StartAssetEditing();
            SyncBuildSettings(gameplayScenePaths);
            SyncMapCatalog(gameplayScenePaths);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            MapCatalog.InvalidateCache();
        }
    }

    private static List<string> DiscoverGameplayScenes()
    {
        List<string> result = new List<string>();
        if (!Directory.Exists(GameplayScenesRoot))
            return result;

        string[] paths = Directory.GetFiles(GameplayScenesRoot, "*.unity", SearchOption.AllDirectories);
        for (int i = 0; i < paths.Length; i++)
            result.Add(paths[i].Replace('\\', '/'));

        result.Sort(System.StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static void SyncBuildSettings(List<string> gameplayScenePaths)
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];

        // Index existing entries by normalized path so we can preserve enabled flags
        // and skip duplicating anything already registered.
        Dictionary<string, EditorBuildSettingsScene> existingByPath = new Dictionary<string, EditorBuildSettingsScene>(System.StringComparer.OrdinalIgnoreCase);
        foreach (EditorBuildSettingsScene s in current)
        {
            if (s == null || string.IsNullOrEmpty(s.path)) continue;
            existingByPath[s.path.Replace('\\', '/')] = s;
        }

        List<EditorBuildSettingsScene> next = new List<EditorBuildSettingsScene>(current.Length + gameplayScenePaths.Count);

        // Keep every non-gameplay scene exactly as it was - we never want to touch
        // MainMenu, Lobby, Testing scenes, or anything the user has manually added.
        foreach (EditorBuildSettingsScene s in current)
        {
            if (s == null || string.IsNullOrEmpty(s.path)) continue;
            string normalized = s.path.Replace('\\', '/');
            if (!normalized.StartsWith(GameplayScenesRoot, System.StringComparison.OrdinalIgnoreCase))
                next.Add(s);
        }

        // Re-add (or freshly add) every discovered gameplay scene. Preserve the
        // existing entry - and therefore its enabled flag and GUID - when present.
        HashSet<string> alreadyAdded = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string path in gameplayScenePaths)
        {
            if (alreadyAdded.Contains(path)) continue;
            alreadyAdded.Add(path);

            if (existingByPath.TryGetValue(path, out EditorBuildSettingsScene existing))
                next.Add(existing);
            else
                next.Add(new EditorBuildSettingsScene(path, true));
        }

        // Only write back if the list actually changed - avoids spurious asset
        // changes on every script reload.
        if (!ScenesEqual(current, next))
            EditorBuildSettings.scenes = next.ToArray();
    }

    private static bool ScenesEqual(EditorBuildSettingsScene[] a, List<EditorBuildSettingsScene> b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a.Length != b.Count) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == null || b[i] == null) return false;
            if (!string.Equals(a[i].path, b[i].path, System.StringComparison.OrdinalIgnoreCase)) return false;
            if (a[i].enabled != b[i].enabled) return false;
        }
        return true;
    }

    private static void SyncMapCatalog(List<string> gameplayScenePaths)
    {
        EnsureFolderExists(ResourcesFolder);

        MapCatalog catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(MapCatalogAssetPath);
        bool isNew = false;
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<MapCatalog>();
            AssetDatabase.CreateAsset(catalog, MapCatalogAssetPath);
            isNew = true;
        }

        // Preserve user-edited display names by sceneName.
        Dictionary<string, string> existingDisplayBySceneName = new Dictionary<string, string>(System.StringComparer.Ordinal);
        if (catalog.Maps != null)
        {
            for (int i = 0; i < catalog.Maps.Length; i++)
            {
                MapCatalog.MapEntry entry = catalog.Maps[i];
                if (entry == null || string.IsNullOrEmpty(entry.sceneName)) continue;
                if (!string.IsNullOrEmpty(entry.displayName))
                    existingDisplayBySceneName[entry.sceneName] = entry.displayName;
            }
        }

        MapCatalog.MapEntry[] next = new MapCatalog.MapEntry[gameplayScenePaths.Count];
        for (int i = 0; i < gameplayScenePaths.Count; i++)
        {
            string sceneName = Path.GetFileNameWithoutExtension(gameplayScenePaths[i]);
            string displayName = existingDisplayBySceneName.TryGetValue(sceneName, out string preserved)
                ? preserved
                : sceneName;

            next[i] = new MapCatalog.MapEntry
            {
                sceneName = sceneName,
                displayName = displayName,
            };
        }

        // Skip the write if nothing actually changed - keeps the inspector clean
        // and avoids touching the asset on every script recompile.
        if (!isNew && MapsEqual(catalog.Maps, next))
            return;

        catalog.SetMaps(next);
        EditorUtility.SetDirty(catalog);
    }

    private static bool MapsEqual(MapCatalog.MapEntry[] a, MapCatalog.MapEntry[] b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == null || b[i] == null) return false;
            if (a[i].sceneName != b[i].sceneName) return false;
            if (a[i].displayName != b[i].displayName) return false;
        }
        return true;
    }

    private static void EnsureFolderExists(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            return;

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
