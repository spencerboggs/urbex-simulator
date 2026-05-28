using System;
using UnityEngine;

/// <summary>
/// Runtime list of gameplay maps for the lobby. Asset path: Assets/Resources/MapCatalog.asset,
/// auto-synced from Assets/Scenes/Gameplay/ by <c>MapCatalogAutoSync</c>. Custom
/// <see cref="MapEntry.displayName"/> values are preserved across re-imports; scene file names are authoritative.
/// </summary>
[CreateAssetMenu(fileName = "MapCatalog", menuName = "UrbexSim/Map Catalog")]
public sealed class MapCatalog : ScriptableObject
{
    /// <summary>One selectable gameplay scene.</summary>
    [Serializable]
    public class MapEntry
    {
        [Tooltip("Scene filename without the .unity extension. This is what FishNet's SceneManager.LoadGlobalScenes expects.")]
        public string sceneName;

        [Tooltip("Friendly name shown to players in the lobby UI. Edits here are preserved by the auto-sync.")]
        public string displayName;
    }

    /// <summary>Serialized map list; replaced by editor auto-sync.</summary>
    [SerializeField]
    private MapEntry[] _maps = Array.Empty<MapEntry>();

    /// <summary>All map entries (never null).</summary>
    public MapEntry[] Maps => _maps ?? Array.Empty<MapEntry>();

    /// <summary>Number of entries.</summary>
    public int Count => _maps?.Length ?? 0;

    /// <summary>Looks up an entry by zero-based index.</summary>
    public bool TryGetByIndex(int index, out MapEntry entry)
    {
        if (_maps != null && index >= 0 && index < _maps.Length && _maps[index] != null)
        {
            entry = _maps[index];
            return true;
        }
        entry = null;
        return false;
    }

    /// <summary>Looks up an entry by scene name (without .unity).</summary>
    public bool TryGetBySceneName(string sceneName, out MapEntry entry, out int index)
    {
        entry = null;
        index = -1;
        if (_maps == null || string.IsNullOrEmpty(sceneName))
            return false;

        for (int i = 0; i < _maps.Length; i++)
        {
            if (_maps[i] != null && string.Equals(_maps[i].sceneName, sceneName, StringComparison.Ordinal))
            {
                entry = _maps[i];
                index = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>Editor-only replacement of entries; do not call at runtime.</summary>
    public void SetMaps(MapEntry[] entries)
    {
        _maps = entries ?? Array.Empty<MapEntry>();
    }

    /// <summary>Resources.Load path for this catalog.</summary>
    public const string ResourcesPath = "MapCatalog";

    /// <summary>Cached instance from the last <see cref="Load"/> call.</summary>
    private static MapCatalog _cached;

    /// <summary>Loads and caches the catalog from Resources; returns null if missing.</summary>
    public static MapCatalog Load()
    {
        if (_cached != null)
            return _cached;
        _cached = Resources.Load<MapCatalog>(ResourcesPath);
        return _cached;
    }

    /// <summary>Clears the runtime cache so the next <see cref="Load"/> reads the asset again.</summary>
    public static void InvalidateCache() => _cached = null;
}
