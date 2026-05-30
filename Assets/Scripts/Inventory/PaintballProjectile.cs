using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics paintball that leaves a colored mark on the first solid collision.
/// </summary>
[DisallowMultipleComponent]
public sealed class PaintballProjectile : MonoBehaviour
{
    private const float DefaultBallRadius = 0.035f;

    [SerializeField]
    private float _maxLifetimeSeconds = 10f;

    private Color _paintColor;
    private float _markDiameter;
    private float _markOpacity;
    private Transform _shooterRoot;
    private bool _resolved;
    private float _spawnTime;

    /// <summary>
    /// Spawns a physics paintball with brief collision ignore against the shooter.
    /// </summary>
    public static PaintballProjectile Spawn(
        Vector3 position,
        Vector3 velocity,
        Color paintColor,
        float markDiameter,
        float markOpacity,
        Transform shooterRoot,
        float ballRadius = DefaultBallRadius)
    {
        GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectileObject.name = "Paintball";

        Collider primitiveCollider = projectileObject.GetComponent<Collider>();
        if (primitiveCollider != null)
            Destroy(primitiveCollider);

        SphereCollider collider = projectileObject.AddComponent<SphereCollider>();
        collider.radius = 0.5f;

        Rigidbody body = projectileObject.AddComponent<Rigidbody>();
        body.mass = 0.045f;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity;

        if (projectileObject.TryGetComponent(out Renderer renderer))
        {
            Material material = new Material(renderer.sharedMaterial);
            material.color = paintColor;
            renderer.sharedMaterial = material;
        }

        PaintballProjectile projectile = projectileObject.AddComponent<PaintballProjectile>();
        projectile.Initialize(paintColor, markDiameter, markOpacity, shooterRoot);
        projectileObject.transform.localScale = Vector3.one * (ballRadius * 2f);
        projectileObject.transform.position = position;
        projectile.IgnoreShooterCollisionsTemporarily(0.3f);
        return projectile;
    }

    /// <summary>
    /// Stores paint settings used when this projectile collides.
    /// </summary>
    private void Initialize(Color paintColor, float markDiameter, float markOpacity, Transform shooterRoot)
    {
        _paintColor = paintColor;
        _markDiameter = markDiameter;
        _markOpacity = markOpacity;
        _shooterRoot = shooterRoot;
        _spawnTime = Time.time;
    }

    /// <summary>
    /// Destroys the projectile when it exceeds its lifetime without hitting anything.
    /// </summary>
    private void Update()
    {
        if (Time.time - _spawnTime >= _maxLifetimeSeconds)
            Destroy(gameObject);
    }

    /// <summary>
    /// Applies a paint splat and destroys the projectile on first valid impact.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (_resolved || collision == null)
            return;

        Collider otherCollider = collision.collider;
        if (otherCollider == null || otherCollider.isTrigger)
            return;

        if (otherCollider.GetComponent<PaintballProjectile>() != null)
            return;

        if (collision.contactCount <= 0)
            return;

        ContactPoint contact = collision.GetContact(0);
        TryApplyPaint(contact.point, contact.normal, otherCollider);
        _resolved = true;
        Destroy(gameObject);
    }

    /// <summary>
    /// Writes a paint mark through the shared paintable surface system.
    /// </summary>
    private void TryApplyPaint(Vector3 point, Vector3 normal, Collider collider)
    {
        PaintableSurface surface = PaintableSurface.GetOrCreate(collider);
        if (surface == null)
            return;

        surface.AddMark(point, normal, _paintColor, _markDiameter, _markOpacity);
    }

    /// <summary>
    /// Temporarily ignores collisions with the shooter's colliders.
    /// </summary>
    private void IgnoreShooterCollisionsTemporarily(float durationSeconds)
    {
        if (_shooterRoot == null || !TryGetComponent(out Collider projectileCollider))
            return;

        Collider[] shooterColliders = _shooterRoot.GetComponentsInChildren<Collider>(true);
        if (shooterColliders == null || shooterColliders.Length == 0)
            return;

        List<Collider> ignored = new List<Collider>(shooterColliders.Length);
        for (int i = 0; i < shooterColliders.Length; i++)
        {
            Collider other = shooterColliders[i];
            if (other == null || other == projectileCollider || other.isTrigger)
                continue;

            Physics.IgnoreCollision(projectileCollider, other, true);
            ignored.Add(other);
        }

        if (ignored.Count > 0 && durationSeconds > 0f)
            StartCoroutine(RestoreIgnoredCollisionsAfterDelay(ignored, durationSeconds));
    }

    /// <summary>
    /// Re-enables collisions with ignored shooter colliders after the spawn window.
    /// </summary>
    private IEnumerator RestoreIgnoredCollisionsAfterDelay(List<Collider> ignoredColliders, float durationSeconds)
    {
        yield return new WaitForSeconds(durationSeconds);

        if (!TryGetComponent(out Collider projectileCollider))
            yield break;

        for (int i = 0; i < ignoredColliders.Count; i++)
        {
            Collider other = ignoredColliders[i];
            if (other != null)
                Physics.IgnoreCollision(projectileCollider, other, false);
        }
    }
}
