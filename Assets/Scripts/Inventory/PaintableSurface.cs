using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Receives spray paint on any collider via accumulated render texture canvases per facing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PaintableSurface : MonoBehaviour
{
    private const float SurfaceOffset = 0.006f;
    private const float BoundsPadding = 0.12f;
    private const int CanvasResolution = 512;
    private const float NormalMatchDot = 0.82f;
    private const int MaxFaceCanvases = 16;

    private Collider _collider;
    private readonly List<PaintFaceCanvas> _faceCanvases = new List<PaintFaceCanvas>(4);

    /// <summary>
    /// Returns or creates a paintable surface on the collider's GameObject.
    /// </summary>
    public static PaintableSurface GetOrCreate(Collider collider)
    {
        if (collider == null)
            return null;

        if (!collider.TryGetComponent(out PaintableSurface surface))
            surface = collider.gameObject.AddComponent<PaintableSurface>();

        return surface;
    }

    /// <summary>
    /// Stamps a soft circular spray mark into the face canvas matching the hit normal.
    /// </summary>
    public bool AddMark(Vector3 worldPoint, Vector3 worldNormal, Color color, float diameter, float layerOpacity)
    {
        if (diameter <= 0f)
            return false;

        if (_collider == null)
            _collider = GetComponent<Collider>();

        if (_collider == null)
            return false;

        Vector3 normal = worldNormal.sqrMagnitude > 0.0001f ? worldNormal.normalized : Vector3.up;
        PaintFaceCanvas canvas = GetOrCreateFaceCanvas(normal, worldPoint);
        if (canvas == null)
            return false;

        return canvas.TryStamp(worldPoint, color, diameter, layerOpacity);
    }

    /// <summary>
    /// Releases all face canvases when the surface is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        for (int i = 0; i < _faceCanvases.Count; i++)
            _faceCanvases[i].Dispose();

        _faceCanvases.Clear();
    }

    /// <summary>
    /// Finds a canvas aligned with the hit normal or creates one anchored at the hit point.
    /// </summary>
    private PaintFaceCanvas GetOrCreateFaceCanvas(Vector3 normal, Vector3 worldPoint)
    {
        PaintFaceCanvas bestMatch = null;
        float bestDot = float.MinValue;

        for (int i = 0; i < _faceCanvases.Count; i++)
        {
            PaintFaceCanvas candidate = _faceCanvases[i];
            float dot = Vector3.Dot(candidate.PlaneNormal, normal);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestMatch = candidate;
            }
        }

        if (bestMatch != null && bestDot >= NormalMatchDot)
            return bestMatch;

        if (_faceCanvases.Count >= MaxFaceCanvases)
            return bestMatch;

        PaintFaceCanvas created = PaintFaceCanvas.Create(_collider, normal, worldPoint);
        if (created == null)
            return bestMatch;

        _faceCanvases.Add(created);
        return created;
    }

    /// <summary>
    /// One render-texture canvas aligned to a collider face near the painted surface.
    /// </summary>
    private sealed class PaintFaceCanvas
    {
        private const float SurfaceOffset = PaintableSurface.SurfaceOffset;
        private const float BoundsPadding = PaintableSurface.BoundsPadding;
        private const int CanvasResolution = PaintableSurface.CanvasResolution;

        private static readonly Vector3[] SamplePointsBuffer = new Vector3[8];

        private Vector3 _planeRight;
        private Vector3 _planeUp;
        private Vector3 _planeCenter;
        private float _worldWidth;
        private float _worldHeight;

        private RenderTexture _paintTexture;
        private RenderTexture _stampScratch;
        private Material _canvasMaterial;
        private GameObject _overlayObject;

        /// <summary>Outward-facing normal for this canvas.</summary>
        public Vector3 PlaneNormal { get; private set; }

        /// <summary>
        /// Builds GPU resources and a world overlay for one collider face.
        /// </summary>
        public static PaintFaceCanvas Create(Collider collider, Vector3 normal, Vector3 referenceWorldPoint)
        {
            if (collider == null)
                return null;

            if (!SprayPaintBrush.IsReady)
                _ = SprayPaintBrush.BrushTexture;

            if (!SprayPaintBrush.IsReady)
                return null;

            PaintFaceCanvas canvas = new PaintFaceCanvas
            {
                PlaneNormal = normal.normalized,
            };

            BuildTangentFrame(canvas.PlaneNormal, out canvas._planeRight, out canvas._planeUp);
            canvas.ComputeFaceRect(collider, referenceWorldPoint, out canvas._planeCenter, out canvas._worldWidth, out canvas._worldHeight);

            if (canvas._worldWidth <= 0.01f || canvas._worldHeight <= 0.01f)
                return null;

            canvas._paintTexture = CreatePaintRenderTexture(CanvasResolution);
            canvas._stampScratch = CreatePaintRenderTexture(CanvasResolution);
            ClearRenderTexture(canvas._paintTexture);

            canvas._canvasMaterial = SprayPaintBrush.CreateCanvasMaterial(canvas._paintTexture);
            if (canvas._canvasMaterial == null)
            {
                canvas.Dispose();
                return null;
            }

            canvas.CreateOverlay(collider.name);
            return canvas;
        }

        /// <summary>
        /// Stamps paint at a world hit point when it falls inside this face canvas.
        /// </summary>
        public bool TryStamp(Vector3 worldPoint, Color color, float diameter, float layerOpacity)
        {
            if (_paintTexture == null || _stampScratch == null)
                return false;

            Vector2 uv = WorldToUv(worldPoint);
            if (uv.x < -0.05f || uv.x > 1.05f || uv.y < -0.05f || uv.y > 1.05f)
                return false;

            float safeDiameter = Mathf.Max(0.02f, diameter);
            Color stampColor = color;
            stampColor.a *= Mathf.Clamp01(layerOpacity);

            Vector2 halfExtent = new Vector2(
                safeDiameter / (_worldWidth * 2f),
                safeDiameter / (_worldHeight * 2f));

            if (!SprayPaintBrush.BlitStamp(_paintTexture, _stampScratch, uv, halfExtent, stampColor))
                return false;

            Graphics.Blit(_stampScratch, _paintTexture);
            return true;
        }

        /// <summary>
        /// Destroys GPU resources and the overlay object.
        /// </summary>
        public void Dispose()
        {
            ReleaseRenderTexture(ref _paintTexture);
            ReleaseRenderTexture(ref _stampScratch);

            if (_canvasMaterial != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_canvasMaterial);
                else
                    Object.DestroyImmediate(_canvasMaterial);
            }

            if (_overlayObject != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_overlayObject);
                else
                    Object.DestroyImmediate(_overlayObject);
            }
        }

        /// <summary>
        /// Places the display quad flush with the painted surface.
        /// </summary>
        private void CreateOverlay(string colliderName)
        {
            _overlayObject = new GameObject($"SprayPaintOverlay_{colliderName}");
            Transform overlayTransform = _overlayObject.transform;
            overlayTransform.SetParent(SprayPaintMarkRoot.Root, false);
            overlayTransform.SetPositionAndRotation(
                _planeCenter + PlaneNormal * SurfaceOffset,
                Quaternion.LookRotation(PlaneNormal, _planeUp));
            overlayTransform.localScale = new Vector3(_worldWidth, _worldHeight, 1f);

            MeshFilter meshFilter = _overlayObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = SprayPaintBrush.UnitQuadMesh;

            MeshRenderer renderer = _overlayObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _canvasMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// Derives canvas size and center by projecting oriented collider corners onto the hit plane.
        /// </summary>
        private void ComputeFaceRect(Collider collider, Vector3 referenceWorldPoint, out Vector3 center, out float width, out float height)
        {
            int pointCount = CollectColliderWorldPoints(collider, SamplePointsBuffer);
            Vector3 anchor = referenceWorldPoint;

            float minU = float.MaxValue;
            float maxU = float.MinValue;
            float minV = float.MaxValue;
            float maxV = float.MinValue;

            for (int i = 0; i < pointCount; i++)
            {
                Vector3 point = SamplePointsBuffer[i];
                Vector3 onPlane = point - PlaneNormal * Vector3.Dot(point - anchor, PlaneNormal);
                Vector3 relative = onPlane - anchor;
                float u = Vector3.Dot(relative, _planeRight);
                float v = Vector3.Dot(relative, _planeUp);
                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, v);
                maxV = Mathf.Max(maxV, v);
            }

            width = Mathf.Max(0.1f, (maxU - minU) * (1f + BoundsPadding));
            height = Mathf.Max(0.1f, (maxV - minV) * (1f + BoundsPadding));

            float midU = (minU + maxU) * 0.5f;
            float midV = (minV + maxV) * 0.5f;
            center = anchor + _planeRight * midU + _planeUp * midV;
        }

        /// <summary>
        /// Collects oriented collider corners in world space so scale and rotation are respected.
        /// </summary>
        private static int CollectColliderWorldPoints(Collider collider, Vector3[] buffer)
        {
            if (collider is BoxCollider box)
                return FillOrientedBoxCorners(box, buffer);

            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
                return FillMeshBoundsCorners(meshCollider, buffer);

            return FillAxisAlignedBoundsCorners(collider.bounds, buffer);
        }

        /// <summary>
        /// Writes world-space corners for a scaled and rotated box collider.
        /// </summary>
        private static int FillOrientedBoxCorners(BoxCollider box, Vector3[] buffer)
        {
            Transform transform = box.transform;
            Vector3 localCenter = box.center;
            Vector3 localExtents = box.size * 0.5f;
            return FillLocalBoxCorners(transform, localCenter, localExtents, buffer);
        }

        /// <summary>
        /// Writes world-space corners for a mesh collider's local bounds.
        /// </summary>
        private static int FillMeshBoundsCorners(MeshCollider meshCollider, Vector3[] buffer)
        {
            Bounds localBounds = meshCollider.sharedMesh.bounds;
            return FillLocalBoxCorners(meshCollider.transform, localBounds.center, localBounds.extents, buffer);
        }

        /// <summary>
        /// Transforms eight local box corners into world space.
        /// </summary>
        private static int FillLocalBoxCorners(Transform transform, Vector3 localCenter, Vector3 localExtents, Vector3[] buffer)
        {
            int index = 0;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = localCenter + Vector3.Scale(localExtents, new Vector3(x, y, z));
                        buffer[index++] = transform.TransformPoint(localCorner);
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Fallback for collider types without an oriented shape.
        /// </summary>
        private static int FillAxisAlignedBoundsCorners(Bounds bounds, Vector3[] buffer)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            int index = 0;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        buffer[index++] = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Converts a world hit point into normalized canvas UV coordinates.
        /// </summary>
        private Vector2 WorldToUv(Vector3 worldPoint)
        {
            Vector3 relative = worldPoint - _planeCenter;
            relative -= PlaneNormal * Vector3.Dot(relative, PlaneNormal);

            float u = Vector3.Dot(relative, _planeRight) / _worldWidth + 0.5f;
            float v = Vector3.Dot(relative, _planeUp) / _worldHeight + 0.5f;
            return new Vector2(u, v);
        }

        private static RenderTexture CreatePaintRenderTexture(int resolution)
        {
            RenderTexture texture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SprayPaintCanvas",
                useMipMap = false,
                autoGenerateMips = false,
            };
            texture.Create();
            return texture;
        }

        private static void ClearRenderTexture(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);

            texture = null;
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
}
