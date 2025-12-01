using UnityEngine;

public static class GameObjectUtils
{
    public static void SetLayerWithChildren(this GameObject gameObject, int layer)
    {
        SetLayerWithChildren(gameObject.transform, layer);
    }

    public static void SetLayerWithChildren(this Transform transform, int layer)
    {
        transform.gameObject.layer = layer;
        foreach (Transform childTransform in transform)
        {
            childTransform.SetLayerWithChildren(layer);
        }
    }
}
