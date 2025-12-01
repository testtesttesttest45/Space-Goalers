using System;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class TrajectoryIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LineRenderer lineRenderer;

    [Header("Settings")]
    public float minDistance = 5f;
    public float maxDistance = 26f;
    public float minArcHeight = 5.0f;
    public float maxArcHeight = 10.0f;
    public float startHeight = 0.25f;
    public int segments = 12;
    public float baseWidth = 0.3f;
    public float widthBoost = 2f;

    [Header("Tip Circle")]
    [SerializeField] private Sprite tipCircleSprite;
    [SerializeField] private float tipYLift = 0.02f;
    [SerializeField] private float minTipRadius = 0.45f;
    [SerializeField] private float maxTipRadius = 0.85f;
    [SerializeField] private bool lockTipRadius = false;
    [SerializeField] private LayerMask groundMask = ~0;
    private Transform _tipCircle;
    private Material _tipMatInstance;

    private Transform _playerCenter;
    private Vector2 _rawInput;
    private bool _isVisible;

    private Vector3 _basisForward, _basisRight;
    private bool _hasBasis;
    private Vector3[] _points;

    public void Initialize(Transform playerCenter)
    {
        _playerCenter = playerCenter;

        if (!lineRenderer)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.useWorldSpace = true;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.colorGradient = MakeDefaultGradient();
        }

        lineRenderer.startWidth = baseWidth;
        lineRenderer.endWidth = baseWidth * 0.8f;

        _points = new Vector3[Mathf.Max(segments, 3)];
        lineRenderer.positionCount = _points.Length;
        lineRenderer.enabled = false;

        if (tipCircleSprite)
        {
            var go = new GameObject("TrajectoryTipCircle");
            go.transform.SetParent(null);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tipCircleSprite;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.sortingOrder = 32760;
            _tipMatInstance = new Material(Shader.Find("Sprites/Default"));
            sr.material = _tipMatInstance;
            _tipCircle = go.transform;
            _tipCircle.gameObject.SetActive(false);
        }
    }

    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (lineRenderer) lineRenderer.enabled = visible;
        if (_tipCircle) _tipCircle.gameObject.SetActive(visible);
    }

    public void BeginAimFromCamera(Camera cam)
    {
        if (!cam) cam = Camera.main;
        if (!cam)
        {
            Debug.LogWarning("[TrajectoryIndicator] No camera found!");
            _hasBasis = false;
            return;
        }

        _basisForward = cam.transform.forward; _basisForward.y = 0f; _basisForward.Normalize();
        _basisRight = cam.transform.right; _basisRight.y = 0f; _basisRight.Normalize();
        _hasBasis = true;
    }

    public void EndAim()
    {
        _hasBasis = false;
        SetVisible(false);
    }

    private Vector2 _lastDir01 = new Vector2(0, 1);
    [SerializeField] private float _minVisualStrength = 0.06f;

    public void UpdateDirection(Vector2 input)
    {
        if (_playerCenter == null || !_hasBasis) { SetVisible(false); return; }
        if (input.sqrMagnitude > 0.000001f) _lastDir01 = input.normalized;
        _rawInput = input;
        SetVisible(true);
    }

    private void LateUpdate()
    {
        if (!_isVisible || !_hasBasis || _playerCenter == null) return;

        float strength = Mathf.Clamp(_rawInput.magnitude, _minVisualStrength, 1f);
        Vector3 worldDir = (_basisForward * _lastDir01.y + _basisRight * _lastDir01.x).normalized;
        Vector3 start = _playerCenter.position + Vector3.up * startHeight;

        float dist = Mathf.Lerp(minDistance, maxDistance, strength);
        float arcHeight = Mathf.Lerp(maxArcHeight, minArcHeight, strength);
        float width = Mathf.Lerp(baseWidth, baseWidth * widthBoost, strength);

        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width * 0.7f;

        for (int i = 0; i < _points.Length; i++)
        {
            float t = i / (float)(_points.Length - 1);
            float y = arcHeight * Mathf.Sin(Mathf.PI * t);
            Vector3 p = start + worldDir * (dist * t);
            p.y += y;
            _points[i] = p;
        }
        lineRenderer.SetPositions(_points);

        if (_tipCircle)
        {
            Vector3 tip = _points[_points.Length - 1];

            Vector3 hitPos = tip;
            if (Physics.Raycast(tip + Vector3.up * 2f, Vector3.down, out var hit, 6f, groundMask))
                hitPos = hit.point;

            hitPos.y += tipYLift;

            _tipCircle.position = hitPos;

            _tipCircle.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            float r = lockTipRadius ? maxTipRadius : Mathf.Lerp(minTipRadius, maxTipRadius, strength);
            _tipCircle.localScale = new Vector3(r * 2f, r * 2f, 1f); // sprite uses diameter in X/Y
        }
    }

    private Gradient MakeDefaultGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.yellow, 0f),
                new GradientColorKey(new Color(1f, 0.6f, 0f), 0.7f),
                new GradientColorKey(Color.clear, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    public Vector3[] GetCurrentPoints() => _points;
}
