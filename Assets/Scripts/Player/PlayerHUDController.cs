using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives stamina and health bars on the player HUD and forwards hotbar or hint updates.
/// </summary>
public class PlayerHUDController : MonoBehaviour
{
    [Tooltip("Optional: assign a nested PlayerHUD in the hierarchy for offline testing.")]
    [SerializeField]
    private PlayerHUD hud;

    [Tooltip("Spawned at runtime when hud is unset or points at a prefab asset (typical for FishNet-spawned players).")]
    [SerializeField]
    private GameObject _hudPrefab;

    /// <summary>Cached movement for stamina bar updates.</summary>
    private PlayerMovement movement;
    /// <summary>Cached health for health bar updates.</summary>
    private PlayerHealth health;
    /// <summary>UI Toolkit document on the spawned or assigned HUD.</summary>
    private UIDocument _hudDocument;
    /// <summary>When false, bars are hidden (e.g. camera viewfinder open).</summary>
    private bool _mainHudVisualVisible = true;

    /// <summary>Spawns or binds HUD, then syncs inventory hotbar state.</summary>
    private void Start()
    {
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();

        // Ignore prefab asset references; spawn runtime HUD when needed.
        if (hud != null && !hud.gameObject.scene.IsValid())
            hud = null;

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

    /// <summary>Pushes normalized health and stamina values to the HUD each frame.</summary>
    private void Update()
    {
        if (hud == null)
            return;

        if (!_mainHudVisualVisible)
            return;

        // Health and stamina bars from cached player components.
        if (health != null)
            hud.SetHealth(health.HealthPercent);

        if (movement != null)
            hud.SetStamina(movement.GetSprintCharge() / movement.GetMaxSprintCharge());
    }

    /// <summary>Hides main HUD bars while the handheld camera viewfinder is open.</summary>
    public void SetMainHudVisual(bool visible)
    {
        _mainHudVisualVisible = visible;
        if (_hudDocument != null && _hudDocument.rootVisualElement != null)
            _hudDocument.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>Shows or hides the interaction context hint line.</summary>
    public void SetContextHint(bool visible, string line)
    {
        if (hud == null)
            return;
        hud.SetContextHint(visible, line);
    }

    /// <summary>Shows or hides the camera equip hint (same as context hint).</summary>
    public void SetCameraEquipHint(bool visible, string line)
    {
        SetContextHint(visible, line);
    }

    /// <summary>Updates hotbar slot count and selection.</summary>
    public void SetHotbarState(int availableSlots, int selectedIndex)
    {
        if (hud == null)
            return;
        hud.SetHotbarState(availableSlots, selectedIndex);
    }

    /// <summary>Updates hotbar with per-slot item names and key labels.</summary>
    public void SetHotbarState(int availableSlots, int selectedIndex, string[] slotItemNames, string[] slotKeyLabels)
    {
        if (hud == null)
            return;
        hud.SetHotbarState(availableSlots, selectedIndex, slotItemNames, slotKeyLabels);
    }

    /// <summary>Shows primary-use and drop key hints for the selected item.</summary>
    public void SetItemKeyHints(bool visible, string line0, string line1 = null)
    {
        if (hud == null)
            return;
        hud.SetItemKeyHints(visible, line0, line1);
    }
}
