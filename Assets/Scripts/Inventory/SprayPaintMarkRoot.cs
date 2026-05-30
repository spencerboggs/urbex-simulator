using UnityEngine;

/// <summary>
/// Identity-scale world root for spray paint overlays so size is never affected by surface transform.
/// </summary>
public static class SprayPaintMarkRoot
{
    private static Transform s_root;

    /// <summary>World-space parent for all spray paint overlay quads.</summary>
    public static Transform Root
    {
        get
        {
            if (s_root != null)
                return s_root;

            GameObject rootObject = new GameObject("__SprayPaintOverlays");
            s_root = rootObject.transform;
            s_root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            s_root.localScale = Vector3.one;
            return s_root;
        }
    }
}
