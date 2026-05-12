using UnityEngine;

public static class FlashlightVisualFactory
{
    public static Transform EnsurePlaceholderVisual(Transform parent, string rootName = "__FlashlightVisual")
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
            "Handle",
            rootTransform,
            new Vector3(0f, 0f, 0.02f),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector3(0.028f, 0.11f, 0.028f));

        CreatePart(
            PrimitiveType.Cylinder,
            "Head",
            rootTransform,
            new Vector3(0f, 0f, 0.16f),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector3(0.042f, 0.04f, 0.042f));

        CreatePart(
            PrimitiveType.Cube,
            "Cap",
            rootTransform,
            new Vector3(0f, 0f, -0.1f),
            Quaternion.identity,
            new Vector3(0.042f, 0.042f, 0.03f));

        return rootTransform;
    }

    private static void CreatePart(
        PrimitiveType primitiveType,
        string name,
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = name;
        Transform partTransform = part.transform;
        partTransform.SetParent(parent, false);
        partTransform.localPosition = localPosition;
        partTransform.localRotation = localRotation;
        partTransform.localScale = localScale;

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
