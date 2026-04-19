using FishNet.Object;
using UnityEngine;

// Enables player input and view only for the owning client. FishNet still instantiates
[DefaultExecutionOrder(-50)]
public sealed class PlayerLocalControls : NetworkBehaviour
{
    private bool IsStagingOrMenuScene()
    {
        // Use the object's scene — active scene can still be the previous scene for a frame after FishNet loads World.
        string n = gameObject.scene.name;
        return n == NetworkSceneFlow.Lobby || n == NetworkSceneFlow.MainMenu;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            if (TryGetComponent(out PlayerMovement movement))
                movement.enabled = !IsStagingOrMenuScene();
            if (TryGetComponent(out CameraLook look))
                look.enabled = !IsStagingOrMenuScene();
            if (TryGetComponent(out ClimbingController climbing))
                climbing.enabled = !IsStagingOrMenuScene();
            if (TryGetComponent(out PlayerHUDController hud))
                hud.enabled = !IsStagingOrMenuScene();

            if (IsStagingOrMenuScene())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
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
