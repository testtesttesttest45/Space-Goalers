using Photon.Deterministic;
using Quantum;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Cinemachine.CinemachineOrbitalTransposer;
using UIButton = UnityEngine.UI.Button;

public class QuantumDemoInputTopDownMobile : MonoBehaviour
{
    [Header("Refs")]
    public MovementJoystick movementJoystick;
    public UIButton punchBtn;
    public UIButton longBtn;
    public UIButton jumpBtn;

    public UIButton dashBtn;
    public UIButton hookBtn;
    public UIButton invisBtn;
    public UIButton speedBtn;

    public PlayerViewController localPlayer;

    [Header("Settings")]
    public float deadZone = 0.15f;

     bool _fire, _alt, _jump;
     bool _dash, _hook, _use, _speed, _select, _bomb;

    bool _usingDynamicUtility;

    void Awake()
    {
        _usingDynamicUtility = FindObjectOfType<UIAbilityController>(true) != null;
    }

    void OnEnable()
    {
        if (punchBtn) punchBtn.onClick.AddListener(() => _fire = true);
        if (longBtn) longBtn.onClick.AddListener(() => _alt = true);
        if (jumpBtn) jumpBtn.onClick.AddListener(() => _jump = true);

        if (_usingDynamicUtility)
        {
            DisableLegacyUtilityButtons();
        }
        else
        {
            if (dashBtn) dashBtn.onClick.AddListener(() => _dash = true);
            if (hookBtn) hookBtn.onClick.AddListener(() => _hook = true);
            if (invisBtn) invisBtn.onClick.AddListener(() => _use = true);
            if (speedBtn) speedBtn.onClick.AddListener(() => _speed = true);
        }

        QuantumCallback.Subscribe(this, (CallbackPollInput cb) => PollInput(cb));
    }

    public void DisableLegacyUtilityButtonsPublic() => DisableLegacyUtilityButtons();

    void DisableLegacyUtilityButtons()
    {
        void Nix(UIButton b)
        {
            if (!b) return;
            b.onClick.RemoveAllListeners();
            b.interactable = false;
            var img = b.GetComponent<Image>();
            if (img) img.raycastTarget = false;
            b.gameObject.SetActive(false);
        }
        Nix(dashBtn); Nix(hookBtn); Nix(invisBtn); Nix(speedBtn);
    }

    void OnDisable()
    {
        if (punchBtn) punchBtn.onClick.RemoveAllListeners();
        if (longBtn) longBtn.onClick.RemoveAllListeners();
        if (jumpBtn) jumpBtn.onClick.RemoveAllListeners();
        if (dashBtn) dashBtn.onClick.RemoveAllListeners();
        if (hookBtn) hookBtn.onClick.RemoveAllListeners();
        if (invisBtn) invisBtn.onClick.RemoveAllListeners();
        if (speedBtn) speedBtn.onClick.RemoveAllListeners();

        QuantumCallback.UnsubscribeListener(this);
    }

    public void PressFire()
    {
        _alt = _jump = _dash = _hook = _use = _speed = _select = _bomb = false;
        _fire = true;
    }
    public void PressAltFire()
    {
        _fire = _jump = _dash = _hook = _use = _speed = _select = _bomb = false;
        _alt = true;
    }

    public void PressPunch() { _fire = true; }
    public void PressLong() { _alt = true; }
    public void PressJump() { _jump = true; }
    public void PressDash() { _dash = true; }
    public void PressHook() { _hook = true; }
    public void PressInvisibility() { _use = true; }
    public void PressSpeedster() { _speed = true; }
    public void PressBanana() { _select = true; }
    public void PressBomb() { _bomb = true; }

    private AbilityType _wiredUtility = AbilityType.Invisibility;
    public void SetWiredUtility(AbilityType t) => _wiredUtility = t;

    public void PressUtilityExclusive(AbilityType t)
    {
        ClearUtilityLatches();
        _use = true;
    }

    void ClearUtilityLatches()
    {
        _dash = _hook = _use = _speed = _select = _bomb = false;
    }

    public void DisableLegacyActionButtonsPublic() => DisableLegacyActionButtons();

    void DisableLegacyActionButtons()
    {
        void Nix(UIButton b)
        {
            if (!b) return;
            b.onClick.RemoveAllListeners();
            b.interactable = false;
            var img = b.GetComponent<Image>();
            if (img) img.raycastTarget = false;
            b.gameObject.SetActive(false);
        }

        Nix(punchBtn);
        Nix(longBtn);
    }


    public void PollInput(CallbackPollInput cb)
    {
        QuantumDemoInputTopDown t = default;

        Vector2 mv = movementJoystick ? movementJoystick.joystickVec : Vector2.zero;
        if (mv.sqrMagnitude < deadZone * deadZone) mv = Vector2.zero;
        t.Left = mv.x < -0.001f; t.Right = mv.x > 0.001f;
        t.Down = mv.y < -0.001f; t.Up = mv.y > 0.001f;
        t.MoveDirection = new FPVector2(mv.x.ToFP(), mv.y.ToFP());

        bool fire = Consume(ref _fire);
        bool alt = Consume(ref _alt);
        bool jump = Consume(ref _jump);
        bool dash = Consume(ref _dash);
        bool hook = Consume(ref _hook);
        bool use = Consume(ref _use);
        bool speed = Consume(ref _speed);
        bool select = Consume(ref _select);
        bool bomb = Consume(ref _bomb);

        t.Fire = fire; t.AltFire = alt; t.Jump = jump;
        t.Dash = dash; t.Hook = hook; t.Use = use;
        t.Speed = speed; t.Select = select; t.Bomb = bomb;

        Vector2 aimVec = Vector2.zero;

        if (localPlayer)
        {
            if (select)
            {
                var drop01 = localPlayer.ConsumePendingDropOffset();
                aimVec = drop01 ?? Vector2.zero;

                localPlayer.MarkPendingDropUsed();
            }
            else if (fire || alt || hook || use || speed || bomb)
            {
                if (use && _wiredUtility == AbilityType.Banana)
                {
                    var drop01 = localPlayer.ConsumePendingDropOffset();
                    aimVec = drop01 ?? Vector2.zero;
                    localPlayer.MarkPendingDropUsed();
                }
                else
                {
                    var committed = localPlayer.ConsumePendingAimDir();
                    aimVec = committed.HasValue ? committed.Value : localPlayer.GetLastIndicatorDirection();
                }
            }
            else
            {
                aimVec = Vector2.zero;
            }
        }

        if (aimVec.sqrMagnitude > 1f)
            aimVec.Normalize();

        t.AimDirection = new FPVector2(aimVec.x.ToFP(), aimVec.y.ToFP());

        cb.SetInput(t, DeterministicInputFlags.Repeatable);
    }

    void Start()
    {
        var pia = FindObjectOfType<PlayerInput>(true);
        if (pia != null)
        {
            var map = pia.actions.FindActionMap("Gameplay", throwIfNotFound: false);
            if (map != null && map.enabled)
            {
                map.Disable();
            }
        }
    }

    static bool Consume(ref bool latch)
    {
        if (!latch) return false;
        latch = false;
        return true;
    }
}

