using UnityEngine;

/// <summary>
/// Shared paint raycast helpers that skip the spraying player's colliders.
/// </summary>
public static class SprayPaintRaycast
{
    private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

    private static readonly Vector3[] FootprintOffsets =
    {
        Vector3.zero,
        new Vector3(1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, -1f, 0f),
        new Vector3(0.707f, 0.707f, 0f),
        new Vector3(-0.707f, 0.707f, 0f),
        new Vector3(0.707f, -0.707f, 0f),
        new Vector3(-0.707f, -0.707f, 0f),
    };

    /// <summary>
    /// Raycasts from the camera forward and returns the first hit that is not part of the player.
    /// </summary>
    public static bool TryGetTarget(
        Camera camera,
        Transform playerRoot,
        float maxDistance,
        out RaycastHit hit)
    {
        hit = default;
        if (camera == null)
            return false;

        Ray ray = new Ray(camera.transform.position, camera.transform.forward);
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            HitBuffer,
            maxDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = HitBuffer[i];
            if (candidate.collider == null)
                continue;

            if (IsPlayerCollider(candidate.collider, playerRoot))
                continue;

            if (candidate.distance >= bestDistance)
                continue;

            bestDistance = candidate.distance;
            hit = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Re-samples an approximate point onto the expected collider along the surface normal.
    /// </summary>
    public static bool TryResolvePointOnCollider(
        Vector3 approximatePoint,
        Vector3 normal,
        Collider collider,
        out RaycastHit hit)
    {
        hit = default;
        if (collider == null)
            return false;

        Vector3 surfaceNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        Vector3 probeOrigin = approximatePoint + surfaceNormal * 0.06f;
        if (!Physics.Raycast(
                probeOrigin,
                -surfaceNormal,
                out hit,
                0.18f,
                ~0,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return hit.collider == collider;
    }

    /// <summary>
    /// True when the full brush footprint lies on the same collider (prevents marks hanging over edges).
    /// </summary>
    public static bool IsBrushFootprintOnSurface(
        Vector3 center,
        Vector3 normal,
        Collider collider,
        float diameter)
    {
        if (collider == null || diameter <= 0f)
            return false;

        Vector3 surfaceNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        BuildTangentFrame(surfaceNormal, out Vector3 tangent, out Vector3 bitangent);

        float sampleRadius = diameter * 0.42f;
        const float probeLift = 0.05f;
        const float probeDistance = 0.16f;
        const float maxPlaneError = 0.035f;

        for (int i = 0; i < FootprintOffsets.Length; i++)
        {
            Vector3 offset = tangent * FootprintOffsets[i].x + bitangent * FootprintOffsets[i].y;
            offset *= sampleRadius;

            Vector3 probeOrigin = center + offset + surfaceNormal * probeLift;
            if (!Physics.Raycast(
                    probeOrigin,
                    -surfaceNormal,
                    out RaycastHit sampleHit,
                    probeDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (sampleHit.collider != collider)
                return false;

            float planeError = Mathf.Abs(Vector3.Dot(sampleHit.point - center, surfaceNormal));
            if (planeError > maxPlaneError)
                return false;
        }

        return true;
    }

    /// <summary>
    /// True when the collider belongs to the player hierarchy.</summary>
    public static bool IsPlayerCollider(Collider collider, Transform playerRoot)
    {
        if (collider == null || playerRoot == null)
            return false;

        Transform hitTransform = collider.transform;
        return hitTransform == playerRoot || hitTransform.IsChildOf(playerRoot);
    }

    private static void BuildTangentFrame(Vector3 normal, out Vector3 tangent, out Vector3 bitangent)
    {
        tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(normal, Vector3.right);

        tangent.Normalize();
        bitangent = Vector3.Cross(normal, tangent).normalized;
    }
}
