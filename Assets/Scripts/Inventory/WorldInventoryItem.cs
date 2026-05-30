using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Physics-backed world pickup/drop instance for an inventory item type.</summary>
[DisallowMultipleComponent]
public sealed class WorldInventoryItem : MonoBehaviour
{
    /// <summary>Network id to instance map for replicated world items on this client.</summary>
    private static readonly Dictionary<int, WorldInventoryItem> ReplicatedItems = new();

    [SerializeField]
    private InventoryItemType _itemType = InventoryItemType.Flashlight;

    [Header("Collision")]
    [SerializeField]
    private Vector3 _colliderCenter = new(0f, 0f, 0.03f);

    [SerializeField]
    private Vector3 _colliderSize = new(0.12f, 0.1f, 0.34f);

    [Header("Visual")]
    [SerializeField]
    private Vector3 _visualOffset = new(0f, -0.015f, 0f);

    /// <summary>Physics body for dropped and replicated items.</summary>
    private Rigidbody _rigidbody;

    /// <summary>Pickup and collision volume for this world item.</summary>
    private BoxCollider _boxCollider;

    /// <summary>Child transform holding procedural or prefab visuals.</summary>
    private Transform _visualRoot;

    /// <summary>Replicated spawn id, or -1 for local-only instances.</summary>
    private int _networkItemId = -1;

    /// <summary>Item type for this instance.</summary>
    public InventoryItemType ItemType => _itemType;

    /// <summary>Replicated id when spawned over the network; -1 for local-only.</summary>
    public int NetworkItemId => _networkItemId;

    /// <summary>Player-facing name from the catalog.</summary>
    public string DisplayName => InventoryItemCatalog.GetDisplayName(_itemType);

    /// <summary>Spawns a dropped item with brief collision ignore against the dropper.</summary>
    public static WorldInventoryItem SpawnDropped(
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Transform droppedBy)
    {
        WorldInventoryItem worldItem = Spawn(itemType, position, rotation, networkItemId: -1);
        if (worldItem != null)
            worldItem.IgnorePlayerCollisionTemporarily(droppedBy, 0.35f);
        return worldItem;
    }

    /// <summary>Spawns or updates a networked world item by id.</summary>
    public static WorldInventoryItem SpawnReplicated(
        int networkItemId,
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity)
    {
        // Update an existing replicated instance in place instead of spawning a duplicate.
        if (ReplicatedItems.TryGetValue(networkItemId, out WorldInventoryItem existing) && existing != null)
        {
            existing.transform.SetPositionAndRotation(position, rotation);
            if (existing.TryGetComponent(out Rigidbody existingBody))
                existingBody.linearVelocity = velocity;
            return existing;
        }

        WorldInventoryItem worldItem = Spawn(itemType, position, rotation, networkItemId);
        if (worldItem == null)
            return null;

        if (worldItem.TryGetComponent(out Rigidbody body))
            body.linearVelocity = velocity;

        ReplicatedItems[networkItemId] = worldItem;
        return worldItem;
    }

    /// <summary>Destroys the replicated instance for <paramref name="networkItemId"/>.</summary>
    public static void DestroyReplicated(int networkItemId)
    {
        if (!ReplicatedItems.TryGetValue(networkItemId, out WorldInventoryItem worldItem))
            return;

        ReplicatedItems.Remove(networkItemId);
        if (worldItem != null)
            Destroy(worldItem.gameObject);
    }

    /// <summary>Instantiates from the prefab catalog, or falls back to a procedural placeholder.</summary>
    private static WorldInventoryItem Spawn(
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        int networkItemId)
    {
        ItemPrefabCatalog catalog = ItemPrefabCatalog.Load();
        if (catalog != null && catalog.TryGetPrefab(itemType, out WorldInventoryItem prefab) && prefab != null)
        {
            WorldInventoryItem instance = Instantiate(prefab, position, rotation);
            instance.FinalizeSpawn(itemType, networkItemId, useProceduralVisualFallback: false);
            return instance;
        }

        Debug.LogWarning(
            $"[WorldInventoryItem] No prefab in ItemPrefabCatalog for {itemType}. " +
            $"Add Assets/Prefabs/Items/{InventoryItemCatalog.GetDisplayName(itemType)}.prefab " +
            "and run Tools/Urbex/Refresh Item Prefab Catalog.");

        return SpawnProceduralFallback(itemType, position, rotation, networkItemId);
    }

    /// <summary>Creates a minimal runtime GameObject when no catalog prefab is available.</summary>
    private static WorldInventoryItem SpawnProceduralFallback(
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        int networkItemId)
    {
        GameObject go = new GameObject($"{InventoryItemCatalog.GetDisplayName(itemType)}_world_item");
        go.transform.SetPositionAndRotation(position, rotation);

        WorldInventoryItem worldItem = go.AddComponent<WorldInventoryItem>();
        worldItem.FinalizeSpawn(itemType, networkItemId, useProceduralVisualFallback: true);
        return worldItem;
    }

    /// <summary>Reconfigures type and rebuilds procedural visuals if needed.</summary>
    public void Configure(InventoryItemType itemType)
    {
        FinalizeSpawn(itemType, _networkItemId, useProceduralVisualFallback: true);
    }

    /// <summary>Applies item type, network id, physics, and optional procedural visuals after spawn.</summary>
    private void FinalizeSpawn(InventoryItemType itemType, int networkItemId, bool useProceduralVisualFallback)
    {
        _itemType = itemType;
        _networkItemId = networkItemId;
        EnsurePhysicsComponents();

        if (useProceduralVisualFallback)
            EnsureProceduralVisual();

        gameObject.name = $"{DisplayName}_world_item";
    }

    /// <summary>Ensures physics components exist when the object is created in the scene.</summary>
    private void Awake()
    {
        EnsurePhysicsComponents();
    }

    /// <summary>Unity editor callback to add physics components on reset.</summary>
    private void Reset()
    {
        EnsurePhysicsComponents();
    }

    /// <summary>Removes this instance from the replicated lookup when destroyed.</summary>
    private void OnDestroy()
    {
        if (_networkItemId >= 0 &&
            ReplicatedItems.TryGetValue(_networkItemId, out WorldInventoryItem item) &&
            item == this)
        {
            ReplicatedItems.Remove(_networkItemId);
        }
    }

    /// <summary>Adds or configures BoxCollider and Rigidbody with drop-friendly settings.</summary>
    private void EnsurePhysicsComponents()
    {
        if (_boxCollider == null && !TryGetComponent(out _boxCollider))
            _boxCollider = gameObject.AddComponent<BoxCollider>();

        if (_rigidbody == null && !TryGetComponent(out _rigidbody))
            _rigidbody = gameObject.AddComponent<Rigidbody>();

        _boxCollider.center = _colliderCenter;
        _boxCollider.size = _colliderSize;
        _boxCollider.isTrigger = false;

        _rigidbody.mass = 0.75f;
        _rigidbody.useGravity = true;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    /// <summary>Attaches a procedural flashlight mesh when no art prefab visual exists.</summary>
    private void EnsureProceduralVisual()
    {
        if (_itemType != InventoryItemType.Flashlight &&
            _itemType != InventoryItemType.SprayPaint &&
            _itemType != InventoryItemType.PaintballGun)
        {
            return;
        }

        if (_itemType == InventoryItemType.Flashlight)
        {
            _visualRoot = FlashlightVisualFactory.EnsurePlaceholderVisual(transform);
        }
        else if (_itemType == InventoryItemType.SprayPaint)
        {
            _visualRoot = SprayPaintVisualFactory.EnsurePlaceholderVisual(transform);
        }
        else
        {
            _visualRoot = PaintballGunVisualFactory.EnsurePlaceholderVisual(transform);
        }
        if (_visualRoot == null)
            return;

        _visualRoot.localPosition = _visualOffset;
        _visualRoot.localRotation = Quaternion.identity;
        _visualRoot.localScale = Vector3.one;
    }

    /// <summary>Temporarily disables collisions between this item and the player hierarchy.</summary>
    public void IgnorePlayerCollisionTemporarily(Transform playerRoot, float durationSeconds)
    {
        if (playerRoot == null || _boxCollider == null)
            return;

        Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>(true);
        if (playerColliders == null || playerColliders.Length == 0)
            return;

        // Ignore every player collider briefly so the drop does not immediately push the player.
        List<Collider> ignored = new List<Collider>(playerColliders.Length);
        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider other = playerColliders[i];
            if (other == null || other == _boxCollider)
                continue;

            Physics.IgnoreCollision(_boxCollider, other, true);
            ignored.Add(other);
        }

        if (ignored.Count > 0 && durationSeconds > 0f)
            StartCoroutine(RestoreIgnoredCollisionsAfterDelay(ignored, durationSeconds));
    }

    /// <summary>Re-enables collisions with the player after the temporary ignore window expires.</summary>
    private IEnumerator RestoreIgnoredCollisionsAfterDelay(List<Collider> ignoredColliders, float durationSeconds)
    {
        yield return new WaitForSeconds(durationSeconds);

        if (_boxCollider == null)
            yield break;

        for (int i = 0; i < ignoredColliders.Count; i++)
        {
            Collider other = ignoredColliders[i];
            if (other != null)
                Physics.IgnoreCollision(_boxCollider, other, false);
        }
    }
}
