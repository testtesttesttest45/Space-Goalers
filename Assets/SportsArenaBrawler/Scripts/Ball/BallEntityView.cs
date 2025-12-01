using Quantum;
using UnityEngine;

public unsafe class BallEntityView : QuantumEntityView
{
    [SerializeField] private float _spaceTransitionSpeed = 4f;

    private EntityRef _holdingPlayerEntityRef;
    private float _interpolationSpaceAlpha;

    private Vector3 _lastBallRealPosition;
    private Quaternion _lastBallRealRotation;

    private Vector3 _lastBallAnimationPosition;
    private Quaternion _lastBallAnimationRotation;

    protected override void ApplyTransform(ref UpdatePositionParameter param)
    {
        base.ApplyTransform(ref param);

        Frame frame = QuantumRunner.Default.Game.Frames.Predicted;
        BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(EntityRef);

        _holdingPlayerEntityRef = ballStatus->HoldingPlayerEntityRef;
    }

    public void UpdateSpaceInterpolation()
    {
        bool isBallHeldByPlayer = _holdingPlayerEntityRef != default;
        UpdateInterpolationSpaceAlpha(isBallHeldByPlayer);

        if (isBallHeldByPlayer)
        {
            PlayerViewController player = PlayersManager.Instance.GetPlayer(_holdingPlayerEntityRef);

            _lastBallAnimationPosition = player.BallFollowTransform.position;
            _lastBallAnimationRotation = player.BallFollowTransform.rotation;
        }
        else
        {
            _lastBallRealPosition = transform.position;
            _lastBallRealRotation = transform.rotation;
        }

        if (_interpolationSpaceAlpha > 0f)
        {
            Vector3 interpolatedPosition = Vector3.Lerp(_lastBallRealPosition, _lastBallAnimationPosition, _interpolationSpaceAlpha);
            Quaternion interpolatedRotation = Quaternion.Slerp(_lastBallRealRotation, _lastBallAnimationRotation, _interpolationSpaceAlpha);

            transform.SetPositionAndRotation(interpolatedPosition, interpolatedRotation);
        }
    }

    private void UpdateInterpolationSpaceAlpha(bool isBallHeldByPlayer)
    {
        float deltaChange = _spaceTransitionSpeed * Time.deltaTime;
        if (isBallHeldByPlayer)
        {
            _interpolationSpaceAlpha += deltaChange;
        }
        else
        {
            _interpolationSpaceAlpha -= deltaChange;
        }

        _interpolationSpaceAlpha = Mathf.Clamp(_interpolationSpaceAlpha, 0f, 1f);
    }
}
