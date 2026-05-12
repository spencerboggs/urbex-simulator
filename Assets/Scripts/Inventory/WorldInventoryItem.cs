using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldInventoryItem : MonoBehaviour
{
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

    private Rigidbody _rigidbody;
    private BoxCollider _boxCollider;
    private Transform _visualRoot;
    private int _networkItemId = -1;

    public InventoryItemType ItemType => _itemType;
    public int NetworkItemId => _networkItemId;

    public string DisplayName => InventoryItemCatalog.GetDisplayName(_itemType);

    public static WorldInventoryItem SpawnDropped(
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Transform droppedBy)
    {
        GameObject go = new GameObject($"{InventoryItemCatalog.GetDisplayName(itemType)}_world_item");
        go.transform.SetPositionAndRotation(position, rotation);

        WorldInventoryItem worldItem = go.AddComponent<WorldInventoryItem>();
        worldItem.Configure(itemType);
        worldItem.IgnorePlayerCollisionTemporarily(droppedBy, 0.35f);
        return worldItem;
    }

    public static WorldInventoryItem SpawnReplicated(
        int networkItemId,
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity)
    {
        if (ReplicatedItems.TryGetValue(networkItemId, out WorldInventoryItem existing) && existing != null)
        {
            existing.transform.SetPositionAndRotation(position, rotation);
            if (existing.TryGetComponent(out Rigidbody existingBody))
                existingBody.linearVelocity = velocity;
            return existing;
        }

        GameObject go = new GameObject($"{InventoryItemCatalog.GetDisplayName(itemType)}_world_item_{networkItemId}");
        go.transform.SetPositionAndRotation(position, rotation);

        WorldInventoryItem worldItem = go.AddComponent<WorldInventoryItem>();
        worldItem._networkItemId = networkItemId;
        worldItem.Configure(itemType);

        if (worldItem.TryGetComponent(out Rigidbody body))
            body.linearVelocity = velocity;

        ReplicatedItems[networkItemId] = worldItem;
        return worldItem;
    }

    public static void DestroyReplicated(int networkItemId)
    {
        if (!ReplicatedItems.TryGetValue(networkItemId, out WorldInventoryItem worldItem))
            return;

        ReplicatedItems.Remove(networkItemId);
        if (worldItem != null)
            Destroy(worldItem.gameObject);
    }

    public void Configure(InventoryItemType itemType)
    {
        _itemType = itemType;
        EnsureSetup();
    }

    private void Awake()
    {
        EnsureSetup();
    }

    private void Reset()
    {
        EnsureSetup();
    }

    private void OnDestroy()
    {
        if (_networkItemId >= 0 &&
            ReplicatedItems.TryGetValue(_networkItemId, out WorldInventoryItem item) &&
            item == this)
        {
            ReplicatedItems.Remove(_networkItemId);
        }
    }

    private void EnsureSetup()
    {
        gameObject.name = $"{DisplayName}_world_item";

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

        EnsureVisual();
    }

    private void EnsureVisual()
    {
        if (_itemType != InventoryItemType.Flashlight)
            return;

        _visualRoot = FlashlightVisualFactory.EnsurePlaceholderVisual(transform);
        if (_visualRoot == null)
            return;

        _visualRoot.localPosition = _visualOffset;
        _visualRoot.localRotation = Quaternion.identity;
        _visualRoot.localScale = Vector3.one;
    }

    public void IgnorePlayerCollisionTemporarily(Transform playerRoot, float durationSeconds)
    {
        if (playerRoot == null || _boxCollider == null)
            return;

        Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>(true);
        if (playerColliders == null || playerColliders.Length == 0)
            return;

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
