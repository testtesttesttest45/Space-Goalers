using Quantum;
using UnityEngine;

public class PlayerGizmos : MonoBehaviour
{
    [SerializeField] private AssetRef<BallHandlingData> _ballHandlingDataAssetRef;
    [SerializeField] private AssetRef<AbilityData> _attackAbilityAssetRef;
    [SerializeField] private AssetRef<AbilityData> _shortThrowAbilityAssetRef;
    [SerializeField] private AssetRef<AbilityData> _longThrowAbilityAssetRef;

    // NEW: Hook ability asset (assign your HookshotAbilityData here)
    [SerializeField] private AssetRef<AbilityData> _hookAbilityAssetRef;

    private void OnDrawGizmos()
    {
        DrawBallDropPosition();
        DrawAttackShape();
        DrawThrowPositions();
        DrawHookRangeAndSteps();   // NEW
    }

    private void DrawBallDropPosition()
    {
        var data = QuantumUnityDB.GetGlobalAsset<BallHandlingData>(_ballHandlingDataAssetRef.Id);
        if (!data) return;
        Vector3 dropLocalPosition = data.DropLocalPosition.ToUnityVector3();
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position + transform.rotation * dropLocalPosition, 0.1f);
    }

    private void DrawAttackShape()
    {
        var data = QuantumUnityDB.GetGlobalAsset<AttackAbilityData>(_attackAbilityAssetRef.Id);
        if (!data) return;
        Shape3DConfig shapeConfig = data.AttackShape;

        Gizmos.color = Color.cyan;
        switch (shapeConfig.ShapeType)
        {
            case Shape3DType.Sphere:
                DrawSphereShape(shapeConfig.PositionOffset.ToUnityVector3(), shapeConfig.SphereRadius.AsFloat);
                break;
            case Shape3DType.Compound:
                foreach (var c in shapeConfig.CompoundShapes)
                {
                    if (c.ShapeType == Shape3DType.Sphere)
                        DrawSphereShape(c.PositionOffset.ToUnityVector3(), c.SphereRadius.AsFloat);
                }
                break;
        }
    }

    private void DrawThrowPositions()
    {
        var shortData = QuantumUnityDB.GetGlobalAsset<ThrowBallAbilityData>(_shortThrowAbilityAssetRef.Id);
        var longData = QuantumUnityDB.GetGlobalAsset<ThrowBallAbilityData>(_longThrowAbilityAssetRef.Id);
        if (!shortData || !longData) return;

        Vector3 pS = transform.position + transform.rotation * shortData.ThrowLocalPosition.ToUnityVector3();
        Vector3 pL = transform.position + transform.rotation * longData.ThrowLocalPosition.ToUnityVector3();

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(pS, 0.1f);
        Gizmos.DrawSphere(pL, 0.1f);
    }

    private void DrawSphereShape(Vector3 positionOffset, float radius)
    {
        Vector3 shapePosition = transform.position + transform.rotation * positionOffset;
        Gizmos.DrawWireSphere(shapePosition, radius);
    }

    // === HOOK VISUALS ===
    private void DrawHookRangeAndSteps()
    {
        // Get the Hook ability data
        var ability = QuantumUnityDB.GetGlobalAsset<AbilityData>(_hookAbilityAssetRef.Id) as HookshotAbilityData;
        if (ability == null) return;

        // Forward on XZ (matches your gameplay)
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) return;
        fwd.Normalize();

        // Same offsets you use in code
        float range = ability.Range.AsFloat;
        float startForward = ability.StartForward.AsFloat;
        float startUp = ability.StartUp.AsFloat;

        Vector3 origin = transform.position + fwd * startForward + Vector3.up * startUp;

        // 1) Range line
        Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
        Gizmos.DrawLine(origin, origin + fwd * range);

        // 2) Optional: draw the stepped hit boxes (what the step-cast samples)
        //    Keep this ON while tuning, OFF later if too noisy.
        var shape = ability.AttackShape;
        if (shape.ShapeType == Shape3DType.Box)
        {
            // box size in world units (extents * 2)
            Vector3 size = (shape.BoxExtents.ToUnityVector3() * 2f);
            Vector3 centerLocal = shape.PositionOffset.ToUnityVector3(); // usually (0,0,0)

            float step = Mathf.Max(0.01f, ability.Step.AsFloat);
            for (float along = 0f; along <= range + 1e-3f; along += step)
            {
                Vector3 center = origin + fwd * along + transform.rotation * centerLocal;
                // draw as a small wire cube aligned to forward
                Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.LookRotation(fwd, Vector3.up), size);
                Gizmos.matrix = m;
                Gizmos.color = new Color(0f, 0.8f, 1f, 0.5f);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
