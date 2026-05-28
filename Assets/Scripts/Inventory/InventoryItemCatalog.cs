/// <summary>Display names and capability flags per <see cref="InventoryItemType"/>.</summary>
public static class InventoryItemCatalog
{
    /// <summary>Player-facing item name.</summary>
    public static string GetDisplayName(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Camera => "Camera",
            InventoryItemType.Flashlight => "Flashlight",
            _ => string.Empty,
        };
    }

    /// <summary>Whether the item can be dropped into the world.</summary>
    public static bool CanDrop(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => true,
            _ => false,
        };
    }

    /// <summary>Whether the item has a primary-use action.</summary>
    public static bool SupportsPrimaryUse(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => true,
            _ => false,
        };
    }

    /// <summary>Primary-use hint suffix (e.g. "Toggle Flashlight").</summary>
    public static string GetPrimaryUseDescription(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => "Toggle Flashlight",
            _ => string.Empty,
        };
    }
}
