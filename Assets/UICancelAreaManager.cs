using UnityEngine;
using UnityEngine.UI;

public class UICancelAreaManager : MonoBehaviour
{
    public static UICancelAreaManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private RectTransform cancelArea;
    [SerializeField] private Image cancelCrossImage;

    [Header("Visuals")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color hoverColor = new Color(1f, 0.3f, 0.3f, 1f);

    [SerializeField] private float uiScale = 1f;
    [SerializeField] private float snapRadiusBase = 80f;

    private Canvas _rootCanvas;
    private bool _isVisible;
    private bool _isHovering;

    public float CancelSnapRadius => snapRadiusBase * uiScale;
    private Vector3 _scaleNormal;
    private Vector3 _scaleHover;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _rootCanvas = GetComponentInParent<Canvas>();

        _scaleNormal = Vector3.one * uiScale;
        _scaleHover = _scaleNormal;
        Hide();
    }

    public void Show()
    {
        if (!cancelArea) return;
        cancelArea.gameObject.SetActive(true);
        cancelArea.localScale = _scaleNormal;
        if (cancelCrossImage) cancelCrossImage.color = normalColor;
        _isVisible = true;
        _isHovering = false;
    }

    public void Hide()
    {
        if (cancelArea) cancelArea.gameObject.SetActive(false);
        _isVisible = false;
        _isHovering = false;
    }

    public bool UpdateHover(Vector2 screenPos)
    {
        if (!_isVisible || !cancelArea) return false;

        bool hit = false;
        Camera cam = _rootCanvas && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? _rootCanvas.worldCamera : null;

        if (RectTransformUtility.RectangleContainsScreenPoint(cancelArea, screenPos, cam))
        {
            hit = true;
        }
        else
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(cancelArea, screenPos, cam, out var local);
            var rect = cancelArea.rect;
            var clamped = new Vector2(
                Mathf.Clamp(local.x, rect.xMin, rect.xMax),
                Mathf.Clamp(local.y, rect.yMin, rect.yMax)
            );
            float snap = CancelSnapRadius;
            hit = (local - clamped).sqrMagnitude <= snap * snap;
        }

        if (hit != _isHovering)
        {
            _isHovering = hit;
            cancelArea.localScale = _isHovering ? _scaleHover : _scaleNormal;
            if (cancelCrossImage)
                cancelCrossImage.color = _isHovering ? hoverColor : normalColor;
        }

        return _isHovering;
    }

    public void SetScale(float s)
    {
        uiScale = s;
        _scaleNormal = Vector3.one * s;
        _scaleHover = Vector3.one * (s * 1.1f);
        if (_isVisible && cancelArea)
            cancelArea.localScale = _isHovering ? _scaleHover : _scaleNormal;
    }


    public bool IsHovering => _isHovering;
}
