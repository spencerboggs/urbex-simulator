using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

// Server-authoritative health, regen, fall damage reporting, and a placeholder respawn
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
    private float _healthRegenDelaySeconds = 3f;

    [SerializeField]
    [Tooltip("Health restored per second while regen is active (default 10).")]
    private float _healthRegenPerSecond = 10f;

    [Header("Fall damage (owner reports impact; server validates)")]
    [SerializeField]
    private float _minDownwardSpeedForDamage = 15f;

    [SerializeField]
    private float _fallDamagePerSpeedOverThreshold = 2f;

    [SerializeField]
    [Tooltip("Clamp on reported downward speed from owner (stability / light anti-cheat).")]
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
            return;
        if (Time.time - _lastDamageTime < _healthRegenDelaySeconds)
            return;

        float next = Mathf.Min(_maxHealth, _health.Value + _healthRegenPerSecond * Time.deltaTime);
        _health.Value = Mathf.RoundToInt(next);
    }

    // Server-only: subtract health (falls, hazards, future hostile AI). Clients no-op
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

        if (next <= 0)
            HandleDeathServer();
    }

    private void HandleDeathServer()
    {
        // Temporary: snap everyone to the initial spawn pose until a proper death / revive / spectate flow exists
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
