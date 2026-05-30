using UnityEditor;
using UnityEngine;

/// <summary>Creates the Paintball Gun world item prefab under Assets/Prefabs/Items/.</summary>
public static class CreatePaintballGunPrefab
{
    private const string PrefabFolder = "Assets/Prefabs/Items";
    private const string PrefabPath = PrefabFolder + "/PaintballGun.prefab";

    /// <summary>Builds or updates the Paintball Gun item prefab and refreshes the item catalog.</summary>
    [MenuItem("Tools/Urbex/Create Item Prefabs/Paintball Gun")]
    public static void CreateOrUpdatePrefab()
    {
        EnsureFolderExists(PrefabFolder);

        GameObject root = new GameObject("PaintballGun");
        WorldInventoryItem worldItem = root.AddComponent<WorldInventoryItem>();
        worldItem.Configure(InventoryItemType.PaintballGun);

        PaintballGunVisualFactory.EnsurePlaceholderVisual(root.transform);

        GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        ItemPrefabCatalogAutoSync.RefreshFromMenu();
        Selection.activeObject = prefabRoot;
        Debug.Log($"[CreatePaintballGunPrefab] Saved {PrefabPath}");
    }

    /// <summary>Recursively creates an Assets/... folder path when missing.</summary>
    private static void EnsureFolderExists(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            return;

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
