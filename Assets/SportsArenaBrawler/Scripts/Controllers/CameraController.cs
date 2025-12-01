using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CameraController : MonoBehaviour
{
    private const float PLAYER_FRAMING_RADIUS = 1f;
    private const float BALL_FRAMING_RADIUS = 3f;
    private const float LOCAL_PLAYER_FRAMING_WEIGHT = 15f;
    private const float REMOTE_PLAYER_FRAMING_WEIGHT = 5f;
    private const float BALL_FRAMING_WEIGHT = 2.5f;

    [SerializeField] private Camera _camera;
    [SerializeField] private Camera _uiCamera;
    [SerializeField] private UniversalAdditionalCameraData _additionalCameraData;
    [SerializeField] private CinemachineVirtualCamera _virtualCamera;
    [SerializeField] private CinemachineTargetGroup _cameraTargetGroup;

    public Camera Camera => _camera;
    public CinemachineVirtualCamera VirtualCamera => _virtualCamera;

    private void LateUpdate()
    {
        _uiCamera.fieldOfView = _camera.fieldOfView;
    }

    public void Initialize(LocalPlayerAccess localPlayerAccess)
    {
        _additionalCameraData.volumeTrigger = localPlayerAccess.LocalPlayer.transform;
    }

    public void AddPlayerTransform(Transform playerTransform, bool isLocal)
    {
        if (isLocal)
        {
            _cameraTargetGroup.AddMember(playerTransform, LOCAL_PLAYER_FRAMING_WEIGHT, PLAYER_FRAMING_RADIUS);
        }
        else
        {
            _cameraTargetGroup.AddMember(playerTransform, REMOTE_PLAYER_FRAMING_WEIGHT, PLAYER_FRAMING_RADIUS);
        }
    }

    public void AddBallTransform(Transform ballTransform)
    {
        _cameraTargetGroup.AddMember(ballTransform, BALL_FRAMING_WEIGHT, BALL_FRAMING_RADIUS);
    }

    public void RemoveTransform(Transform transform)
    {
        _cameraTargetGroup.RemoveMember(transform);
    }
}
