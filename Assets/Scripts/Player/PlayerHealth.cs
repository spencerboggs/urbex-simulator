using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Server-authoritative health, regeneration, fall damage, and placeholder respawn.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField]
    [Min(1)]
    private int _maxHealth = 100;

    [Header("Regen (server)")]
    [SerializeField]
    [Tooltip("Seconds after last damage before regeneration begins.")]
    private float _healthRegenDelaySeconds = 5f;

    [SerializeField]
    [Tooltip("Health restored per second while regen is active (default 10).")]
    private float _healthRegenPerSecond = 5f;

    [Header("Fall damage (owner reports impact, server validates)")]
    [SerializeField]
    private float _minDownwardSpeedForDamage = 15f;

    [SerializeField]
    [Tooltip("Damage per unit per second of downward speed above the minimum threshold as a linear ramp.")]
    private float _fallDamagePerSpeedOverThreshold = 5f;

    [SerializeField]
    [Tooltip("Clamp on reported downward speed from owner for stability and light anti-cheat.")]
    private float _maxAcceptedFallSpeed = 80f;

    /// <summary>Replicated health value; server-only writes.</summary>
    private readonly SyncVar<int> _health = new(
        100,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 0f, Channel.Reliable));

    /// <summary>Cached CharacterController for grounded checks and teleport.</summary>
    private CharacterController _characterController;
    /// <summary>Cached movement for vertical velocity and fall impact reporting.</summary>
    private PlayerMovement _movement;

    /// <summary>True after server has stored initial spawn pose for respawn.</summary>
    private bool _spawnCaptured;
    /// <summary>World position used for placeholder respawn teleport.</summary>
    private Vector3 _spawnPosition;
    /// <summary>World rotation used for placeholder respawn teleport.</summary>
    private Quaternion _spawnRotation;

    /// <summary>Previous frame grounded state for landing detection on the owner.</summary>
    private bool _wasGroundedLastFrame = true;
    /// <summary>Last vertical velocity while airborne, sampled before landing.</summary>
    private float _lastAirborneVerticalVelocity;

    /// <summary>Time.time when damage last applied; gates regen delay.</summary>
    private float _lastDamageTime = -999f;

    /// <summary>Fractional regen accumulator so integer SyncVar can reach max health.</summary>
    private float _regenCarry;

    /// <summary>Current health as a 0-1 fraction of max health.</summary>
    public float HealthPercent => _maxHealth <= 0 ? 0f : Mathf.Clamp01((float)_health.Value / _maxHealth);

    /// <summary>Current health points.</summary>
    public int CurrentHealth => _health.Value;

    /// <summary>Caches CharacterController and PlayerMovement references.</summary>
    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _movement = GetComponent<PlayerMovement>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (!_spawnCaptured)
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _spawnCaptured = true;
        }

        _health.Value = _maxHealth;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner && _characterController != null)
            _wasGroundedLastFrame = _characterController.isGrounded;
    }

    /// <summary>Runs server-side health regeneration when authoritative.</summary>
    private void Update()
    {
        if (IsServerStarted)
            ServerTickRegen();
    }

    /// <summary>Owner samples airborne velocity and reports fall impact on landing.</summary>
    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner || _movement == null)
            return;

        CharacterController cc = _characterController != null ? _characterController : GetComponent<CharacterController>();
        bool grounded = cc != null && cc.isGrounded;

        // Track last downward speed while in the air.
        if (!grounded)
            _lastAirborneVerticalVelocity = _movement.GetVerticalVelocity();

        // On landing, report impact to server if speed exceeded damage threshold.
        if (grounded && !_wasGroundedLastFrame)
        {
            float vy = _lastAirborneVerticalVelocity;
            if (vy < -_minDownwardSpeedForDamage)
            {
                float downwardSpeed = Mathf.Clamp(-vy, 0f, _maxAcceptedFallSpeed);
                RpcReportFallImpact(downwardSpeed);
            }
        }

        _wasGroundedLastFrame = grounded;
    }

    /// <summary>Applies delayed health regeneration on the server.</summary>
    private void ServerTickRegen()
    {
        if (_health.Value >= _maxHealth)
        {
            _regenCarry = 0f;
            return;
        }

        // Wait for regen delay after last damage.
        if (Time.time - _lastDamageTime < _healthRegenDelaySeconds)
            return;

        // Accumulate fractional health into integer SyncVar steps.
        _regenCarry += _healthRegenPerSecond * Time.deltaTime;
        int add = Mathf.FloorToInt(_regenCarry);
        if (add <= 0)
            return;

        _regenCarry -= add;
        _health.Value = Mathf.Min(_maxHealth, _health.Value + add);
    }

    /// <summary>Applies damage on the server; no-op on clients when spawned.</summary>
    public void RemoveHealth(int amount)
    {
        if (amount <= 0)
            return;
        if (IsSpawned && !IsServerStarted)
            return;

        ApplyDamageAuthority(amount);
    }

    /// <inheritdoc />
    public void ApplyDamage(int amount) => RemoveHealth(amount);

    /// <summary>Subtracts health on the server, resets regen carry, and triggers death at zero.</summary>
    private void ApplyDamageAuthority(int amount)
    {
        if (!IsServerStarted)
            return;

        int next = Mathf.Max(0, _health.Value - amount);
        _health.Value = next;
        _lastDamageTime = Time.time;
        _regenCarry = 0f;

        if (next <= 0)
            HandleDeathServer();
    }

    /// <summary>Placeholder respawn: refills health and teleports all clients to spawn pose.</summary>
    private void HandleDeathServer()
    {
        // Placeholder respawn at initial spawn pose until a full death flow exists.
        _health.Value = _maxHealth;
        ObserversApplyRespawnTeleport(_spawnPosition, _spawnRotation);
    }

    /// <summary>Owner-reported landing speed; server converts excess speed to fall damage.</summary>
    [ServerRpc(RequireOwnership = true)]
    private void RpcReportFallImpact(float downwardSpeed)
    {
        if (downwardSpeed < _minDownwardSpeedForDamage)
            return;

        // Linear damage ramp above minimum impact speed.
        int damage = Mathf.RoundToInt((downwardSpeed - _minDownwardSpeedForDamage) * _fallDamagePerSpeedOverThreshold);
        if (damage > 0)
            ApplyDamageAuthority(damage);
    }

    /// <summary>Teleports this player locally to the server respawn pose.</summary>
    [ObserversRpc]
    private void ObserversApplyRespawnTeleport(Vector3 position, Quaternion rotation)
    {
        ApplyTeleportLocal(position, rotation);
    }

    /// <summary>Disables controller briefly, moves transform, and clears vertical velocity.</summary>
    private void ApplyTeleportLocal(Vector3 position, Quaternion rotation)
    {
        if (_characterController != null)
        {
            // Disable controller so SetPositionAndRotation does not fight internal state.
            _characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _characterController.enabled = true;
        }
        else
            transform.SetPositionAndRotation(position, rotation);

        if (_movement != null)
            _movement.ResetVerticalVelocity();
    }
}
