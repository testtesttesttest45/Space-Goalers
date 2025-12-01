using System;
using System.Collections;
using Quantum;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class UIAbility : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerUpHandler, ICancelHandler, IInitializePotentialDragHandler
{
    private static readonly int READY_ANIM_HASH = Animator.StringToHash("Ready");
    private static readonly int ACTIVATE_ANIM_HASH = Animator.StringToHash("Activate");
    private static readonly int ACTIVATION_FAILED_ANIM_HASH = Animator.StringToHash("Activation Failed");
    private static readonly int EMPTY_STATE_TAG_HASH = Animator.StringToHash("Empty");

    [SerializeField] private Image _revealingMask;
    [SerializeField] private Image _hidingMask;
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioClip _readySound;
    [SerializeField] private AudioClip _activationFailedSound;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private CanvasGroup _invisibleCanvasGroup;
    [SerializeField] private LayoutElement _layoutElement;
    [SerializeField] private bool _isPreviewMode = false;

    [Header("Joystick")]
    [SerializeField] public AbilityJoystick abilityJoystick;
    [SerializeField] private float holdThreshold = 0.1f;


    // Assigned by controller
    [HideInInspector] public AbilityType AbilityKind;

    private PlayerViewController _playerViewController;
    private QuantumDemoInputTopDownMobile _mobile;
    private Canvas _rootCanvas;

    private bool _isShown = true;
    private bool _isReady = true;

    // Aim/press state
    private bool _isAiming;
    private bool _heldLongEnough;
    private Vector2 _pressPosition;
    private Coroutine _holdRoutine;

    // Press tracking
    private bool _pressAlive;
    private bool _usingMouse;
    private int _activePointerId;

    // Cancel hover
    private bool _overCancel;
    private Vector2 _lastScreenPos;

    // Trigger delegate
    private Action _invokeAbility;
    public enum AbilityAimType { Forward, Dropzone, Trajectory }

    [Header("Aim Type")]
    [SerializeField] private AbilityAimType _aimType = AbilityAimType.Forward;

    [SerializeField] private float _dropMaxRadiusMeters = 5.0f;
    private bool IsCancellable => AbilityKind != AbilityType.Jump;
    public void SetTrigger(Action trigger) => _invokeAbility = trigger;
    private static UIAbility _activeAimAbility;
    private bool _inputLocked = false;



    // ------------------------- POINTER FLOW -------------------------
    public void OnPointerDown(PointerEventData e)
    {
        if (_inputLocked) return;
        if (e.pointerEnter == null || e.pointerEnter.GetComponentInParent<UIAbility>() != this)
            return;

        if (_revealingMask && !RectTransformUtility.RectangleContainsScreenPoint(
                _revealingMask.rectTransform, e.position, e.enterEventCamera))
            return;

        _pressPosition = e.position;
        _lastScreenPos = e.position;
        _heldLongEnough = false;

        // Keep routing to us even when pointer leaves the button
        e.pointerPress = gameObject;
        e.rawPointerPress = gameObject;
        e.pointerPressRaycast = new RaycastResult { gameObject = gameObject };

        _pressAlive = true;
        _activePointerId = e.pointerId; // mouse -1/-2/-3, touch 0..N
        _usingMouse = _activePointerId < 0;
        _overCancel = false;

        if (_holdRoutine != null)
            StopCoroutine(_holdRoutine);
        _holdRoutine = StartCoroutine(HoldCheck(e.position));
    }

    public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
    public void OnBeginDrag(PointerEventData e) { /* ensures OnEndDrag fires */ }

    private IEnumerator HoldCheck(Vector2 pos)
{
    // 🔁 use unscaled time so countdown / paused frames don't block aiming
    yield return new WaitForSecondsRealtime(holdThreshold);
    if (!_pressAlive) yield break;

    _heldLongEnough = true;

    // refresh refs right before starting aim (in case of scene restart)
    EnsurePlayerRefs();

    if (IsCancellable)
    {
        if (_activeAimAbility != null && _activeAimAbility != this)
            _activeAimAbility.ForceEndAim();

        _activeAimAbility = this;

        if (abilityJoystick != null)
        {
            abilityJoystick.SetVisible(true);
            abilityJoystick.SetPosition(pos);
            _isAiming = true;

            if (_aimType == AbilityAimType.Forward)
                _playerViewController?.BeginAbilityIndicatorAim(Camera.main);
            else if (_aimType == AbilityAimType.Dropzone)
                _playerViewController?.BeginDropIndicatorAim(Camera.main, _dropMaxRadiusMeters);
            else if (_aimType == AbilityAimType.Trajectory)
                _playerViewController?.BeginTrajectoryIndicatorAim(Camera.main);
        }

        ShowCancelArea(true);
        UpdateCancelHover(pos);
    }
}


    public void OnDrag(PointerEventData e)
    {
        if (_inputLocked) return;
        EnsurePlayerRefs();
        _lastScreenPos = e.position;

        // --- If not cancellable, ignore drag logic entirely ---
        if (!IsCancellable)
            return;

        // --- Always update cancel hover ---
        UpdateCancelHover(e.position);

        // --- If there’s no joystick, skip aim updates ---
        if (abilityJoystick == null)
            return;

        // (existing joystick + aim code follows below)
        abilityJoystick.UpdateHandle(e.position);

        if (_aimType == AbilityAimType.Forward)
        {
            var worldDir = ToWorldAim(abilityJoystick.InputVector);
            _playerViewController?.UpdateAbilityIndicator(worldDir);
            _playerViewController?.StorePendingAbilityDirection(worldDir);
        }
        else if (_aimType == AbilityAimType.Trajectory)
        {
            // ✅ Pass the raw joystick input (not normalized)
            _playerViewController?.UpdateTrajectoryIndicator(abilityJoystick.InputVector);

            // Still store the world direction for actual cast
            var worldDir = ToWorldAim(abilityJoystick.InputVector.normalized);
            _playerViewController?.StorePendingAbilityDirection(worldDir);
        }

        else // Dropzone
        {
            Vector2 offset01 = abilityJoystick.InputVector;
            float m = offset01.magnitude;
            if (m > 1f) offset01 /= m;

            _playerViewController?.UpdateDropIndicator(offset01);
            _playerViewController?.StorePendingDropOffset(
                _playerViewController.DropOffset01ToWorld01(offset01)
            );
        }
    }

    private void ReleaseAndCast(Vector2? releaseScreenPos)
    {
        _pressAlive = false;

        // Helper: are we releasing inside the same button?
        bool releasedInsideSame = false;
        if (_heldLongEnough && _revealingMask && _rootCanvas != null)
        {
            Camera cam = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;
            releasedInsideSame = RectTransformUtility.RectangleContainsScreenPoint(
                _revealingMask.rectTransform,
                releaseScreenPos ?? _lastScreenPos,
                cam
            );
        }

        // --- JUMP EXCEPTION ---
        // If Jump was *held* and we release OUTSIDE the button, cancel (do nothing).
        if (AbilityKind == AbilityType.Jump && _heldLongEnough)
        {
            if (!releasedInsideSame)
            {
                // cancel jump
                Debug.Log("[UIAbility] Jump: held & released outside → cancel (no cast)");
                CleanupAfterRelease();
                return;
            }
            // else: held & released inside → proceed to cast like normal tap
            // (no early-return here)
        }

        // Only ignore if we DIDN'T actually enter aim.
        // If we were aiming (joystick visible), we should CAST even if released on the same button.
        if (_heldLongEnough && IsCancellable && releasedInsideSame && !_isAiming)
        {
            Debug.Log("[UIAbility] Ignored release on same button (no aim)");
            CleanupAfterRelease();
            return;
        }


        // --- Non-cancellable abilities default path (Jump falls here unless we cancelled above) ---
        if (!IsCancellable)
        {
            if (_heldLongEnough && abilityJoystick != null)
                abilityJoystick.SetVisible(false);

            if (_playerViewController && _playerViewController.LastTrajectoryPoints != null)
            {
                BombTrajectoryBuffer.StoreUnity(_playerViewController.LastTrajectoryPoints);
                Debug.Log($"[UIAbility] Stored trajectory with {_playerViewController.LastTrajectoryPoints.Length} points");
            }
            else
            {
                Debug.LogWarning("[UIAbility] No trajectory points found to store before cast!");
            }


            TriggerAbilityNow();
            CleanupAfterRelease();
            return;
        }

        // --- Cancellable abilities flow (unchanged) ---
        bool didCancel = _heldLongEnough && _overCancel;
        if (didCancel)
        {
            abilityJoystick?.SetVisible(false);
            if (_aimType == AbilityAimType.Forward) _playerViewController?.EndAbilityIndicatorAim();
            else if (_aimType == AbilityAimType.Trajectory) _playerViewController?.EndTrajectoryIndicatorAim();
            else _playerViewController?.EndDropIndicatorAim();
            ShowCancelArea(false);
            _isAiming = _heldLongEnough = false; _usingMouse = false; _activePointerId = 0;
            return;
        }

        if (_heldLongEnough && abilityJoystick != null && _playerViewController != null)
        {
            if (_aimType == AbilityAimType.Forward)
            {
                Vector2 inputDir = abilityJoystick.GetSafeInput();
                Vector2 worldDir = ToWorldAim(inputDir);
                _playerViewController.StorePendingAbilityDirection(worldDir);
                StartCoroutine(TriggerAbilityNextFrame());
                abilityJoystick.SetVisible(false);
                _playerViewController.EndAbilityIndicatorAim();
            }
            else if (_aimType == AbilityAimType.Dropzone)// Dropzone
            {
                Vector2 offset01 = abilityJoystick.GetSafeInput();
                float m = offset01.magnitude;
                if (m > 1f) offset01 /= m;
                var world01 = _playerViewController.DropOffset01ToWorld01(offset01);
                _playerViewController.StorePendingDropOffset(world01);
                Debug.Log($"[UIAbility] Drop commit: offset01={offset01} dir01={world01} mag={world01.magnitude:F2}");
                StartCoroutine(TriggerAbilityNextFrame());
                abilityJoystick.SetVisible(false);
                _playerViewController.EndDropIndicatorAim();
            }
            else if (_aimType == AbilityAimType.Trajectory)
            {
                EnsurePlayerRefs();
                Vector2 inputDir = abilityJoystick.GetSafeInput();
                Vector2 worldDir = ToWorldAim(inputDir);
                _playerViewController.StorePendingAbilityDirection(worldDir);
                _playerViewController.StorePendingAbilityStrength(inputDir.magnitude);

                // ⬇️ take live snapshot FIRST
                var pts = _playerViewController?.SnapshotTrajectoryPoints();
                if ((pts == null || pts.Length < 2))
                {
                    // nudge once more using current stick to ensure points exist for tiny drags
                    _playerViewController?.UpdateTrajectoryIndicator(abilityJoystick.InputVector);
                    pts = _playerViewController?.SnapshotTrajectoryPoints();
                }

                // last-resort fallback (optional but robust)
                if (pts == null || pts.Length < 2)
                    pts = BuildFallbackTrajectory();

                BombTrajectoryBuffer.StoreUnity(pts);

                // ⬇️ now it's safe to end/hide
                _playerViewController.EndTrajectoryIndicatorAim();
                StartCoroutine(TriggerAbilityNextFrame());
                abilityJoystick.SetVisible(false);
            }


        }
        else
        {
            // TAP
            if (_aimType == AbilityAimType.Forward)
            {
                _playerViewController?.StorePendingAbilityDirection(GetFacingDir());
                TriggerAbilityNow();
            }
            else if (_aimType == AbilityAimType.Dropzone)
            {
                _playerViewController?.StorePendingDropOffset(Vector2.zero);
                TriggerAbilityNow();
            }
            else
            {
                // TAP
                if (_aimType == AbilityAimType.Forward)
                {
                    _playerViewController?.StorePendingAbilityDirection(GetFacingDir());
                    TriggerAbilityNow();
                }
                else if (_aimType == AbilityAimType.Dropzone)
                {
                    _playerViewController?.StorePendingDropOffset(Vector2.zero);
                    TriggerAbilityNow();
                }
                else if (_aimType == AbilityAimType.Trajectory)
                {
                    // Tap = fire at MAX range, facing direction (no snapping to old aim)
                    Vector2 facing = GetFacingDir();
                    _playerViewController?.StorePendingAbilityDirection(facing);
                    _playerViewController?.StorePendingAbilityStrength(1f); // max strength

                    var pts = BuildMaxTrajectory(facing);
                    BombTrajectoryBuffer.StoreUnity(pts);

                    StartCoroutine(TriggerAbilityNextFrame());
                }
            }

        }

        CleanupAfterRelease();
    }

    private void EnsurePlayerRefs()
    {
        if (_mobile == null)
            _mobile = FindObjectOfType<QuantumDemoInputTopDownMobile>(true);

        // prefer the live local player from your mobile controller
        if ((_playerViewController == null || _playerViewController.gameObject == null) && _mobile != null)
            _playerViewController = _mobile.localPlayer;

        // last-chance fallback
        if (_playerViewController == null)
            _playerViewController = FindObjectOfType<PlayerViewController>();
    }


    private void CleanupAfterRelease()
    {
        abilityJoystick?.SetVisible(false);

        // End both aim types safely (no conflict)
        _playerViewController?.EndAbilityIndicatorAim();
        _playerViewController?.EndDropIndicatorAim();
        _playerViewController?.EndTrajectoryIndicatorAim();

        if (IsCancellable)
            ShowCancelArea(false);

        _isAiming = _heldLongEnough = false;
        _usingMouse = false;
        _activePointerId = 0;

        if (_activeAimAbility == this)
            _activeAimAbility = null;
    }

    private void ForceEndAim()
    {
        abilityJoystick?.SetVisible(false);
        _playerViewController?.EndAbilityIndicatorAim();
        _playerViewController?.EndDropIndicatorAim();
        _playerViewController?.EndTrajectoryIndicatorAim();
        ShowCancelArea(false);
        _isAiming = _heldLongEnough = false;
    }


    public void OnEndDrag(PointerEventData e)
    {
        if (_inputLocked) return;
        if (_pressAlive)
            ReleaseAndCast(e.position);
    }

    public void OnCancel(BaseEventData e)
    {
        if (_inputLocked) return;
        if (_pressAlive)
            ReleaseAndCast(null);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (_inputLocked) return;
        if (_pressAlive)
            ReleaseAndCast(e.position);
    }

    private void Update()
    {
        if (_inputLocked) return;
        // Fallback when UI routing stops (release far away)
        if (!_pressAlive) return;

        bool released = false;

        if (_usingMouse)
        {
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                released = true;
            else
                _lastScreenPos = Mouse.current.position.ReadValue();
        }
        else
        {
            var ts = Touchscreen.current;
            if (ts == null || ts.touches.Count == 0)
                released = true;
            else
                _lastScreenPos = ts.primaryTouch.position.ReadValue();
        }

        if (released)
            ReleaseAndCast(null);
    }

    void ShowCancelArea(bool show)
    {
        if (show)
        {
            UICancelAreaManager.Instance?.SetScale(1f);
            UICancelAreaManager.Instance?.Show();
        }
        else
            UICancelAreaManager.Instance?.Hide();
    }

    void UpdateCancelHover(Vector2 screenPos)
    {
        if (UICancelAreaManager.Instance == null)
            return;
        _overCancel = UICancelAreaManager.Instance.UpdateHover(screenPos);
    }

    private static bool RectangleContainsScreenPoint(RectTransform rt, Vector2 screenPos, Canvas rootCanvas)
    {
        if (!rt) return false;
        Camera cam = null;
        if (rootCanvas && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = rootCanvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam);
    }

    // ------------------------- RELEASE -------------------------
    private Vector2 GetFacingDir()
    {
        if (_playerViewController == null) return Vector2.up;
        var fwd = _playerViewController.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) return Vector2.up;
        return new Vector2(fwd.x, fwd.z).normalized;
    }

    private IEnumerator TriggerAbilityNextFrame()
    {
        yield return null;
        TriggerAbilityNow();
    }

    private void TriggerAbilityNow()
    {
        if (_invokeAbility == null)
            TryBindTriggerNow();

        if (_invokeAbility != null)
        {
            _invokeAbility();
        }
        else
        {
            Debug.LogWarning("[UIAbility] No trigger bound for this ability button.");
        }
    }

    private void TryBindTriggerNow()
    {
        var mobile = GameObject.FindObjectOfType<QuantumDemoInputTopDownMobile>(true);
        if (mobile == null) return;

        switch (AbilityKind)
        {
            case AbilityType.Attack:
            case AbilityType.ThrowShort: _invokeAbility = mobile.PressFire; break;
            case AbilityType.Block:
            case AbilityType.ThrowLong: _invokeAbility = mobile.PressAltFire; break;
            case AbilityType.Jump: _invokeAbility = mobile.PressJump; break;
            case AbilityType.Dash: _invokeAbility = mobile.PressDash; break;
            case AbilityType.Hook: _invokeAbility = mobile.PressHook; break;
            case AbilityType.Invisibility: _invokeAbility = mobile.PressInvisibility; break;
            case AbilityType.Speedster: _invokeAbility = mobile.PressSpeedster; break;
            case AbilityType.Banana: _invokeAbility = mobile.PressBanana; break;
            case AbilityType.Bomb: _invokeAbility = mobile.PressBomb; break;
            default: break;
        }
    }

    // ------------------------- SETUP -------------------------
    private void Awake()
    {
        if (!_invisibleCanvasGroup)
            _invisibleCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        if (!_layoutElement)
            _layoutElement = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        if (!_animator)
            _animator = GetComponent<Animator>();
        if (!_audioSource)
            _audioSource = GetComponent<AudioSource>();

        if (!_revealingMask || !_hidingMask)
        {
            var images = GetComponentsInChildren<UnityEngine.UI.Image>(true);
            if (!_revealingMask && images.Length > 0)
                _revealingMask = images[0];
            if (!_hidingMask && images.Length > 1)
                _hidingMask = images[1];
        }

        _mobile = FindObjectOfType<QuantumDemoInputTopDownMobile>(true);
        _playerViewController = _mobile != null && _mobile.localPlayer != null
            ? _mobile.localPlayer
            : FindObjectOfType<PlayerViewController>();

        _rootCanvas = GetComponentInParent<Canvas>();

    }

    // ------------------------- UI STATE MGMT -------------------------
    public void SetPreviewMode(bool enable)
    {
        _isPreviewMode = enable;

        // lock out all pointer/drag logic when used as a menu card
        _inputLocked = enable;

        // keep your existing visuals
        if (_isPreviewMode)
        {
            if (_revealingMask) _revealingMask.fillAmount = 1f;
            if (_hidingMask) { _hidingMask.fillAmount = 0f; _hidingMask.enabled = true; }
            _isReady = true;

            // make EXTRA sure no joystick/cancel UI sneaks in
            abilityJoystick?.SetVisible(false);
            UICancelAreaManager.Instance?.Hide();
            _isAiming = _heldLongEnough = _pressAlive = false;
        }
        else
        {
            if (_hidingMask) _hidingMask.enabled = true;
        }
    }

    public void ToggleVisibility(bool isShown)
    {
        if (_isShown == isShown)
            return;
        _isShown = isShown;

        if (_invisibleCanvasGroup)
            _invisibleCanvasGroup.enabled = !_isShown;
        if (_layoutElement)
            _layoutElement.ignoreLayout = !_isShown;
    }

    public void UpdateAbility(Frame frame, in Ability ability, bool wasButtonPressed)
    {
        if (_isPreviewMode)
        {
            if (_revealingMask) _revealingMask.fillAmount = 1f;
            if (_hidingMask) { _hidingMask.fillAmount = 0f; _hidingMask.enabled = false; }
            _isReady = true;
            return;
        }

        float normalizedCooldown = ability.CooldownTimer.NormalizedTime.AsFloat;
        if (_revealingMask) _revealingMask.fillAmount = normalizedCooldown;
        if (_hidingMask) _hidingMask.fillAmount = 1 - normalizedCooldown;

        bool wasReady = _isReady;
        _isReady = (_revealingMask ? _revealingMask.fillAmount : 1f) >= 0.99f;

        if (_isShown)
        {
            if (_isReady && !wasReady)
            {
                if (_animator) _animator.SetTrigger(READY_ANIM_HASH);
                if (_audioSource && _readySound) _audioSource.PlayOneShot(_readySound);
            }
            else if (!_isReady)
            {
                if (wasReady)
                {
                    if (_animator) _animator.SetTrigger(ACTIVATE_ANIM_HASH);
                }
                else if (wasButtonPressed && _animator)
                {
                    AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
                    if (st.tagHash == EMPTY_STATE_TAG_HASH &&
                        ability.CooldownTimer.TimeLeft > (ability.InputBufferTimer.TimeLeft - frame.DeltaTime))
                    {
                        _animator.SetTrigger(ACTIVATION_FAILED_ANIM_HASH);
                        if (_audioSource && _activationFailedSound)
                            _audioSource.PlayOneShot(_activationFailedSound);
                    }
                }
            }
        }
    }

    // Map stick (local) to world aim (camera-relative)
    // Map stick (local) to world aim (camera-relative), **keeping joystick magnitude**
    private static Vector2 ToWorldAim(Vector2 stick)
    {
        if (stick.sqrMagnitude < 1e-6f)
            return Vector2.zero;

        // strength from stick pull (0..1)
        float mag = Mathf.Clamp01(stick.magnitude);
        Vector2 dir01 = stick / mag;  // normalized 2D

        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 f = cam.transform.forward; f.y = 0f;
            Vector3 r = cam.transform.right; r.y = 0f;
            if (f.sqrMagnitude > 1e-6f) f.Normalize();
            if (r.sqrMagnitude > 1e-6f) r.Normalize();

            // pure direction from camera
            Vector3 worldDir = r * dir01.x + f * dir01.y;
            if (worldDir.sqrMagnitude > 1e-6f) worldDir.Normalize();

            // multiply by joystick magnitude → length encodes strength
            worldDir *= mag;
            return new Vector2(worldDir.x, worldDir.z);
        }

        // Fallback: use player forward, but still carry magnitude
        var pvc = FindObjectOfType<PlayerViewController>();
        if (pvc != null)
        {
            var fwd = pvc.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();
            fwd *= mag;
            return new Vector2(fwd.x, fwd.z);
        }

        return Vector2.up * mag;
    }

    // Build a simple parabola if the indicator didn't produce points (rare, tiny drags)
    private Vector3[] BuildFallbackTrajectory()
    {
        if (_playerViewController == null) return Array.Empty<Vector3>();

        // Use same visual params as TrajectoryIndicator defaults
        const int segments = 24;
        const float startHeight = 0.3f;
        const float minDist = 3f, maxDist = 12f;
        const float minArc = 1.0f, maxArc = 5.0f;

        // Direction & strength from current stick
        Vector2 stick = abilityJoystick ? abilityJoystick.InputVector : Vector2.up;
        float strength = Mathf.Clamp01(stick.magnitude);
        Vector2 dir01 = (stick.sqrMagnitude > 1e-6f) ? stick.normalized : new Vector2(0, 1);

        // Map to world using the same helper you already have
        Vector2 world2D = ToWorldAim(dir01);
        Vector3 worldDir = new Vector3(world2D.x, 0f, world2D.y);
        if (worldDir.sqrMagnitude < 1e-6f) worldDir = _playerViewController.transform.forward;
        worldDir.Normalize();

        float dist = Mathf.Lerp(minDist, maxDist, strength);
        float arc = Mathf.Lerp(maxArc, minArc, strength);

        var pts = new Vector3[Mathf.Max(segments, 3)];
        Vector3 start = _playerViewController.transform.position + Vector3.up * startHeight;
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (float)(pts.Length - 1);
            float y = arc * Mathf.Sin(Mathf.PI * t);
            pts[i] = start + worldDir * (dist * t) + Vector3.up * y;
        }
        return pts;
    }

    // Build a parabola in world space given a 2D world dir (xz) and strength [0..1]
    private Vector3[] BuildTrajectory(Vector2 worldDir2D, float strength01)
    {
        // Match TrajectoryIndicator defaults
        const int segments = 24;
        const float startHeight = 0.3f;
        const float minDist = 3f, maxDist = 12f;
        const float minArc = 1.0f, maxArc = 5.0f;

        if (_playerViewController == null)
            return Array.Empty<Vector3>();

        strength01 = Mathf.Clamp01(strength01);

        Vector3 dir = new Vector3(worldDir2D.x, 0f, worldDir2D.y);
        if (dir.sqrMagnitude < 1e-6f)
        {
            var fwd = _playerViewController.transform.forward;
            dir = new Vector3(fwd.x, 0f, fwd.z);
        }
        dir.Normalize();

        float dist = Mathf.Lerp(minDist, maxDist, strength01);
        float arc = Mathf.Lerp(maxArc, minArc, strength01);

        var pts = new Vector3[Mathf.Max(segments, 3)];
        Vector3 start = _playerViewController.transform.position + Vector3.up * startHeight;

        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (float)(pts.Length - 1);
            float y = arc * Mathf.Sin(Mathf.PI * t);
            pts[i] = start + dir * (dist * t) + Vector3.up * y;
        }
        return pts;
    }

    private Vector3[] BuildMaxTrajectory(Vector2 worldDir2D)
    {
        if (_playerViewController == null) return Array.Empty<Vector3>();

        var ti = _playerViewController.TrajectoryIndicatorInstance;

        // Fallbacks in case indicator isn't present yet
        float maxDistance = ti ? ti.maxDistance : 26f;
        float maxArcHeight = ti ? ti.maxArcHeight : 10f;
        float startHeight = ti ? ti.startHeight : 0.25f;
        int segments = ti ? Mathf.Max(ti.segments, 3) : 12;

        Vector3 dir = new Vector3(worldDir2D.x, 0f, worldDir2D.y);
        if (dir.sqrMagnitude < 1e-6f)
        {
            var f = _playerViewController.transform.forward;
            dir = new Vector3(f.x, 0f, f.z);
        }
        dir.Normalize();

        var pts = new Vector3[segments];
        Vector3 start = _playerViewController.transform.position + Vector3.up * startHeight;

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            float y = maxArcHeight * Mathf.Sin(Mathf.PI * t);
            pts[i] = start + dir * (maxDistance * t) + Vector3.up * y;
        }
        return pts;
    }

    // Cancel aim ONLY if this button is the one currently aiming.
    // Does NOT touch other abilities' indicators.
    public void CancelOwnAimIfActive()
    {
        // only act if this ability actually owns the current aim
        if (!_isAiming || _activeAimAbility != this)
            return;

        // hide its own joystick
        abilityJoystick?.SetVisible(false);

        // end only this ability's indicator type
        switch (_aimType)
        {
            case AbilityAimType.Forward:
                _playerViewController?.EndAbilityIndicatorAim();
                break;
            case AbilityAimType.Dropzone:
                _playerViewController?.EndDropIndicatorAim();
                break;
            case AbilityAimType.Trajectory:
                _playerViewController?.EndTrajectoryIndicatorAim();
                break;
        }

        // hide cancel ring only if we own it
        UICancelAreaManager.Instance?.Hide();

        // clear state
        _isAiming = false;
        _heldLongEnough = false;
        _pressAlive = false;
        _usingMouse = false;
        _activePointerId = 0;

        if (_activeAimAbility == this)
            _activeAimAbility = null;
    }




}
