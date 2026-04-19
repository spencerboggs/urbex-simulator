using UnityEngine;

public class PlayerHUDController : MonoBehaviour
{
    [Tooltip("Optional: assign a nested PlayerHUD in the hierarchy for offline testing.")]
    [SerializeField]
    private PlayerHUD hud;

    [Tooltip("Spawned at runtime when hud is unset or points at a prefab asset (typical for FishNet-spawned players).")]
    [SerializeField]
    private GameObject _hudPrefab;

    private PlayerMovement movement;

    private void Start()
    {
        movement = GetComponent<PlayerMovement>();

        if (hud != null && !hud.gameObject.scene.IsValid())
            hud = null;

        if (hud == null)
        {
            if (_hudPrefab == null)
                return;
            hud = Instantiate(_hudPrefab, transform).GetComponent<PlayerHUD>();
        }
    }

    private void Update()
    {
        if (movement == null || hud == null)
            return;

        hud.SetStamina(movement.GetSprintCharge() / movement.GetMaxSprintCharge());
    }
}
