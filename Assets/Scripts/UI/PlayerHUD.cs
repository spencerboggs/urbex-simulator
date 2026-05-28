using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Gameplay HUD bindings for health, stamina, hotbar, and context hints.</summary>
public class PlayerHUD : MonoBehaviour
{
    /// <summary>UI Toolkit health bar fill element.</summary>
    private VisualElement healthFill;

    /// <summary>UI Toolkit stamina bar fill element.</summary>
    private VisualElement staminaFill;

    /// <summary>Context hint row (camera equip, interact, etc.).</summary>
    private VisualElement cameraHintRow;

    /// <summary>Text inside the context hint row.</summary>
    private Label cameraHintLabel;

    /// <summary>Hotbar container for slot styling.</summary>
    private VisualElement hotbarRoot;

    /// <summary>Item key hint block below the hotbar.</summary>
    private VisualElement itemKeyHintsRow;

    /// <summary>First line of item key hints.</summary>
    private Label itemKeyHintLine0;

    /// <summary>Second line of item key hints.</summary>
    private Label itemKeyHintLine1;

    /// <summary>Five hotbar slot visual elements.</summary>
    private VisualElement[] hotbarSlots;

    /// <summary>Item name labels per hotbar slot.</summary>
    private Label[] hotbarItemLabels;

    /// <summary>Key badge labels per hotbar slot.</summary>
    private Label[] hotbarKeyLabels;

    /// <summary>Lazy-queries UIDocument elements by name on first use.</summary>
    private void TryBindFills()
    {
        if (healthFill != null &&
            staminaFill != null &&
            hotbarRoot != null &&
            hotbarSlots != null &&
            hotbarItemLabels != null &&
            hotbarKeyLabels != null)
            return;

        var doc = GetComponent<UIDocument>();
        if (doc == null)
            return;

        var root = doc.rootVisualElement;
        if (root == null)
            return;

        if (healthFill == null)
            healthFill = root.Q<VisualElement>("healthFill");
        if (staminaFill == null)
            staminaFill = root.Q<VisualElement>("staminaFill");
        if (cameraHintRow == null)
            cameraHintRow = root.Q<VisualElement>("cameraHintRow");
        if (cameraHintLabel == null)
            cameraHintLabel = root.Q<Label>("cameraHintLabel");

        if (hotbarRoot == null)
            hotbarRoot = root.Q<VisualElement>("hotbarRoot");
        if (itemKeyHintsRow == null)
            itemKeyHintsRow = root.Q<VisualElement>("itemKeyHintsRow");
        if (itemKeyHintLine0 == null)
            itemKeyHintLine0 = root.Q<Label>("itemKeyHintLine0");
        if (itemKeyHintLine1 == null)
            itemKeyHintLine1 = root.Q<Label>("itemKeyHintLine1");

        // Resolve five hotbar slots and paired item/key labels.
        if (hotbarSlots == null || hotbarSlots.Length != 5)
        {
            hotbarSlots = new VisualElement[5];
            hotbarItemLabels = new Label[5];
            hotbarKeyLabels = new Label[5];
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                hotbarSlots[i] = root.Q<VisualElement>($"hotbarSlot{i}");
                hotbarItemLabels[i] = root.Q<Label>($"hotbarSlot{i}Item");
                hotbarKeyLabels[i] = root.Q<Label>($"hotbarSlot{i}Key");
            }
        }
    }

    /// <summary>Shows or hides the context hint row with the given text.</summary>
    public void SetContextHint(bool visible, string line)
    {
        TryBindFills();
        if (cameraHintLabel != null && !string.IsNullOrEmpty(line))
            cameraHintLabel.text = line;
        if (cameraHintRow != null)
            cameraHintRow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>Alias for <see cref="SetContextHint"/> (camera equip line).</summary>
    public void SetCameraEquipHint(bool visible, string line)
    {
        SetContextHint(visible, line);
    }

    /// <summary>Sets health bar fill (0 to 1) and color band.</summary>
    public void SetHealth(float percent)
    {
        TryBindFills();
        if (healthFill == null)
            return;

        percent = Mathf.Clamp01(percent);
        healthFill.style.width = Length.Percent(percent * 100f);
        if (percent > 0.5f)
            healthFill.style.backgroundColor = new Color(0.3f, 0.75f, 0.35f);
        else if (percent > 0.28f)
            healthFill.style.backgroundColor = Color.yellow;
        else
            healthFill.style.backgroundColor = Color.red;
    }

    /// <summary>Sets stamina bar fill (0 to 1) and color band.</summary>
    public void SetStamina(float percent)
    {
        TryBindFills();
        if (staminaFill == null)
            return;

        percent = Mathf.Clamp01(percent);
        staminaFill.style.width = Length.Percent(percent * 100f);
        if (percent > 0.5f)
        {
            staminaFill.style.backgroundColor = Color.green;
        }
        else if (percent > 0.28f)
        {
            staminaFill.style.backgroundColor = Color.yellow;
        }
        else
        {
            staminaFill.style.backgroundColor = Color.red;
        }
    }

    /// <summary>Shows item key hint lines below the hotbar.</summary>
    public void SetItemKeyHints(bool visible, string line0, string line1 = null)
    {
        TryBindFills();

        if (itemKeyHintsRow != null)
            itemKeyHintsRow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (itemKeyHintLine0 != null)
        {
            itemKeyHintLine0.text = line0 ?? string.Empty;
            itemKeyHintLine0.style.display =
                visible && !string.IsNullOrEmpty(line0) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (itemKeyHintLine1 != null)
        {
            itemKeyHintLine1.text = line1 ?? string.Empty;
            itemKeyHintLine1.style.display =
                visible && !string.IsNullOrEmpty(line1) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Updates hotbar slot availability, selection, labels, and key badges.</summary>
    public void SetHotbarState(int availableSlots, int selectedIndex, string[] slotItemNames = null, string[] slotKeyLabels = null)
    {
        TryBindFills();
        if (hotbarSlots == null)
            return;

        availableSlots = Mathf.Clamp(availableSlots, 0, hotbarSlots.Length);
        if (selectedIndex >= hotbarSlots.Length)
            selectedIndex = hotbarSlots.Length - 1;

        for (int i = 0; i < hotbarSlots.Length; i++)
        {
            VisualElement slot = hotbarSlots[i];
            if (slot == null)
                continue;

            bool isAvailable = i < availableSlots;
            slot.EnableInClassList("hotbarSlotUnavailable", !isAvailable);
            slot.EnableInClassList("hotbarSlotSelected", isAvailable && selectedIndex >= 0 && i == selectedIndex);

            if (hotbarItemLabels != null && i < hotbarItemLabels.Length && hotbarItemLabels[i] != null)
            {
                hotbarItemLabels[i].text =
                    slotItemNames != null &&
                    i < slotItemNames.Length &&
                    !string.IsNullOrEmpty(slotItemNames[i])
                        ? slotItemNames[i]
                        : string.Empty;
            }

            if (hotbarKeyLabels != null && i < hotbarKeyLabels.Length && hotbarKeyLabels[i] != null)
            {
                hotbarKeyLabels[i].text =
                    slotKeyLabels != null &&
                    i < slotKeyLabels.Length &&
                    !string.IsNullOrEmpty(slotKeyLabels[i])
                        ? slotKeyLabels[i]
                        : string.Empty;
                hotbarKeyLabels[i].EnableInClassList("hotbarKeyLabelUnavailable", !isAvailable);
            }
        }
    }
}
