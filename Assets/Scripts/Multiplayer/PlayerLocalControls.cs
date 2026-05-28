using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

/// <summary>
/// Owner-only input and camera; replicates root transform to remote clients without NetworkTransform.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class PlayerLocalControls : NetworkBehaviour
{
    private const float TransformSendInterval = 1f / 20f;

    private const int MaxRemoteSamples = 32;

    /// <summary>Next Time.time when the owner may send a transform RPC.</summary>
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

    /// <summary>Time-stamped poses for remote client interpolation.</summary>
    private readonly List<RemoteTransformSample> _remoteSamples = new(16);

    /// <summary>Last lobby/menu staging flag used to toggle owner components.</summary>
    private bool? _lastOwnerStagingForComponents;

    private readonly SyncVar<Vector3> _replicatedPos = new(
        Vector3.zero,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 1f / 20f, Channel.Unreliable));

    private readonly SyncVar<Quaternion> _replicatedRot = new(
        Quaternion.identity,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 1f / 20f, Channel.Unreliable));

    private readonly SyncVar<string> _matchPhotoRollId = new(
        string.Empty,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 0f, Channel.Reliable));

    /// <summary>True for lobby or main menu scene names.</summary>
    private static bool IsLobbyOrMainMenuName(string sceneName)
    {
        return sceneName == NetworkSceneFlow.Lobby || sceneName == NetworkSceneFlow.MainMenu;
    }

    /// <summary>True when the player is in lobby/menu rather than a gameplay map.</summary>
    private bool IsStagingOrMenuScene()
    {
        string active = UnitySceneManager.GetActiveScene().name;
        if (NetworkSceneFlow.IsGameplayScene(active))
            return false;

        string objectScene = gameObject.scene.name;
        return IsLobbyOrMainMenuName(objectScene) || IsLobbyOrMainMenuName(active);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _replicatedPos.Value = transform.position;
        _replicatedRot.Value = transform.rotation;

        string rollId = PhotoRollSession.PeekServerMatchId();
        _matchPhotoRollId.Value = string.IsNullOrEmpty(rollId) ? string.Empty : rollId;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        EnsureHardBodyPresent();

        _replicatedPos.OnChange += OnReplicatedPosChanged;
        _replicatedRot.OnChange += OnReplicatedRotChanged;
        _matchPhotoRollId.OnChange += OnMatchPhotoRollIdChanged;

        if (!string.IsNullOrEmpty(_matchPhotoRollId.Value))
            PhotoRollSession.ApplyReplicatedMatchId(_matchPhotoRollId.Value);

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
        _matchPhotoRollId.OnChange -= OnMatchPhotoRollIdChanged;
        base.OnStopClient();
    }

    /// <summary>Adds PlayerHardBody and PlayerInventoryController when a CharacterController exists.</summary>
    private void EnsureHardBodyPresent()
    {
        if (!TryGetComponent(out CharacterController _))
            return;
        if (!TryGetComponent(out PlayerHardBody _))
            gameObject.AddComponent<PlayerHardBody>();

        if (!TryGetComponent(out PlayerInventoryController _))
            gameObject.AddComponent<PlayerInventoryController>();
    }

    /// <summary>Applies replicated match photo roll id on clients.</summary>
    private void OnMatchPhotoRollIdChanged(string previous, string next, bool asServer)
    {
        if (asServer)
            return;
        PhotoRollSession.ApplyReplicatedMatchId(next);
    }

    /// <summary>Owner-only: toggles gameplay vs staging component and cursor state.</summary>
    private void Update()
    {
        if (!IsClientStarted || !IsOwner)
            return;

        RefreshOwnerStagingPresentation(forceComponents: false);
    }

    /// <summary>Enables movement/HUD/camera on gameplay scenes; frees cursor in lobby/menu.</summary>
    private void RefreshOwnerStagingPresentation(bool forceComponents)
    {
        bool staging = IsStagingOrMenuScene();

        // Toggle owner-only gameplay scripts when crossing lobby vs map.
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
            if (TryGetComponent(out PlayerCameraMode cameraMode))
                cameraMode.enabled = gameplay;
            if (TryGetComponent(out PlayerFlashlightMode flashlightMode))
                flashlightMode.enabled = gameplay;
            if (TryGetComponent(out PlayerInventoryController inventory))
                inventory.enabled = gameplay;
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

    /// <summary>Owner sends transform RPCs; remotes apply buffered interpolation.</summary>
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

        // Solo host: skip RPC when no remote observers.
        if (NetworkManager != null && NetworkManager.IsHostStarted &&
            NetworkManager.ServerManager != null &&
            NetworkManager.ServerManager.Clients.Count <= 1)
            return;

        if (Time.time < _nextTransformRpcTime)
            return;
        _nextTransformRpcTime = Time.time + TransformSendInterval;

        RpcSubmitTransform(transform.position, transform.rotation);
    }

    /// <summary>Owner to server: writes replicated position and rotation SyncVars.</summary>
    [ServerRpc(RequireOwnership = true)]
    private void RpcSubmitTransform(Vector3 position, Quaternion rotation)
    {
        _replicatedPos.Value = position;
        _replicatedRot.Value = rotation;
    }

    /// <summary>SyncVar hook: forwards position changes to transform and remote buffer logic.</summary>
    private void OnReplicatedPosChanged(Vector3 previous, Vector3 next, bool asServer) =>
        OnReplicatedTransformChanged(asServer);

    /// <summary>SyncVar hook: forwards rotation changes to transform and remote buffer logic.</summary>
    private void OnReplicatedRotChanged(Quaternion previous, Quaternion next, bool asServer) =>
        OnReplicatedTransformChanged(asServer);

    /// <summary>Server snaps transform; clients append samples for interpolation.</summary>
    private void OnReplicatedTransformChanged(bool asServer)
    {
        if (IsOwner)
            return;

        if (asServer && IsServerStarted)
            transform.SetPositionAndRotation(_replicatedPos.Value, _replicatedRot.Value);

        if (!asServer && IsClientStarted)
            TryAppendRemoteSample(Time.time, _replicatedPos.Value, _replicatedRot.Value);
    }

    /// <summary>Duplicates the current SyncVar pose so interpolation has two samples.</summary>
    private void SeedRemoteInterpolationBuffer()
    {
        _remoteSamples.Clear();
        float t = Time.time;
        Vector3 p = _replicatedPos.Value;
        Quaternion r = _replicatedRot.Value;
        _remoteSamples.Add(new RemoteTransformSample(t, p, r));
        _remoteSamples.Add(new RemoteTransformSample(t, p, r));
    }

    /// <summary>Adds a remote sample when pose or time changed meaningfully.</summary>
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

    /// <summary>Drops old remote samples while keeping at least two for interpolation.</summary>
    private void PruneRemoteSamplesBefore(float minTime)
    {
        while (_remoteSamples.Count > 2 && _remoteSamples[1].Time < minTime)
            _remoteSamples.RemoveAt(0);
    }

    /// <summary>Lerps or extrapolates remote transform from the sample buffer at render time.</summary>
    private void ApplyRemoteInterpolation()
    {
        float renderTime = Time.time - Mathf.Max(0f, _remoteInterpolationDelaySeconds);

        // Fall back to latest SyncVars when the buffer is empty or too small.

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

            // Optional short extrapolation past the newest sample using recent velocity.
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

        // Find bracketing samples and lerp between them at renderTime.
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

    /// <summary>Single timestamped pose for remote player interpolation.</summary>
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
