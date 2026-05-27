using UnityEngine;
using UnityEngine.UIElements;

public class PlayerHUDController : MonoBehaviour
{
    [Tooltip("Optional: assign a nested PlayerHUD in the hierarchy for offline testing.")]
    [SerializeField]
    private PlayerHUD hud;

    [Tooltip("Spawned at runtime when hud is unset or points at a prefab asset (typical for FishNet-spawned players).")]
    [SerializeField]
    private GameObject _hudPrefab;

    private PlayerMovement movement;
    private PlayerHealth health;
    private UIDocument _hudDocument;
    private bool _mainHudVisualVisible = true;

    private void Start()
    {
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();

        if (hud != null && !hud.gameObject.scene.IsValid())
            hud = null;

        // If no valid HUD reference is assigned
        // Instantiate one from the prefab
        if (hud == null)
        {
            if (_hudPrefab == null)
                return;
            hud = Instantiate(_hudPrefab, transform).GetComponent<PlayerHUD>();
        }

        if (hud != null)
            _hudDocument = hud.GetComponent<UIDocument>();

        if (TryGetComponent(out PlayerInventoryController inventory))
            inventory.RefreshHudState();
    }

    private void Update()
    {
        if (hud == null)
            return;

        if (!_mainHudVisualVisible)
            return;

        if (health != null)
            hud.SetHealth(health.HealthPercent);

        if (movement != null)
            hud.SetStamina(movement.GetSprintCharge() / movement.GetMaxSprintCharge());
    }

    // Hides main HUD bars while the handheld camera viewfinder is open
    public void SetMainHudVisual(bool visible)
    {
        _mainHudVisualVisible = visible;
        if (_hudDocument != null && _hudDocument.rootVisualElement != null)
            _hudDocument.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetContextHint(bool visible, string line)
    {
        if (hud == null)
            return;
        hud.SetContextHint(visible, line);
    }

    public void SetCameraEquipHint(bool visible, string line)
    {
        SetContextHint(visible, line);
    }

    public void SetHotbarState(int availableSlots, int selectedIndex)
    {
        if (hud == null)
            return;
        hud.SetHotbarState(availableSlots, selectedIndex);
    }

    public void SetHotbarState(int availableSlots, int selectedIndex, string[] slotItemNames, string[] slotKeyLabels)
    {
        if (hud == null)
            return;
        hud.SetHotbarState(availableSlots, selectedIndex, slotItemNames, slotKeyLabels);
    }

    public void SetItemKeyHints(bool visible, string line0, string line1 = null)
    {
        if (hud == null)
            return;
        hud.SetItemKeyHints(visible, line0, line1);
    }
}
