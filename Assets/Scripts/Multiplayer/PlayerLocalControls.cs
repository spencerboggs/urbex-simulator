using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

// Enables player input and view only for the owning client. Replicates root transform to
// other clients (movement is local-only on the owner so there is no NetworkTransform on the prefab)
[DefaultExecutionOrder(-50)]
public sealed class PlayerLocalControls : NetworkBehaviour
{
    private const float TransformSendInterval = 1f / 20f;

    private const int MaxRemoteSamples = 32;

    private float _nextTransformRpcTime;

    [SerializeField]
    [Tooltip("How far behind real time remote poses are rendered; hides jitter and allows lerp between samples.")]
    private float _remoteInterpolationDelaySeconds = 0.1f;

    [SerializeField]
    [Tooltip("When the playhead is past the newest sample, blend in extrapolation from recent velocity (0 = off).")]
    [Range(0f, 1f)]
    private float _remoteVelocityExtrapolationBlend = 0.35f;

    [SerializeField]
    [Min(0f)]
    private float _remoteMaxExtrapolationSeconds = 0.08f;

    private readonly List<RemoteTransformSample> _remoteSamples = new(16);

    // Last staging flag applied to owner movement/camera toggles (null = not yet synced this session)
    private bool? _lastOwnerStagingForComponents;

    private readonly SyncVar<Vector3> _replicatedPos = new(
        Vector3.zero,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 1f / 20f, Channel.Unreliable));

    private readonly SyncVar<Quaternion> _replicatedRot = new(
        Quaternion.identity,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 1f / 20f, Channel.Unreliable));

    private static bool IsLobbyOrMainMenuName(string sceneName)
    {
        return sceneName == NetworkSceneFlow.Lobby || sceneName == NetworkSceneFlow.MainMenu;
    }

    private bool IsStagingOrMenuScene()
    {
        // Once the active scene is the match, never treat as lobby/menu
        string active = UnitySceneManager.GetActiveScene().name;
        if (active == NetworkSceneFlow.World)
            return false;

        string objectScene = gameObject.scene.name;
        return IsLobbyOrMainMenuName(objectScene) || IsLobbyOrMainMenuName(active);
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
            _lastOwnerStagingForComponents = null;
            RefreshOwnerStagingPresentation(forceComponents: true);
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

            if (IsClientStarted)
                SeedRemoteInterpolationBuffer();
        }
    }

    public override void OnStopClient()
    {
        _replicatedPos.OnChange -= OnReplicatedPosChanged;
        _replicatedRot.OnChange -= OnReplicatedRotChanged;
        base.OnStopClient();
    }

    private void Update()
    {
        if (!IsClientStarted || !IsOwner)
            return;

        RefreshOwnerStagingPresentation(forceComponents: false);
    }

    private void RefreshOwnerStagingPresentation(bool forceComponents)
    {
        bool staging = IsStagingOrMenuScene();

        if (forceComponents || !_lastOwnerStagingForComponents.HasValue || _lastOwnerStagingForComponents.Value != staging)
        {
            bool gameplay = !staging;
            if (TryGetComponent(out PlayerMovement movement))
                movement.enabled = gameplay;
            if (TryGetComponent(out CameraLook look))
                look.enabled = gameplay;
            if (TryGetComponent(out ClimbingController climbing))
                climbing.enabled = gameplay;
            if (TryGetComponent(out PlayerHUDController hud))
                hud.enabled = gameplay;
            _lastOwnerStagingForComponents = staging;
        }

        if (staging)
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

    private void LateUpdate()
    {
        if (!IsClientStarted)
            return;

        if (!IsOwner)
        {
            ApplyRemoteInterpolation();
            return;
        }

        if (IsStagingOrMenuScene())
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

    private void OnReplicatedPosChanged(Vector3 previous, Vector3 next, bool asServer) =>
        OnReplicatedTransformChanged(asServer);

    private void OnReplicatedRotChanged(Quaternion previous, Quaternion next, bool asServer) =>
        OnReplicatedTransformChanged(asServer);

    private void OnReplicatedTransformChanged(bool asServer)
    {
        if (IsOwner)
            return;

        // Keep server instance aligned with last authoritative sample (hits/triggers)
        if (asServer && IsServerStarted)
            transform.SetPositionAndRotation(_replicatedPos.Value, _replicatedRot.Value);

        if (!asServer && IsClientStarted)
            TryAppendRemoteSample(Time.time, _replicatedPos.Value, _replicatedRot.Value);
    }

    private void SeedRemoteInterpolationBuffer()
    {
        _remoteSamples.Clear();
        float t = Time.time;
        Vector3 p = _replicatedPos.Value;
        Quaternion r = _replicatedRot.Value;
        _remoteSamples.Add(new RemoteTransformSample(t, p, r));
        _remoteSamples.Add(new RemoteTransformSample(t, p, r));
    }

    private void TryAppendRemoteSample(float time, Vector3 position, Quaternion rotation)
    {
        if (_remoteSamples.Count > 0)
        {
            RemoteTransformSample last = _remoteSamples[^1];
            const float posEps = 0.0001f;
            if ((last.Position - position).sqrMagnitude < posEps &&
                Quaternion.Angle(last.Rotation, rotation) < 0.02f &&
                time - last.Time < 0.0005f)
                return;
        }

        _remoteSamples.Add(new RemoteTransformSample(time, position, rotation));
        PruneRemoteSamplesBefore(Time.time - 2f);
        while (_remoteSamples.Count > MaxRemoteSamples)
            _remoteSamples.RemoveAt(0);
    }

    private void PruneRemoteSamplesBefore(float minTime)
    {
        while (_remoteSamples.Count > 2 && _remoteSamples[1].Time < minTime)
            _remoteSamples.RemoveAt(0);
    }

    private void ApplyRemoteInterpolation()
    {
        float renderTime = Time.time - Mathf.Max(0f, _remoteInterpolationDelaySeconds);

        if (_remoteSamples.Count == 0)
        {
            transform.SetPositionAndRotation(_replicatedPos.Value, _replicatedRot.Value);
            return;
        }

        if (_remoteSamples.Count == 1)
        {
            RemoteTransformSample s = _remoteSamples[0];
            transform.SetPositionAndRotation(s.Position, s.Rotation);
            return;
        }

        // If every sample is still in the future relative to the playhead, hold the oldest
        if (renderTime <= _remoteSamples[0].Time)
        {
            RemoteTransformSample s = _remoteSamples[0];
            transform.SetPositionAndRotation(s.Position, s.Rotation);
            return;
        }

        RemoteTransformSample newest = _remoteSamples[^1];
        if (renderTime >= newest.Time)
        {
            Vector3 pos = newest.Position;
            Quaternion rot = newest.Rotation;

            if (_remoteVelocityExtrapolationBlend > 0f &&
                _remoteSamples.Count >= 2 &&
                _remoteMaxExtrapolationSeconds > 0f)
            {
                RemoteTransformSample prev = _remoteSamples[^2];
                float dt = newest.Time - prev.Time;
                if (dt > 1e-5f)
                {
                    Vector3 velocity = (newest.Position - prev.Position) / dt;
                    float over = Mathf.Min(renderTime - newest.Time, _remoteMaxExtrapolationSeconds);
                    pos = Vector3.Lerp(newest.Position, newest.Position + velocity * over, _remoteVelocityExtrapolationBlend);
                }
            }

            transform.SetPositionAndRotation(pos, rot);
            return;
        }

        int a = 0;
        for (int i = _remoteSamples.Count - 1; i >= 0; i--)
        {
            if (_remoteSamples[i].Time <= renderTime)
            {
                a = i;
                break;
            }
        }

        RemoteTransformSample from = _remoteSamples[a];
        RemoteTransformSample to = _remoteSamples[a + 1];
        float span = to.Time - from.Time;
        float t = span > 1e-6f ? Mathf.Clamp01((renderTime - from.Time) / span) : 1f;
        Vector3 lerpedPos = Vector3.LerpUnclamped(from.Position, to.Position, t);
        Quaternion lerpedRot = Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, t);
        transform.SetPositionAndRotation(lerpedPos, lerpedRot);
    }

    private readonly struct RemoteTransformSample
    {
        public readonly float Time;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public RemoteTransformSample(float time, Vector3 position, Quaternion rotation)
        {
            Time = time;
            Position = position;
            Rotation = rotation;
        }
    }
}
