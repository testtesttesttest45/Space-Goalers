using UnityEngine;
using UnityEngine.EventSystems;

public class MovementJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Assign these")]
    public RectTransform joystick;   // inner handle
    public RectTransform joystickBG; // outer ring

    [Header("Settings")]
    [Tooltip("The maximum distance the joystick handle can move (in pixels).")]
    public float joystickRadius = 100f;
    [Tooltip("Limit joystick to left half of the screen.")]
    public bool restrictToLeftSide = true;

    [HideInInspector] public Vector2 joystickVec;

    private Vector2 _joystickOrigin;
    private Canvas _canvas;
    private int _activePointerId = -1;
    private bool _isDragging;

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();

        // Start hidden
        if (joystickBG) joystickBG.gameObject.SetActive(true);
        if (joystick) joystick.gameObject.SetActive(true);
    }

    public void OnPointerDown(PointerEventData e)
    {
        // only allow on left side (optional)
        if (restrictToLeftSide && e.position.x > Screen.width / 2f)
            return;

        if (_isDragging)
            return;

        _isDragging = true;
        _activePointerId = e.pointerId;

        if (joystickBG) joystickBG.gameObject.SetActive(true);
        if (joystick) joystick.gameObject.SetActive(true);

        RectTransform parentRect = joystickBG.parent as RectTransform;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect,
            e.position,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
            out _joystickOrigin
        );

        joystickBG.anchoredPosition = _joystickOrigin;
        joystick.anchoredPosition = Vector2.zero;
        joystickVec = Vector2.zero;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_isDragging || e.pointerId != _activePointerId)
            return;

        RectTransform parentRect = joystickBG.parent as RectTransform;

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect,
            e.position,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
            out localPoint
        );

        Vector2 offset = localPoint - _joystickOrigin;
        float distance = Mathf.Min(offset.magnitude, joystickRadius);

        Vector2 direction = offset.normalized;
        joystickVec = direction * (distance / joystickRadius);

        if (joystick)
            joystick.anchoredPosition = direction * distance;
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != _activePointerId)
            return;

        joystickVec = Vector2.zero;

        if (joystickBG) joystickBG.gameObject.SetActive(false);
        if (joystick) joystick.gameObject.SetActive(false);

        _isDragging = false;
        _activePointerId = -1;
    }

    public void ForceRelease()
    {
        joystickVec = Vector2.zero;
        _isDragging = false;
        _activePointerId = -1;

        if (joystickBG) joystickBG.gameObject.SetActive(false);
        if (joystick) joystick.gameObject.SetActive(false);
    }
}
