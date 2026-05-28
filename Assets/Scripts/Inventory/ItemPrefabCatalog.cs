using System;
using UnityEngine;

/// <summary>
/// Runtime lookup for world-item prefabs under Assets/Prefabs/Items/, maintained in
/// Assets/Resources/ItemPrefabCatalog.asset by <c>ItemPrefabCatalogAutoSync</c>.
/// </summary>
[CreateAssetMenu(fileName = "ItemPrefabCatalog", menuName = "UrbexSim/Item Prefab Catalog")]
public sealed class ItemPrefabCatalog : ScriptableObject
{
    /// <summary>Maps an item type to its world prefab.</summary>
    [Serializable]
    public class ItemPrefabEntry
    {
        public InventoryItemType itemType;
        public WorldInventoryItem prefab;
    }

    /// <summary>Serialized prefab entries; replaced by editor auto-sync.</summary>
    [SerializeField]
    private ItemPrefabEntry[] _entries = Array.Empty<ItemPrefabEntry>();

    /// <summary>All entries (never null).</summary>
    public ItemPrefabEntry[] Entries => _entries ?? Array.Empty<ItemPrefabEntry>();

    /// <summary>Resources.Load path for this catalog.</summary>
    public const string ResourcesPath = "ItemPrefabCatalog";

    /// <summary>Cached instance from the last <see cref="Load"/> call.</summary>
    private static ItemPrefabCatalog _cached;

    /// <summary>Loads and caches the catalog from Resources.</summary>
    public static ItemPrefabCatalog Load()
    {
        if (_cached != null)
            return _cached;
        _cached = Resources.Load<ItemPrefabCatalog>(ResourcesPath);
        return _cached;
    }

    /// <summary>Clears the runtime cache.</summary>
    public static void InvalidateCache() => _cached = null;

    /// <summary>Resolves a prefab for the given item type.</summary>
    public bool TryGetPrefab(InventoryItemType itemType, out WorldInventoryItem prefab)
    {
        prefab = null;
        if (_entries == null)
            return false;

        for (int i = 0; i < _entries.Length; i++)
        {
            ItemPrefabEntry entry = _entries[i];
            if (entry == null || entry.prefab == null)
                continue;
            if (entry.itemType == itemType)
            {
                prefab = entry.prefab;
                return true;
            }
        }

        return false;
    }

    /// <summary>Editor-only replacement of entries.</summary>
    public void SetEntries(ItemPrefabEntry[] entries)
    {
        _entries = entries ?? Array.Empty<ItemPrefabEntry>();
    }
}
