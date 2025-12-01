using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DecalIndicator : MonoBehaviour
{
    private const float MESH_INDICATOR_HEIGHT   = -2f;
    private const float SCALING_REFERENCE_HEIGHT = 0.5f;
    private const float SCALING_MAX_DISTANCE     = 9f;
    private const float MAX_DISTANCE_SCALE       = 0.15f;

    [Header("Behaviour")]
    [SerializeField] private bool _manualUpdate = false;
    [SerializeField] private bool _scaleBasedOnHeight = false;

    [Header("Refs (optional)")]
    [SerializeField] private DecalProjector _decalProjector;
    [SerializeField] private MeshRenderer   _meshIndicator;

    [Header("Coloring")]
    [SerializeField] private bool _forceNoEmission = false;

    private readonly List<DecalProjector> _allProjectors = new();
    private readonly List<Renderer>       _allRenderers  = new();

    private static readonly int[] kColorProps = {
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_BaseColor0"),
        Shader.PropertyToID("_BaseTint"),
        Shader.PropertyToID("_Tint"),
        Shader.PropertyToID("_EmissionColor"),
        Shader.PropertyToID("_EmissiveColor")
    };

    public void SetManualUpdate(bool v) => _manualUpdate = v;

    void Awake()
    {
        GetComponentsInChildren(true, _allProjectors);
        GetComponentsInChildren(true, _allRenderers);

        if (_decalProjector && !_allProjectors.Contains(_decalProjector))
            _allProjectors.Add(_decalProjector);
        if (_meshIndicator && !_allRenderers.Contains(_meshIndicator))
            _allRenderers.Add(_meshIndicator);
    }

    void LateUpdate()
    {
        if (_manualUpdate) return;
        UpdateDecal(Quaternion.identity);
    }

    public void ChangeLayerAndMaterial(int layer)
    {
        SetLayerRecursively(gameObject, layer);

        for (int i = 0; i < _allProjectors.Count; i++)
        {
            var p = _allProjectors[i];
            if (!p) continue;
            var mat = p.material;
            if (mat) p.material = new Material(mat);
        }

        for (int i = 0; i < _allRenderers.Count; i++)
        {
            var r = _allRenderers[i];
            if (!r) continue;

            var mats = r.sharedMaterials;
            if (mats == null) continue;

            for (int m = 0; m < mats.Length; m++)
                if (mats[m]) mats[m] = new Material(mats[m]);

            r.sharedMaterials = mats;
        }
    }

    public void SetColor(Color c, bool logApplied = false)
    {
        Color tint = c;

        for (int i = 0; i < _allProjectors.Count; i++)
        {
            var p = _allProjectors[i];
            if (p && p.material) TrySetColorOnMaterial(p.material, tint, logApplied, $"[Projector:{p.name}]");
        }

        for (int i = 0; i < _allRenderers.Count; i++)
        {
            var r = _allRenderers[i];
            if (!r) continue;
            var mats = r.sharedMaterials;
            if (mats == null) continue;

            for (int m = 0; m < mats.Length; m++)
                if (mats[m]) TrySetColorOnMaterial(mats[m], tint, logApplied, $"[Renderer:{r.name} mat{m}]");
        }
    }

    public void SetReceiverMask(uint renderingLayerMask)
    {
        for (int i = 0; i < _allProjectors.Count; i++)
        {
            var p = _allProjectors[i];
            if (p) p.renderingLayerMask = renderingLayerMask;
        }
    }

    public void UpdateDecal(Quaternion rotation)
    {
        transform.rotation = rotation;

        if (_meshIndicator)
        {
            var pos = _meshIndicator.transform.position;
            pos.y = MESH_INDICATOR_HEIGHT;
            _meshIndicator.transform.position = pos;
        }

        if (_scaleBasedOnHeight)
        {
            float height = transform.position.y;
            float distance = Mathf.Abs(height - SCALING_REFERENCE_HEIGHT);
            float normalizedDistance = distance / SCALING_MAX_DISTANCE;
            float scale = Mathf.Lerp(1f, MAX_DISTANCE_SCALE, normalizedDistance);
            transform.localScale = new Vector3(scale, 1f, scale);
        }
    }

    private void TrySetColorOnMaterial(Material mat, Color tint, bool log, string tag)
    {
        bool wroteAny = false;

        for (int i = 0; i < kColorProps.Length; i++)
        {
            int id = kColorProps[i];
            if (!mat.HasProperty(id)) continue;

            if (_forceNoEmission && (id == Shader.PropertyToID("_EmissionColor") || id == Shader.PropertyToID("_EmissiveColor")))
            {
                mat.SetColor(id, Color.black);
                wroteAny = true;
                if (log) Debug.Log($"{tag} set {ShaderIDToName(id)} = (black)");
                continue;
            }

            mat.SetColor(id, tint);
            wroteAny = true;
            if (log) Debug.Log($"{tag} set {ShaderIDToName(id)} = {tint}");
        }

        if (!wroteAny)
        {
            mat.color = tint; // last resort
            if (log) Debug.Log($"{tag} used material.color = {tint}");
        }
    }

    private static string ShaderIDToName(int id)
    {
        // Best-effort pretty print for logs
        if (id == Shader.PropertyToID("_BaseColor"))   return "_BaseColor";
        if (id == Shader.PropertyToID("_Color"))       return "_Color";
        if (id == Shader.PropertyToID("_TintColor"))   return "_TintColor";
        if (id == Shader.PropertyToID("_BaseColor0"))  return "_BaseColor0";
        if (id == Shader.PropertyToID("_BaseTint"))    return "_BaseTint";
        if (id == Shader.PropertyToID("_Tint"))        return "_Tint";
        if (id == Shader.PropertyToID("_EmissionColor"))  return "_EmissionColor";
        if (id == Shader.PropertyToID("_EmissiveColor"))  return "_EmissiveColor";
        return $"prop({id})";
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (!go) return;
        go.layer = layer;
        var t = go.transform;
        for (int i = 0, c = t.childCount; i < c; i++)
        {
            var child = t.GetChild(i);
            if (child) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
