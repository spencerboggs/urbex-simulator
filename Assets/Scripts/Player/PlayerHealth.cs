using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

// Server-authoritative health and regen
// Fall damage reporting and placeholder respawn
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
    [Tooltip("Damage per unit per second of downward speed above the minimum threshold as a linear ramp")]
    private float _fallDamagePerSpeedOverThreshold = 5f;

    [SerializeField]
    [Tooltip("Clamp on reported downward speed from owner for stability and light anti-cheat")]
    private float _maxAcceptedFallSpeed = 80f;

    private readonly SyncVar<int> _health = new(
        100,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers, 0f, Channel.Reliable));

    private CharacterController _characterController;
    private PlayerMovement _movement;

    private bool _spawnCaptured;
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;

    private bool _wasGroundedLastFrame = true;
    private float _lastAirborneVerticalVelocity;

    private float _lastDamageTime = -999f;

    // Integer SyncVar with fractional regen each frame can stall one below max health
    private float _regenCarry;

    public float HealthPercent => _maxHealth <= 0 ? 0f : Mathf.Clamp01((float)_health.Value / _maxHealth);
    public int CurrentHealth => _health.Value;

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

    private void Update()
    {
        if (IsServerStarted)
            ServerTickRegen();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner || _movement == null)
            return;

        CharacterController cc = _characterController != null ? _characterController : GetComponent<CharacterController>();
        bool grounded = cc != null && cc.isGrounded;

        if (!grounded)
            _lastAirborneVerticalVelocity = _movement.GetVerticalVelocity();

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

    private void ServerTickRegen()
    {
        if (_health.Value >= _maxHealth)
        {
            _regenCarry = 0f;
            return;
        }

        if (Time.time - _lastDamageTime < _healthRegenDelaySeconds)
            return;

        _regenCarry += _healthRegenPerSecond * Time.deltaTime;
        int add = Mathf.FloorToInt(_regenCarry);
        if (add <= 0)
            return;

        _regenCarry -= add;
        _health.Value = Mathf.Min(_maxHealth, _health.Value + add);
    }

    // Server-only subtract for falls hazards and future AI
    // Clients no-op here
    public void RemoveHealth(int amount)
    {
        if (amount <= 0)
            return;
        if (IsSpawned && !IsServerStarted)
            return;

        ApplyDamageAuthority(amount);
    }

    public void ApplyDamage(int amount) => RemoveHealth(amount);

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

    private void HandleDeathServer()
    {
        // Temporary snap to initial spawn pose
        // Replace later with death revive or spectate flow
        _health.Value = _maxHealth;
        ObserversApplyRespawnTeleport(_spawnPosition, _spawnRotation);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RpcReportFallImpact(float downwardSpeed)
    {
        if (downwardSpeed < _minDownwardSpeedForDamage)
            return;

        int damage = Mathf.RoundToInt((downwardSpeed - _minDownwardSpeedForDamage) * _fallDamagePerSpeedOverThreshold);
        if (damage > 0)
            ApplyDamageAuthority(damage);
    }

    [ObserversRpc]
    private void ObserversApplyRespawnTeleport(Vector3 position, Quaternion rotation)
    {
        ApplyTeleportLocal(position, rotation);
    }

    private void ApplyTeleportLocal(Vector3 position, Quaternion rotation)
    {
        if (_characterController != null)
        {
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
