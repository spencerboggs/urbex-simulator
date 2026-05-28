using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Keeps Assets/Resources/ItemPrefabCatalog.asset in sync with prefabs in
// Assets/Prefabs/Items/. Each prefab must have a WorldInventoryItem whose
// Item Type field defines the catalog key
public sealed class ItemPrefabCatalogAutoSync : AssetPostprocessor
{
    private const string ItemsPrefabsRoot = "Assets/Prefabs/Items";
    private const string ResourcesFolder = "Assets/Resources";
    private const string CatalogAssetPath = ResourcesFolder + "/ItemPrefabCatalog.asset";

    [InitializeOnLoadMethod]
    private static void ResyncOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(CatalogAssetPath))
                Resync();
        };
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (AnyItemPrefab(importedAssets) || AnyItemPrefab(deletedAssets) ||
            AnyItemPrefab(movedAssets) || AnyItemPrefab(movedFromAssetPaths))
        {
            Resync();
        }
    }

    [MenuItem("Tools/Urbex/Refresh Item Prefab Catalog")]
    public static void RefreshFromMenu()
    {
        Resync();
        Debug.Log("[ItemPrefabCatalogAutoSync] Manual refresh complete.");
    }

    private static bool AnyItemPrefab(string[] paths)
    {
        if (paths == null)
            return false;

        for (int i = 0; i < paths.Length; i++)
        {
            if (string.IsNullOrEmpty(paths[i]))
                continue;

            string normalized = paths[i].Replace('\\', '/');
            if (normalized.StartsWith(ItemsPrefabsRoot, System.StringComparison.OrdinalIgnoreCase) &&
                normalized.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Resync()
    {
        if (!Directory.Exists(ItemsPrefabsRoot))
            return;

        try
        {
            AssetDatabase.StartAssetEditing();
            EnsureFolderExists(ResourcesFolder);

            ItemPrefabCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemPrefabCatalog>(CatalogAssetPath);
            bool isNew = false;
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPrefabCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
                isNew = true;
            }

            List<ItemPrefabCatalog.ItemPrefabEntry> entries = new List<ItemPrefabCatalog.ItemPrefabEntry>();
            string[] prefabPaths = Directory.GetFiles(ItemsPrefabsRoot, "*.prefab", SearchOption.AllDirectories);
            System.Array.Sort(prefabPaths, System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < prefabPaths.Length; i++)
            {
                string assetPath = prefabPaths[i].Replace('\\', '/');
                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefabRoot == null)
                    continue;

                if (!prefabRoot.TryGetComponent(out WorldInventoryItem worldItem))
                {
                    Debug.LogWarning(
                        $"[ItemPrefabCatalogAutoSync] Skipping '{assetPath}' - no WorldInventoryItem on root.");
                    continue;
                }

                InventoryItemType itemType = worldItem.ItemType;
                if (itemType == InventoryItemType.None)
                {
                    Debug.LogWarning(
                        $"[ItemPrefabCatalogAutoSync] Skipping '{assetPath}' - Item Type is None.");
                    continue;
                }

                entries.Add(new ItemPrefabCatalog.ItemPrefabEntry
                {
                    itemType = itemType,
                    prefab = worldItem,
                });
            }

            if (!isNew && EntriesEqual(catalog.Entries, entries))
                return;

            catalog.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(catalog);
            ItemPrefabCatalog.InvalidateCache();
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }
    }

    private static bool EntriesEqual(
        ItemPrefabCatalog.ItemPrefabEntry[] a,
        List<ItemPrefabCatalog.ItemPrefabEntry> b)
    {
        if (a == null || b == null)
            return a == null && (b == null || b.Count == 0);
        if (a.Length != b.Count)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == null || b[i] == null)
                return false;
            if (a[i].itemType != b[i].itemType)
                return false;
            if (a[i].prefab != b[i].prefab)
                return false;
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
