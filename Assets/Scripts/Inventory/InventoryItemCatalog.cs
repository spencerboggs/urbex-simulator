public static class InventoryItemCatalog
{
    public static string GetDisplayName(InventoryItemType itemType)
    {
        return itemType switch
        {
            InventoryItemType.Camera => "camera",
            InventoryItemType.Flashlight => "flashlight",
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
}
