using FishNet.Object;
using UnityEngine;

// Enables player input and view only for the owning client. FishNet still instantiates
[DefaultExecutionOrder(-50)]
public sealed class PlayerLocalControls : NetworkBehaviour
{
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            if (TryGetComponent(out PlayerMovement movement))
                movement.enabled = true;
            if (TryGetComponent(out CameraLook look))
                look.enabled = true;
            if (TryGetComponent(out ClimbingController climbing))
                climbing.enabled = true;
            if (TryGetComponent(out PlayerHUDController hud))
                hud.enabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            foreach (Camera cam in GetComponentsInChildren<Camera>(true))
                cam.enabled = false;
            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
        }
    }
}
