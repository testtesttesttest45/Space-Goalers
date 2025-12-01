using UnityEngine;
using UnityEngine.UI;

public class AbilityJoystick : MonoBehaviour
{
    [Header("References")]
    public RectTransform background;
    public RectTransform handle;

    [Header("Settings")]
    public float radius = 80f;

    [HideInInspector] public Vector2 InputVector;
    private Vector2 _lastNonZeroInput;
    private Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();

        if (background)
        {
            background.gameObject.SetActive(false);
            var bgImg = background.GetComponent<Image>();
            if (bgImg) bgImg.raycastTarget = true;
        }


        if (handle)
        {
            handle.gameObject.SetActive(false);
            var hImg = handle.GetComponent<Image>();
            if (hImg) hImg.raycastTarget = false;
        }
    }

    public void SetVisible(bool visible)
    {
        if (background) background.gameObject.SetActive(visible);
        if (handle) handle.gameObject.SetActive(visible);

        if (!visible)
        {
            if (handle) handle.anchoredPosition = Vector2.zero;
            InputVector = Vector2.zero;
            _lastNonZeroInput = Vector2.zero;
        }
    }

    public void SetPosition(Vector2 _ ) // _ means unused
    {
        if (handle) handle.anchoredPosition = Vector2.zero;
        InputVector = Vector2.zero;
        _lastNonZeroInput = Vector2.zero;
    }

    public void UpdateHandle(Vector2 screenPosition)
    {
        if (!background || !handle) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background,
            screenPosition,
            _canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null,
            out Vector2 localPoint
        );

        Vector2 offset = localPoint;
        float magnitude = offset.magnitude;
        if (magnitude > radius)
            offset = offset.normalized * radius;

        handle.anchoredPosition = offset;

        Vector2 current = offset / radius;
        if (magnitude > radius)
            current = current.normalized;

        InputVector = current;

        if (current.sqrMagnitude > 0.001f)
            _lastNonZeroInput = current;
    }

    public Vector2 GetSafeInput()
    {
        return InputVector.sqrMagnitude > 0.001f ? InputVector : _lastNonZeroInput;
    }
}
