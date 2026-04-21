using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

// Enables player input and view only for the owning client. Replicates root transform to
// other clients (movement is local-only on the owner so there is no NetworkTransform on the prefab)
[DefaultExecutionOrder(-50)]
public sealed class PlayerLocalControls : NetworkBehaviour
{
    private const float TransformSendInterval = 1f / 20f;

    private float _nextTransformRpcTime;

    private readonly SyncVar<Vector3> _replicatedPos = new(
        Vector3.zero,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 1f / 20f, Channel.Unreliable));

    private readonly SyncVar<Quaternion> _replicatedRot = new(
        Quaternion.identity,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 1f / 20f, Channel.Unreliable));

    private bool IsStagingOrMenuScene()
    {
        // Use the object's scene (active scene can still be the previous scene for a frame after FishNet loads World)
        string n = gameObject.scene.name;
        return n == NetworkSceneFlow.Lobby || n == NetworkSceneFlow.MainMenu;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Match spawn pose so observers are correct before the first owner LateUpdate/Rpc
        _replicatedPos.Value = transform.position;
        _replicatedRot.Value = transform.rotation;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        _replicatedPos.OnChange += OnReplicatedPosChanged;
        _replicatedRot.OnChange += OnReplicatedRotChanged;

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

            // Remote proxies: transform is driven by sync; CharacterController would fight Teleport/set
            if (TryGetComponent(out CharacterController characterController))
                characterController.enabled = false;
        }
    }

    public override void OnStopClient()
    {
        _replicatedPos.OnChange -= OnReplicatedPosChanged;
        _replicatedRot.OnChange -= OnReplicatedRotChanged;
        base.OnStopClient();
    }

    private void LateUpdate()
    {
        if (!IsClientStarted || !IsOwner || IsStagingOrMenuScene())
            return;

        // Listen host alone: no remote observers 
        // Skip RPC + SyncVar churn (avoids fighting CharacterController)
        if (NetworkManager != null && NetworkManager.IsHostStarted &&
            NetworkManager.ServerManager != null &&
            NetworkManager.ServerManager.Clients.Count <= 1)
            return;

        if (Time.time < _nextTransformRpcTime)
            return;
        _nextTransformRpcTime = Time.time + TransformSendInterval;

        RpcSubmitTransform(transform.position, transform.rotation);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RpcSubmitTransform(Vector3 position, Quaternion rotation)
    {
        _replicatedPos.Value = position;
        _replicatedRot.Value = rotation;
    }

    private void OnReplicatedPosChanged(Vector3 previous, Vector3 next, bool asServer) => ApplyReplicatedTransform();

    private void OnReplicatedRotChanged(Quaternion previous, Quaternion next, bool asServer) => ApplyReplicatedTransform();

    private void ApplyReplicatedTransform()
    {
        // Owner (listen host included): transform is driven by CharacterController / local simulation
        // Applying replicated state here fights the controller and feels very laggy on solo host tests
        if (IsOwner)
            return;

        transform.SetPositionAndRotation(_replicatedPos.Value, _replicatedRot.Value);
    }
}
