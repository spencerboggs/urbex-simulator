using UnityEngine;

/// <summary>Procedural placeholder mesh for spray paint can visuals.</summary>
public static class SprayPaintVisualFactory
{
    /// <summary>Creates or returns an existing placeholder visual under <paramref name="parent"/>.</summary>
    public static Transform EnsurePlaceholderVisual(Transform parent, string rootName = "__SprayPaintVisual")
    {
        if (parent == null)
            return null;

        Transform existing = parent.Find(rootName);
        if (existing != null)
            return existing;

        GameObject root = new GameObject(rootName);
        Transform rootTransform = root.transform;
        rootTransform.SetParent(parent, false);

        CreatePart(
            PrimitiveType.Cylinder,
            "CanBody",
            rootTransform,
            new Vector3(0f, 0f, 0.02f),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector3(0.05f, 0.09f, 0.05f),
            new Color(0.85f, 0.85f, 0.85f, 1f));

        CreatePart(
            PrimitiveType.Cylinder,
            "Cap",
            rootTransform,
            new Vector3(0f, 0f, 0.11f),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector3(0.028f, 0.025f, 0.028f),
            new Color(0.2f, 0.2f, 0.2f, 1f));

        CreatePart(
            PrimitiveType.Cube,
            "Nozzle",
            rootTransform,
            new Vector3(0f, 0.035f, 0.125f),
            Quaternion.identity,
            new Vector3(0.02f, 0.02f, 0.05f),
            new Color(0.15f, 0.15f, 0.15f, 1f));

        return rootTransform;
    }

    /// <summary>Creates a tinted primitive child mesh without a collider.</summary>
    private static void CreatePart(
        PrimitiveType primitiveType,
        string name,
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Color color)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = name;
        Transform partTransform = part.transform;
        partTransform.SetParent(parent, false);
        partTransform.localPosition = localPosition;
        partTransform.localRotation = localRotation;
        partTransform.localScale = localScale;

        if (part.TryGetComponent(out Renderer renderer))
        {
            Material material = new Material(renderer.sharedMaterial);
            material.color = color;
            renderer.sharedMaterial = material;
        }

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
                Object.Destroy(collider);
            else
                Object.DestroyImmediate(collider);
        }
    }
}
