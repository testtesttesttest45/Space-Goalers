using UnityEngine;

[DefaultExecutionOrder(1000)]
public class AbilityIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer arrowSprite;

    [Header("Settings")]
    public float distance = 1.5f;

    private Transform _playerCenter;
    private Vector2 _lastInput;
    private bool _isVisible;

    private Vector3 _basisForward, _basisRight;
    private bool _hasBasis;

    public void Initialize(Transform playerCenter)
    {
        _playerCenter = playerCenter;
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (arrowSprite) arrowSprite.enabled = visible;
        gameObject.SetActive(visible);
    }

    public void BeginAimFromCamera(Camera cam)
    {
        if (!cam) { _hasBasis = false; return; }
        _basisForward = cam.transform.forward; _basisForward.y = 0f; _basisForward.Normalize();
        _basisRight = cam.transform.right; _basisRight.y = 0f; _basisRight.Normalize();
        _hasBasis = true;
    }

    public void EndAim()
    {
        _hasBasis = false;
        SetVisible(false);
    }

    public void UpdateDirection(Vector2 input)
    {
        if (_playerCenter == null) return;
        if (input.sqrMagnitude < 0.01f || !_hasBasis) { SetVisible(false); return; }

        SetVisible(true);
        _lastInput = input.normalized;
    }

    private void LateUpdate()
    {
        if (!_isVisible || _playerCenter == null || !_hasBasis || _lastInput.sqrMagnitude < 0.01f)
            return;

        Vector3 worldDir = (_basisForward * _lastInput.y + _basisRight * _lastInput.x).normalized;

        transform.position = _playerCenter.position + worldDir * distance;

        transform.rotation = Quaternion.LookRotation(worldDir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
    }
}
