using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps MapCatalog and EditorBuildSettings in sync with Assets/Scenes/Gameplay/.
/// Runs on scene asset changes and via Tools/Urbex/Refresh Map Catalog.
/// </summary>
public sealed class MapCatalogAutoSync : AssetPostprocessor
{
    private const string GameplayScenesRoot = "Assets/Scenes/Gameplay";
    private const string ResourcesFolder = "Assets/Resources";
    private const string MapCatalogAssetPath = ResourcesFolder + "/MapCatalog.asset";

    /// <summary>Asset postprocessor hook: resync when gameplay scenes change.</summary>
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (AnyScene(importedAssets) || AnyScene(deletedAssets) || AnyScene(movedAssets) || AnyScene(movedFromAssetPaths))
            Resync();
    }

    /// <summary>Manual refresh from the Tools menu.</summary>
    [MenuItem("Tools/Urbex/Refresh Map Catalog")]
    public static void RefreshFromMenu()
    {
        Resync();
        Debug.Log("[MapCatalogAutoSync] Manual refresh complete.");
    }

    /// <summary>True when any path is a scene under Assets/Scenes/.</summary>
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

    /// <summary>Discovers gameplay scenes and updates build settings plus MapCatalog asset.</summary>
    private static void Resync()
    {
        List<string> gameplayScenePaths = DiscoverGameplayScenes();

        try
        {
            AssetDatabase.StartAssetEditing();
            // Keep EditorBuildSettings and MapCatalog.asset aligned with disk.
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

    /// <summary>Collects sorted .unity paths under GameplayScenesRoot.</summary>
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

    /// <summary>Merges gameplay scenes into EditorBuildSettings without removing other scenes.</summary>
    private static void SyncBuildSettings(List<string> gameplayScenePaths)
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];

        Dictionary<string, EditorBuildSettingsScene> existingByPath = new Dictionary<string, EditorBuildSettingsScene>(System.StringComparer.OrdinalIgnoreCase);
        foreach (EditorBuildSettingsScene s in current)
        {
            if (s == null || string.IsNullOrEmpty(s.path)) continue;
            existingByPath[s.path.Replace('\\', '/')] = s;
        }

        List<EditorBuildSettingsScene> next = new List<EditorBuildSettingsScene>(current.Length + gameplayScenePaths.Count);

        // Retain non-gameplay scenes (menu, lobby, etc.).
        foreach (EditorBuildSettingsScene s in current)
        {
            if (s == null || string.IsNullOrEmpty(s.path)) continue;
            string normalized = s.path.Replace('\\', '/');
            if (!normalized.StartsWith(GameplayScenesRoot, System.StringComparison.OrdinalIgnoreCase))
                next.Add(s);
        }

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

        if (!ScenesEqual(current, next))
            EditorBuildSettings.scenes = next.ToArray();
    }

    /// <summary>Compares build settings scene lists for path and enabled flag.</summary>
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

    /// <summary>Creates or updates MapCatalog.asset entries from discovered scene paths.</summary>
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

        // Preserve hand-edited display names when the scene name is unchanged.
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

        if (!isNew && MapsEqual(catalog.Maps, next))
            return;

        catalog.SetMaps(next);
        EditorUtility.SetDirty(catalog);
    }

    /// <summary>Compares catalog map entries for scene and display name equality.</summary>
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

    /// <summary>Recursively creates an Assets/... folder path when missing.</summary>
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
