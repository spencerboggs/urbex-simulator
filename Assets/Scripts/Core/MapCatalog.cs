using System;
using UnityEngine;

// Runtime list of gameplay maps the player can pick in the lobby.
//
// The asset lives at Assets/Resources/MapCatalog.asset and is auto-generated /
// kept in sync with Assets/Scenes/Gameplay/*.unity by Editor/MapCatalogAutoSync.cs.
// You can edit `displayName` by hand on each entry; the auto-sync preserves
// custom display names across re-imports. Scene names themselves are the source
// of truth — drop a new .unity file in Scenes/Gameplay/ and it will appear here.
//
// Loaded once at runtime via Resources.Load. If the asset doesn't exist yet,
// Load() returns null and callers should treat that as "no maps available".
[CreateAssetMenu(fileName = "MapCatalog", menuName = "UrbexSim/Map Catalog")]
public sealed class MapCatalog : ScriptableObject
{
    [Serializable]
    public class MapEntry
    {
        [Tooltip("Scene filename without the .unity extension. This is what FishNet's SceneManager.LoadGlobalScenes expects.")]
        public string sceneName;

        [Tooltip("Friendly name shown to players in the lobby UI. Edits here are preserved by the auto-sync.")]
        public string displayName;
    }

    [SerializeField]
    private MapEntry[] _maps = Array.Empty<MapEntry>();

    public MapEntry[] Maps => _maps ?? Array.Empty<MapEntry>();
    public int Count => _maps?.Length ?? 0;

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

    // Editor-only entry point for MapCatalogAutoSync. Don't call at runtime — the
    // catalog should be treated as immutable once the game is running.
    public void SetMaps(MapEntry[] entries)
    {
        _maps = entries ?? Array.Empty<MapEntry>();
    }

    // ---- Runtime load ----

    public const string ResourcesPath = "MapCatalog";

    private static MapCatalog _cached;

    public static MapCatalog Load()
    {
        if (_cached != null)
            return _cached;
        _cached = Resources.Load<MapCatalog>(ResourcesPath);
        return _cached;
    }

    // Allow tests / editor tooling to invalidate the cache so the next Load picks
    // up an updated asset without a domain reload.
    public static void InvalidateCache() => _cached = null;
}
