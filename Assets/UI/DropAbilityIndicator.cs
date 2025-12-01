using UnityEngine;

[DefaultExecutionOrder(1000)]
public class DropAbilityIndicator : MonoBehaviour
{
    [Header("References (children)")]
    [SerializeField] private SpriteRenderer rangeSprite;
    [SerializeField] private SpriteRenderer dropAreaSprite;

    private Transform _playerCenter;
    private float _radiusM = 3f;

    private bool _visible;
    private bool _hasBasis;
    private Vector3 _basisF, _basisR;

    private Vector2 _lastOffset01;

    public void Initialize(Transform playerCenter, float radiusMeters)
    {
        _playerCenter = playerCenter;
        _radiusM = Mathf.Max(0f, radiusMeters);
        SetVisible(false);
    }

    public void BeginAimFromCamera(Camera cam)
    {
        if (!cam) { _hasBasis = false; SetVisible(false); return; }
        _basisF = cam.transform.forward; _basisF.y = 0f; _basisF.Normalize();
        _basisR = cam.transform.right; _basisR.y = 0f; _basisR.Normalize();
        _hasBasis = true;
        SetVisible(true);
        _lastOffset01 = Vector2.zero;
        ApplyNow();
    }

    public void EndAim()
    {
        _hasBasis = false;
        SetVisible(false);
    }

    public void UpdateOffset(Vector2 offset01)
    {
        if (!_hasBasis || _playerCenter == null) return;

        float m = offset01.magnitude;
        if (m > 1f) offset01 /= m;
        _lastOffset01 = offset01;

        ApplyNow();
    }

    private const float VISUAL_FORWARD_OFFSET = 0.25f;

    private void ApplyNow()
    {
        if (_playerCenter == null) return;
        if (!_visible) return;

        Vector3 p = _playerCenter.position;
        transform.position = new Vector3(p.x, p.y, p.z);

        Vector3 worldOffset = (_basisR * _lastOffset01.x + _basisF * _lastOffset01.y) * _radiusM;

        if (dropAreaSprite)
        {
            Vector3 target = transform.position + worldOffset + (_basisF * 0.3f);

            if (Camera.main)
            {
                Vector3 camF = Camera.main.transform.forward;
                camF.y = 0f;
                target += camF.normalized * VISUAL_FORWARD_OFFSET;
            }

            const float RAISE_Y = 0.05f;
            dropAreaSprite.transform.position = new Vector3(target.x, RAISE_Y, target.z);
        }

        if (rangeSprite)
            rangeSprite.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        if (dropAreaSprite)
            dropAreaSprite.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void LateUpdate()
    {
        if (_visible) ApplyNow();
    }

    private void SetVisible(bool v)
    {
        _visible = v;
        if (rangeSprite) rangeSprite.enabled = v;
        if (dropAreaSprite) dropAreaSprite.enabled = v;
        gameObject.SetActive(v);
    }

    public void BeginAimWithBasis(Vector3 basisF, Vector3 basisR)
    {
        _basisF = basisF;
        _basisR = basisR;
        _hasBasis = true;
        SetVisible(true);
        _lastOffset01 = Vector2.zero;
        ApplyNow();
    }

}
