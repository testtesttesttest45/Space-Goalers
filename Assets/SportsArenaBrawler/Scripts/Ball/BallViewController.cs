using Quantum;
using UnityEngine;

public unsafe class BallViewController : QuantumCallbacks
{
    [Header("Hierarchy")]
    [SerializeField] private BallEntityView _entityView;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private Transform _noRotationParent;
    [SerializeField] private DecalIndicator _decalIndicator;
    [SerializeField] private DecalIndicator _shadowDecalIndicator;

    [Header("Sounds")]
    [SerializeField] private AudioClip _ballBounceSound;

    private bool _updateView;

    public BallEntityView EntityView => _entityView;

    private void Start()
    {
        QuantumEvent.Subscribe<EventOnBallBounced>(this, OnBallBounced);
    }

    public override void OnSimulateFinished(QuantumGame game, Frame frame)
    {
        _updateView = true;
    }

    public override void OnUpdateView(QuantumGame game)
    {
        if (!_updateView)
        {
            return;
        }

        _updateView = false;

        Frame frame = game.Frames.Predicted;
        if (!frame.Exists(_entityView.EntityRef))
        {
            return;
        }

        BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(_entityView.EntityRef);

        ToggleBallIndicators(ballStatus);
    }

    private void ToggleBallIndicators(BallStatus* ballStatus)
    {
        if (ballStatus->IsHeldByPlayer)
        {
            if (_decalIndicator.gameObject.activeSelf)
            {
                _decalIndicator.gameObject.SetActive(false);
                _shadowDecalIndicator.gameObject.SetActive(false);
            }
        }
        else
        {
            if (!_decalIndicator.gameObject.activeSelf)
            {
                _decalIndicator.gameObject.SetActive(true);
                _shadowDecalIndicator.gameObject.SetActive(true);
            }
        }
    }

    private void LateUpdate()
    {
        _entityView.UpdateSpaceInterpolation();

        _noRotationParent.rotation = Quaternion.identity;

        if (_decalIndicator.isActiveAndEnabled)
        {
            _decalIndicator.UpdateDecal(Quaternion.identity);
        }

        if (_shadowDecalIndicator.isActiveAndEnabled)
        {
            _shadowDecalIndicator.UpdateDecal(Quaternion.identity);
        }
    }

    public void OnEntityInstantiated(QuantumGame game)
    {
        PlayersManager.Instance.RegisterBall(this);
    }

    public void OnEntityDestroyed(QuantumGame game)
    {
        PlayersManager.Instance.DeregisterBall(this);
    }

    private void OnBallBounced(EventOnBallBounced eventData)
    {
        if (eventData.BallEntityRef == _entityView.EntityRef)
        {
            _audioSource.PlayOneShot(_ballBounceSound);
        }
    }
}
