using UnityEngine;

/// <summary>Procedural placeholder mesh for paintball gun visuals.</summary>
public static class PaintballGunVisualFactory
{
    /// <summary>Creates or returns an existing placeholder visual under <paramref name="parent"/>.</summary>
    public static Transform EnsurePlaceholderVisual(Transform parent, string rootName = "__PaintballGunVisual")
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
            PrimitiveType.Cube,
            "Receiver",
            rootTransform,
            new Vector3(0f, -0.02f, 0.04f),
            Quaternion.identity,
            new Vector3(0.07f, 0.11f, 0.18f),
            new Color(0.18f, 0.18f, 0.2f, 1f));

        CreatePart(
            PrimitiveType.Cylinder,
            "Barrel",
            rootTransform,
            new Vector3(0f, 0.01f, 0.24f),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector3(0.035f, 0.16f, 0.035f),
            new Color(0.12f, 0.12f, 0.14f, 1f));

        CreatePart(
            PrimitiveType.Cylinder,
            "Hopper",
            rootTransform,
            new Vector3(0f, 0.08f, 0.02f),
            Quaternion.identity,
            new Vector3(0.06f, 0.045f, 0.06f),
            new Color(0.75f, 0.12f, 0.12f, 1f));

        CreatePart(
            PrimitiveType.Cube,
            "Grip",
            rootTransform,
            new Vector3(0f, -0.09f, 0.02f),
            Quaternion.identity,
            new Vector3(0.05f, 0.12f, 0.07f),
            new Color(0.22f, 0.16f, 0.12f, 1f));

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
