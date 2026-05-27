public static class InventoryItemCatalog
{
    public static string GetDisplayName(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Camera => "Camera",
            InventoryItemType.Flashlight => "Flashlight",
            _ => string.Empty,
        };
    }

    public static bool CanDrop(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => true,
            _ => false,
        };
    }

    public static bool SupportsPrimaryUse(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => true,
            _ => false,
        };
    }

    // Action text after the key in item hints, e.g. "Toggle Flashlight"
    public static string GetPrimaryUseDescription(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Flashlight => "Toggle Flashlight",
            _ => string.Empty,
        };
    }
}
