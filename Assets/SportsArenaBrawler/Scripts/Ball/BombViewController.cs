using Quantum;
using UnityEngine;

[RequireComponent(typeof(QuantumEntityView))]
public class BombViewController : QuantumCallbacks
{
    [Header("Optional SFX fallback on destroy")]
    [SerializeField] private AudioClip explosionClip;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float volume = 1f;

    QuantumEntityView _view;
    bombTimer _timer;

    Vector3 _lastSimPos;
    bool _prevFuseArmed = false;

    void Awake()
    {
        _view = GetComponent<QuantumEntityView>();
        _timer = GetComponentInChildren<bombTimer>(true);
    }

    public override unsafe void OnUpdateView(QuantumGame game)
    {
        var frame = game.Frames?.Predicted;
        if (frame == null) return;

        var ent = _view.EntityRef;
        if (!frame.Exists(ent)) return;

        if (frame.Has<Transform3D>(ent))
        {
            var tr = frame.Unsafe.GetPointer<Transform3D>(ent);
            _lastSimPos = tr->Position.ToUnityVector3();
        }

        _timer?.BeginFuseVisual();

        if (frame.Has<BombState>(ent))
        {
            var st = frame.Unsafe.GetPointer<BombState>(ent);
            bool armedNow = st->GroundFuseArmed;

            if (armedNow && !_prevFuseArmed)
            {
                _timer?.ArmFuse(st->GroundFuseLeft.AsFloat);
            }

            _prevFuseArmed = armedNow;
        }
    }

    public void OnEntityDestroyed(QuantumGame game)
    {
        if (_timer) _timer.ExplodeNow(_lastSimPos);

        if (audioSource && explosionClip)
        {
            var srcGO = audioSource.gameObject;
            srcGO.transform.SetParent(null, true);
            audioSource.PlayOneShot(explosionClip, volume);
            Destroy(srcGO, explosionClip.length + 0.1f);
        }
    }
}
