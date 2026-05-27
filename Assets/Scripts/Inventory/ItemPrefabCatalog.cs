using System;
using UnityEngine;

// Runtime lookup for world-item prefabs under Assets/Prefabs/Items/
// Auto-maintained by Editor/ItemPrefabCatalogAutoSync.cs into
// Assets/Resources/ItemPrefabCatalog.asset
[CreateAssetMenu(fileName = "ItemPrefabCatalog", menuName = "UrbexSim/Item Prefab Catalog")]
public sealed class ItemPrefabCatalog : ScriptableObject
{
    [Serializable]
    public class ItemPrefabEntry
    {
        public InventoryItemType itemType;
        public WorldInventoryItem prefab;
    }

    [SerializeField]
    private ItemPrefabEntry[] _entries = Array.Empty<ItemPrefabEntry>();

    public ItemPrefabEntry[] Entries => _entries ?? Array.Empty<ItemPrefabEntry>();

    public const string ResourcesPath = "ItemPrefabCatalog";

    private static ItemPrefabCatalog _cached;

    public static ItemPrefabCatalog Load()
    {
        if (_cached != null)
            return _cached;
        _cached = Resources.Load<ItemPrefabCatalog>(ResourcesPath);
        return _cached;
    }

    public static void InvalidateCache() => _cached = null;

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

    public void SetEntries(ItemPrefabEntry[] entries)
    {
        _entries = entries ?? Array.Empty<ItemPrefabEntry>();
    }
}
