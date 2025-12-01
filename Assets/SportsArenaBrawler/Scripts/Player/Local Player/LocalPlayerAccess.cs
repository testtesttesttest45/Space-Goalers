using UnityEngine;
using UnityEngine.InputSystem;

public class LocalPlayerAccess : MonoBehaviour
{
    [SerializeField] private UIGameplay _uiGameplay;
    [SerializeField] private UIAbilityController _abilityController;
    [SerializeField] private PlayerInput _playerInput;
    [SerializeField] private CameraController _cameraController;

    public UIGameplay UIGameplay => _uiGameplay;
    public UIAbilityController UIAbilityController => _abilityController;
    public PlayerInput PlayerInput => _playerInput;
    public CameraController CameraController => _cameraController;

    public bool IsMainLocalPlayer { get; set; }
    public PlayerViewController LocalPlayer { get; private set; }

    public void InitializeLocalPlayer(PlayerViewController localPlayer)
    {
        LocalPlayer = localPlayer;

        _uiGameplay.Initialize(this);
        _abilityController.Initialize(this);
        _cameraController.Initialize(this);

        // NEW: hand the local player to the mobile input poller
        var mobile = FindObjectOfType<QuantumDemoInputTopDownMobile>(true);
        if (mobile) mobile.localPlayer = localPlayer;
    }

}
