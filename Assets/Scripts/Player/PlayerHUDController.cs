using UnityEngine;

public class PlayerHUDController : MonoBehaviour
{
    private PlayerMovement movement;
    public PlayerHUD hud;

    void Start() 
    {
        movement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        hud.SetStamina(movement.GetSprintCharge() / movement.GetMaxSprintCharge());
    }
}
