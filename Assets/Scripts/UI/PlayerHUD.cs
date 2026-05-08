using UnityEngine;
using UnityEngine.UIElements;

public class PlayerHUD : MonoBehaviour
{
    private VisualElement healthFill;
    private VisualElement staminaFill;
    private VisualElement cameraHintRow;
    private Label cameraHintLabel;
    private VisualElement hotbarRoot;
    private VisualElement[] hotbarSlots;

    private void TryBindFills()
    {
        if (healthFill != null && staminaFill != null && hotbarRoot != null && hotbarSlots != null)
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

        if (hotbarSlots == null || hotbarSlots.Length != 5)
        {
            hotbarSlots = new VisualElement[5];
            for (int i = 0; i < hotbarSlots.Length; i++)
                hotbarSlots[i] = root.Q<VisualElement>($"hotbarSlot{i}");
        }
    }

    public void SetCameraEquipHint(bool visible, string line)
    {
        TryBindFills();
        if (cameraHintLabel != null && !string.IsNullOrEmpty(line))
            cameraHintLabel.text = line;
        if (cameraHintRow != null)
            cameraHintRow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

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

    public void SetHotbarState(int availableSlots, int selectedIndex)
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
        }
    }
}
