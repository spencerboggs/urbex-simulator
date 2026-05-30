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
            InventoryItemType.SprayPaint => "Spray Paint",
            _ => string.Empty,
        };
    }

    /// <summary>Whether the item can be dropped into the world.</summary>
    public static bool CanDrop(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => true,
            InventoryItemType.SprayPaint => true,
            _ => false,
        };
    }

    /// <summary>Whether the item has a primary-use action.</summary>
    public static bool SupportsPrimaryUse(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => true,
            InventoryItemType.SprayPaint => true,
            _ => false,
        };
    }

    /// <summary>Primary-use hint suffix (e.g. "Toggle Flashlight").</summary>
    public static string GetPrimaryUseDescription(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => "Toggle Flashlight",
            InventoryItemType.SprayPaint => "Spray Paint",
            _ => string.Empty,
        };
    }

    /// <summary>Optional third hint line for an item (e.g. scroll wheel actions).</summary>
    public static bool TryGetExtraKeyHint(InventoryItemType itemType, out string hint)
    {
        if (itemType == InventoryItemType.SprayPaint)
        {
            hint = "Scroll - Adjust Spray Size";
            return true;
        }

        hint = string.Empty;
        return false;
    }
}
