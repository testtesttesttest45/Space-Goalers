using System;
using Photon.Deterministic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public unsafe class PlayerViewController : QuantumCallbacks
{
    private static readonly int NORMALIZED_SPEED_ANIM_HASH = Animator.StringToHash("Normalized Speed");
    private static readonly int IS_GROUNDED_ANIM_HASH = Animator.StringToHash("Is Grounded");
    private static readonly int JUMP_ANIM_HASH = Animator.StringToHash("Jump");
    private static readonly int DASH_ANIM_HASH = Animator.StringToHash("Dash");
    private static readonly int ATTACK_ANIM_HASH = Animator.StringToHash("Attack");
    private static readonly int BLOCK_ANIM_HASH = Animator.StringToHash("Block");
    private static readonly int IS_KNOCKBACKED_ANIM_HASH = Animator.StringToHash("Is Knockbacked");
    private static readonly int IS_STUNNED_ANIM_HASH = Animator.StringToHash("Is Stunned");
    private static readonly int HAS_BALL_ANIM_HASH = Animator.StringToHash("Has Ball");
    private static readonly int CATCH_BALL_ANIM_HASH = Animator.StringToHash("Catch Ball");
    private static readonly int THROW_SHORT_BALL_ANIM_HASH = Animator.StringToHash("Throw Ball Short");
    private static readonly int THROW_LONG_BALL_ANIM_HASH = Animator.StringToHash("Throw Ball Long");
    private static readonly int HOOKSHOT_ANIM_HASH = Animator.StringToHash("Hookshot");
    private static readonly int BANANA = Animator.StringToHash("Banana");

    [Header("Ball Indicators")]
    [SerializeField] private DecalIndicator _aimIndicatorPrefab;
    [SerializeField] private DecalIndicator _possessionIndicator;

    [Header("Team Indicators")]
    [SerializeField] private DecalIndicator _localPlayerIndicatorPrefab;
    [SerializeField] private DecalIndicator _bluePlayerIndicatorPrefab;
    [SerializeField] private DecalIndicator _redPlayerIndicatorPrefab;
    [SerializeField] private Material _redPlayerBodyMaterial;

    [Header("Effects")]
    [SerializeField] private ParticleSystem _jumpParticleSystem;
    [SerializeField] private ParticleSystem _airJumpParticleSystem;
    [SerializeField] private ParticleSystem _landParticleSystem;
    [SerializeField] private ParticleSystem _dashParticleSystem;
    [SerializeField] private ParticleSystem _hitParticleSystem;
    [SerializeField] private ParticleSystem _blockHitParticleSystem;
    [SerializeField] private ParticleSystem _stunnedParticleSystem;
    [SerializeField] private ParticleSystem _respawnParticleSystem;
    [SerializeField] private ParticleSystem _goalParticleSystem;

    [Header("Sounds")]
    [SerializeField] private AudioClip _jumpSound;
    [SerializeField] private AudioClip _airJumpSound;
    [SerializeField] private AudioClip _landSound;
    [SerializeField] private AudioClip _dashSound;
    [SerializeField] private AudioClip _attackSound;
    [SerializeField] private AudioClip _blockSound;
    [SerializeField] private AudioClip _blockHitSound;
    [SerializeField] private AudioClip _stunnedSound;
    [SerializeField] private AudioClip _catchBallSound;
    [SerializeField] private AudioClip _throwShortBallSound;
    [SerializeField] private AudioClip _throwLongBallSound;
    [SerializeField] private AudioClip _enterVoidSound;
    [SerializeField] private AudioClip _respawnSound;
    [SerializeField] private AudioClip _invisibilitySound;
    [SerializeField] private AudioClip _speedsterSound;
    [SerializeField] private AudioClip _bananaSound;
    [SerializeField] private AudioClip _bombCastSound;

    [Header("Camera Shake")]
    [SerializeField] private CameraShakeSource _hitCameraShakeSource;
    [SerializeField] private CameraShakeSource _goalCameraShakeSource;

    [Header("World UI")]
    [SerializeField] private Canvas _worldCanvas;
    [SerializeField] private TextMeshProUGUI _nicknameText;
    [SerializeField] private Image _respawnCooldownImage;

    [Header("Hierarchy")]
    [SerializeField] private QuantumEntityView _entityView;
    [SerializeField] private Transform _playerCenterTransform;
    [SerializeField] private Transform _ballFollowTransform;
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private Renderer _playerBodyRenderer;
    [SerializeField] private Renderer[] _allRenderers;
    [SerializeField] private GameObject _blockShieldGameObject;

    [Header("Ability Indicator")]
    [SerializeField] private AbilityIndicator _abilityIndicatorPrefab;
    private AbilityIndicator _abilityIndicatorInstance;

    private bool _updateView;
    private Quaternion _aimRotation;
    private DecalIndicator _aimIndicator;
    private const float INDICATOR_AHEAD = 0.1f;

    public PlayerRef PlayerRef { get; private set; }
    public PlayerTeam PlayerTeam { get; private set; }
    public string Nickname { get; private set; }
    public QuantumEntityView EntityView => _entityView;
    public Transform BallFollowTransform => _ballFollowTransform;

    [Header("Invisibility (Material Swap)")]
    [SerializeField] private Material _invisibleMaterial;
    private readonly System.Collections.Generic.List<DecalIndicator> _teamIndicators = new();
    [Header("Team Colors")]
    [SerializeField] private Color _teamRedColor = new Color(0.62f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color _teamBlueColor = new Color(0.18f, 0.36f, 0.58f, 1f);
    [Serializable]
    private struct MaterialSwapSlot
    {
        public Renderer renderer;
        public int[] materialIndices;
        [NonSerialized] public Material[] _original;
    }

    [Header("Hookshot Sounds")]
    [SerializeField] private AudioClip _hookCastSound;
    [SerializeField] private AudioClip _hookHitSound;

    [SerializeField] private MaterialSwapSlot[] _invisTargets;
    private bool _invisAppliedMaterials;
    private bool _invisHiddenRenderers;

    [Header("Banana (View)")]
    [SerializeField] private GameObject _bananaMarkerPrefab;
    [SerializeField] private LineRenderer _bananaRingPrefab;
    private GameObject _bananaMarkerInstance;
    private LineRenderer _bananaRingInstance;
    [SerializeField] private AudioClip _bananaConsumeSound;

    [SerializeField] private TrajectoryIndicator _trajectoryIndicatorPrefab;
    private TrajectoryIndicator _trajectoryIndicatorInstance;
    public TrajectoryIndicator TrajectoryIndicatorInstance => _trajectoryIndicatorInstance;
    private DecalIndicator _teamRing;
    private DecalIndicator _localOverlay;

    [SerializeField] private DropAbilityIndicator _dropIndicatorPrefab;
    private DropAbilityIndicator _dropIndicator;
    private bool _dropHasBasis;
    private Vector3 _dropBasisF, _dropBasisR;
    private float _dropRadiusM;

    private Vector2? _pendingDropWorldMeters;

    private void Awake()
    {
        CacheOriginalBodyMat();
        _possessionIndicator.gameObject.SetActive(false);
    }

    public void OnEntityInstantiated(QuantumGame game)
    {
        var frame = game.Frames.Predicted;
        var ps = frame.Unsafe.GetPointer<PlayerStatus>(_entityView.EntityRef);
        PlayerRef = ps->PlayerRef;
        PlayerTeam = ps->PlayerTeam;
        PlayersManager.Instance.RegisterPlayer(game, this);

        ApplyTeamVisuals(force: true);
    }

    private void Start()
    {
        Frame frame = QuantumRunner.Default?.Game?.Frames.Predicted;
        if (frame != null)
        {
            RuntimePlayer runtimePlayerData = frame.GetPlayerData(PlayerRef);
            Nickname = runtimePlayerData.PlayerNickname;
            _nicknameText.text = NameUtils.CleanName(Nickname);
        }

        if (_trajectoryIndicatorPrefab)
        {
            _trajectoryIndicatorInstance = Instantiate(_trajectoryIndicatorPrefab, null);
            _trajectoryIndicatorInstance.Initialize(_playerCenterTransform);
        }
    }

    public void OnEntityDestroyed(QuantumGame game)
    {
        PlayersManager.Instance.DeregisterPlayer(this);
    }

    public void InitializeTeamIndicators(bool isLocalPlayer, int layer)
    {
        if (PlayerTeam == default)
        {
            var frame = QuantumRunner.Default?.Game?.Frames.Predicted;
            if (frame != null && frame.Exists(_entityView.EntityRef))
            {
                var ps = frame.Unsafe.GetPointer<PlayerStatus>(_entityView.EntityRef);
                PlayerTeam = ps->PlayerTeam;
            }
        }

        if (_teamIndicators.Count > 0)
        {
            for (int i = 0; i < _teamIndicators.Count; ++i)
                if (_teamIndicators[i]) Destroy(_teamIndicators[i].gameObject);
            _teamIndicators.Clear();
        }
        if (_aimIndicator) { Destroy(_aimIndicator.gameObject); _aimIndicator = null; }

        var teamRingPrefab = (PlayerTeam == PlayerTeam.Red)
          ? _redPlayerIndicatorPrefab
          : _bluePlayerIndicatorPrefab;

        if (teamRingPrefab)
        {
            _teamRing = Instantiate(teamRingPrefab, _playerCenterTransform);
            _teamRing.ChangeLayerAndMaterial(layer);
            _teamIndicators.Add(_teamRing);
        }


        if (isLocalPlayer && _localPlayerIndicatorPrefab)
        {
            _localOverlay = Instantiate(_localPlayerIndicatorPrefab, _playerCenterTransform);
            _localOverlay.ChangeLayerAndMaterial(layer);
            _teamIndicators.Add(_localOverlay);
        }

        if (isLocalPlayer && _aimIndicatorPrefab)
        {
            _aimIndicator = Instantiate(_aimIndicatorPrefab, _playerCenterTransform);
            _aimIndicator.ChangeLayerAndMaterial(layer);
            _aimIndicator.SetManualUpdate(true);
            _aimIndicator.enabled = true;
        }

        CacheInvisibilityOriginals();

        ApplyTeamVisuals(force: true);
    }

    private static readonly int[] kColorProps = {
    Shader.PropertyToID("_BaseColor"),
    Shader.PropertyToID("_Color"),
    Shader.PropertyToID("_TintColor")
};

    private void ApplyTeamTintToDecal(DecalIndicator decal, PlayerTeam team)
    {
        if (!decal) return;
        var r = decal.GetComponent<Renderer>() ?? decal.GetComponentInChildren<Renderer>();
        if (!r) return;

        var color = (PlayerTeam == PlayerTeam.Red) ? _teamRedColor : _teamBlueColor;

        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);

        bool applied = false;
        foreach (var pid in kColorProps)
        {
            if (r.sharedMaterial && r.sharedMaterial.HasProperty(pid))
            {
                mpb.SetColor(pid, color);
                applied = true;
                break;
            }
        }
        if (!applied) mpb.SetColor(kColorProps[0], color);

        r.SetPropertyBlock(mpb);
    }


    public override void OnSimulateFinished(QuantumGame game, Frame frame)
    {
        _updateView = true;
    }

    public override void OnUpdateView(QuantumGame game)
    {
        if (!_updateView) return;
        _updateView = false;

        Frame frame = game.Frames.Predicted;
        if (!frame.Exists(_entityView.EntityRef)) return;

        PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(_entityView.EntityRef);
        if (playerStatus->PlayerTeam != PlayerTeam)
        {
            PlayerTeam = playerStatus->PlayerTeam;
            ApplyTeamVisuals(force: true);
        }
        AbilityInventory* abilityInventory = frame.Unsafe.GetPointer<AbilityInventory>(_entityView.EntityRef);

        if (frame.Has<BananaTrapOwner>(_entityView.EntityRef))
        {
            var trap = frame.Unsafe.GetPointer<BananaTrapOwner>(_entityView.EntityRef);
            bool show = trap->Active;
            if (show)
            {
                if (_bananaMarkerPrefab && !_bananaMarkerInstance)
                    _bananaMarkerInstance = Instantiate(_bananaMarkerPrefab);
                if (_bananaMarkerInstance && !_bananaMarkerInstance.activeSelf)
                    _bananaMarkerInstance.SetActive(true);

                var pFP = trap->Pos.ToUnityVector3();
                if (_bananaMarkerInstance)
                    _bananaMarkerInstance.transform.position = new Vector3(pFP.x, pFP.y, pFP.z);

                if (!_bananaRingInstance)
                {
                    if (_bananaRingPrefab)
                    {
                        _bananaRingInstance = Instantiate(_bananaRingPrefab);
                    }
                    else
                    {
                        var go = new GameObject("BananaRing");
                        _bananaRingInstance = go.AddComponent<LineRenderer>();
                        _bananaRingInstance.useWorldSpace = true;
                        _bananaRingInstance.widthMultiplier = 0.2f;
                        _bananaRingInstance.material = new Material(Shader.Find("Sprites/Default"));
                        _bananaRingInstance.textureMode = LineTextureMode.Stretch;
                    }

                    if (_bananaRingInstance.material != null)
                        _bananaRingInstance.material = new Material(_bananaRingInstance.material);

                    if (_bananaRingInstance.material != null)
                        _bananaRingInstance.material.renderQueue = 3100;

                    _bananaRingInstance.alignment = LineAlignment.View;
                }

                if (_bananaRingInstance && !_bananaRingInstance.gameObject.activeSelf)
                    _bananaRingInstance.gameObject.SetActive(true);

                Color ringColor = (PlayerTeam == PlayerTeam.Red) ? _teamRedColor : _teamBlueColor;
                ringColor.a = 0.9f;

                var mat = _bananaRingInstance.material;
                if (mat != null)
                {
                    int pidBase = Shader.PropertyToID("_BaseColor");
                    int pidCol = Shader.PropertyToID("_Color");
                    if (mat.HasProperty(pidBase)) mat.SetColor(pidBase, ringColor);
                    else if (mat.HasProperty(pidCol)) mat.SetColor(pidCol, ringColor);
                }
                _bananaRingInstance.startColor = ringColor;
                _bananaRingInstance.endColor = ringColor;

                float r = trap->TriggerRadius.AsFloat;
                var center = new Vector3(pFP.x, pFP.y, pFP.z);
                BuildCircle(_bananaRingInstance, center, r, 64, 0.03f);
            }
            else
            {
                if (_bananaMarkerInstance && _bananaMarkerInstance.activeSelf)
                    _bananaMarkerInstance.SetActive(false);
                if (_bananaRingInstance && _bananaRingInstance.gameObject.activeSelf)
                    _bananaRingInstance.gameObject.SetActive(false);
            }
        }
        else
        {
            if (_bananaMarkerInstance && _bananaMarkerInstance.activeSelf)
                _bananaMarkerInstance.SetActive(false);
            if (_bananaRingInstance && _bananaRingInstance.gameObject.activeSelf)
                _bananaRingInstance.gameObject.SetActive(false);
        }

        CharacterController3D* kcc = frame.Unsafe.GetPointer<CharacterController3D>(_entityView.EntityRef);
        PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(playerStatus->PlayerMovementData.Id);
        CharacterController3DConfig defaultKCCConfig = frame.FindAsset<CharacterController3DConfig>(playerMovementData.DefaultKCCSettings.Id);

        UpdateAnimatorMovementSpeed(kcc, defaultKCCConfig);
        UpdateAnimatorIsGrounded(kcc);
        UpdateAnimatorHasBall(playerStatus);
        UpdateAnimatorIsKnockbacked(playerStatus);
        UpdateAnimatorIsStunned(playerStatus);
        UpdateIsRespawning(playerStatus);

        bool dashActive = IsAbilityActive(abilityInventory, AbilityType.Dash);

        bool speedsterActive = false;
        try { speedsterActive = IsAbilityActive(abilityInventory, AbilityType.Speedster); } catch (System.Exception) { /* enum might not exist */ }
        if (!speedsterActive)
        {
            speedsterActive |= IsAbilityActiveByName(frame, abilityInventory, "SpeedsterAbilityData");
        }
        bool externalBoost = playerStatus->ExternalSpeedsterActive && playerStatus->ExternalSpeedster.IsRunning;
        speedsterActive |= externalBoost;
        float horizSpeed = kcc->Velocity.ToUnityVector3().X0Z().magnitude;
        const float MOVE_THRESHOLD = 0.25f;

        bool wantTrail = dashActive || (speedsterActive && horizSpeed > MOVE_THRESHOLD);
        ToggleMotionTrailFX(wantTrail);

        ToggleBlockShieldFX(abilityInventory);
        ToggleStunnedFX(playerStatus);
        TogglePossessionIndicator(playerStatus);
        ToggleInvisibilityFX(playerStatus, abilityInventory);

        int aidx = abilityInventory->ActiveAbilityInfo.ActiveAbilityIndex;
        string atype = "None";
        if (aidx >= 0 && aidx < abilityInventory->Abilities.Length)
        {
            var a = abilityInventory->Abilities[aidx];
            var data = frame.FindAsset<AbilityData>(a.AbilityData.Id);
            atype = data ? data.GetType().Name : "Unknown";
        }
        if (atype == nameof(BananaAbilityData))
        {
            MarkPendingDropUsed();
        }

        bool blockActive = IsAbilityActive(abilityInventory, AbilityType.Block);

        LocalPlayerAccess localPlayerAccess = LocalPlayersManager.Instance.GetLocalPlayerAccess(PlayerRef);
        if (localPlayerAccess != null)
            localPlayerAccess.UIAbilityController.UpdateAbilities(frame, this, playerStatus, abilityInventory);

        QuantumDemoInputTopDown inp = *frame.GetPlayerInput(PlayerRef);
        var aim = new Vector2(inp.AimDirection.X.AsFloat, inp.AimDirection.Y.AsFloat);
        var mov = new Vector2(inp.MoveDirection.X.AsFloat, inp.MoveDirection.Y.AsFloat);

        Vector3 dir;
        if (aim.sqrMagnitude > 0.0001f) dir = new Vector3(aim.x, 0f, aim.y);
        else if (mov.sqrMagnitude > 0.0001f) dir = new Vector3(mov.x, 0f, mov.y);
        else dir = _playerCenterTransform.forward;
        _aimRotation = Quaternion.LookRotation(dir.normalized);
    }

    private void LateUpdate()
    {
        if (_aimIndicator && _aimIndicator.isActiveAndEnabled)
        {
            Vector3 fwd = _playerCenterTransform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = transform.forward;
            fwd.Normalize();

            Vector3 pos = _playerCenterTransform.position + fwd * INDICATOR_AHEAD;
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

            _aimIndicator.transform.SetPositionAndRotation(pos, rot);
            _aimIndicator.UpdateDecal(rot);
        }

        if (Camera.main) _worldCanvas.transform.forward = Camera.main.transform.forward;
    }

    private void UpdateAnimatorMovementSpeed(CharacterController3D* kcc, CharacterController3DConfig defaultKCCConfig)
    {
        float normalizedSpeed = kcc->Velocity.ToUnityVector3().X0Z().magnitude / defaultKCCConfig.MaxSpeed.AsFloat;
        _animator.SetFloat(NORMALIZED_SPEED_ANIM_HASH, normalizedSpeed);
    }

    private void UpdateAnimatorIsGrounded(CharacterController3D* kcc)
    {
        _animator.SetBool(IS_GROUNDED_ANIM_HASH, kcc->Grounded);
    }

    private void UpdateAnimatorHasBall(PlayerStatus* playerStatus)
    {
        _animator.SetBool(HAS_BALL_ANIM_HASH, playerStatus->IsHoldingBall);
    }

    private void UpdateAnimatorIsKnockbacked(PlayerStatus* playerStatus)
    {
        _animator.SetBool(IS_KNOCKBACKED_ANIM_HASH, playerStatus->IsKnockbacked);
    }

    private void UpdateAnimatorIsStunned(PlayerStatus* playerStatus)
    {
        _animator.SetBool(IS_STUNNED_ANIM_HASH, playerStatus->IsStunned);
    }

    private void UpdateIsRespawning(PlayerStatus* playerStatus)
    {
        if (playerStatus->IsRespawning)
        {
            if (_allRenderers[0].gameObject.activeSelf)
            {
                foreach (var r in _allRenderers) r.gameObject.SetActive(false);
                _aimIndicator?.gameObject.SetActive(false);
                _respawnCooldownImage.gameObject.SetActive(true);
            }
            _respawnCooldownImage.fillAmount = playerStatus->RespawnTimer.NormalizedTime.AsFloat;
        }
        else
        {
            if (!_allRenderers[0].gameObject.activeSelf)
            {
                foreach (var r in _allRenderers) r.gameObject.SetActive(true);
                _aimIndicator?.gameObject.SetActive(true);
                _respawnCooldownImage.gameObject.SetActive(false);
                _audioSource.PlayOneShot(_respawnSound);
                _respawnParticleSystem.Play();
            }
        }
    }

    private void ToggleDashFX(AbilityInventory* inv)
    {
        if (inv->GetAbility(AbilityType.Dash).IsDelayedOrActive)
        {
            if (!_dashParticleSystem.isPlaying) _dashParticleSystem.Play();
        }
        else
        {
            if (_dashParticleSystem.isPlaying) _dashParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static int FindAbilityIndex(AbilityInventory* inv, AbilityType t)
    {
        for (int i = 0; i < inv->Abilities.Length; i++)
        {
            if (!inv->Abilities[i].AbilityData.IsValid) continue;
            if (inv->Abilities[i].AbilityType == t) return i;
        }
        return -1;
    }

    private static bool IsAbilityActive(AbilityInventory* inv, AbilityType t)
    {
        int idx = FindAbilityIndex(inv, t);
        if (idx < 0) return false;
        if (inv->ActiveAbilityInfo.ActiveAbilityIndex == idx) return true;
        return inv->Abilities[idx].IsDelayedOrActive;
    }

    private bool _shieldPrev;
    private void ToggleBlockShieldFX(AbilityInventory* inv)
    {
        bool on = IsAbilityActive(inv, AbilityType.Block);

        if (on)
        {
            if (!_blockShieldGameObject.activeSelf)
            {
                _blockShieldGameObject.SetActive(true);
                _audioSource.PlayOneShot(_blockSound);
                Debug.Log("[VIEW] Shield ON (Block active)");
            }
        }
        else
        {
            if (_blockShieldGameObject.activeSelf)
            {
                _blockShieldGameObject.SetActive(false);
                Debug.Log("[VIEW] Shield OFF (Block inactive)");
            }
        }

        if (_shieldPrev != on)
        {
            Debug.Log($"[VIEW] Shield state change => {(on ? "ON" : "OFF")}");
            _shieldPrev = on;
        }
    }

    private void ToggleStunnedFX(PlayerStatus* playerStatus)
    {
        if (playerStatus->IsStunned)
        {
            if (!_stunnedParticleSystem.isPlaying) _stunnedParticleSystem.Play();
        }
        else
        {
            if (_stunnedParticleSystem.isPlaying) _stunnedParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void TogglePossessionIndicator(PlayerStatus* playerStatus)
    {
        if (playerStatus->IsHoldingBall)
        {
            if (!_possessionIndicator.gameObject.activeSelf) _possessionIndicator.gameObject.SetActive(true);
        }
        else
        {
            if (_possessionIndicator.gameObject.activeSelf) _possessionIndicator.gameObject.SetActive(false);
        }
    }

    public void UpdateAim(Vector2 movementInput, Vector2 aimInput)
    {
        if (aimInput != default) _aimRotation = Quaternion.LookRotation(aimInput.X0Y());
        else if (movementInput != default) _aimRotation = Quaternion.LookRotation(movementInput.X0Y());
        else _aimRotation = Quaternion.LookRotation(transform.forward);
    }

    public void OnPlayerJumped(EventOnPlayerJumped e)
    {
        _audioSource.PlayOneShot(_jumpSound);
        _animator.SetTrigger(JUMP_ANIM_HASH);
        RotateAndPlayFX(_jumpParticleSystem, transform.rotation.eulerAngles.y);
    }

    public void OnPlayerAirJumped(EventOnPlayerAirJumped e)
    {
        _audioSource.PlayOneShot(_airJumpSound);
        _animator.SetTrigger(JUMP_ANIM_HASH);
        RotateAndPlayFX(_airJumpParticleSystem, transform.rotation.eulerAngles.y);
    }

    public void OnPlayerLanded(EventOnPlayerLanded e)
    {
        _audioSource.PlayOneShot(_landSound);
        RotateAndPlayFX(_landParticleSystem, transform.rotation.eulerAngles.y);
    }

    public void OnPlayerDashed(EventOnPlayerDashed e)
    {
        _animator.SetTrigger(DASH_ANIM_HASH);
        _audioSource.PlayOneShot(_dashSound);
    }

    public void OnPlayerAttacked(EventOnPlayerAttacked e)
    {
        _animator.SetTrigger(ATTACK_ANIM_HASH);
        _audioSource.PlayOneShot(_attackSound);
    }

    public void OnPlayerBlocked(EventOnPlayerBlocked e)
    {
        _animator.SetTrigger(BLOCK_ANIM_HASH);
        Debug.Log("[VIEW] Anim Trigger: BLOCK");
    }

    public void OnPlayerHookshot(EventOnPlayerHookshot e)
    {
        _audioSource.PlayOneShot(_hookCastSound, 0.6f);
        if (e.PlayerEntityRef == _entityView.EntityRef)
            _animator.SetTrigger(HOOKSHOT_ANIM_HASH);
    }

    public void OnInvisibilityActivated(EventOnInvisibilityActivated e)
    {
        _audioSource.PlayOneShot(_invisibilitySound, 0.6f);
    }

    public void OnBananaActivated(EventOnBananaActivated e)
    {
        _animator.SetTrigger(BANANA);
        _audioSource.PlayOneShot(_bananaSound, 0.6f);
    }

    public void OnSpeedsterActivated(EventOnSpeedsterActivated e)
    {
        _audioSource.PlayOneShot(_speedsterSound, 0.1f);
        if (e.PlayerEntityRef != _entityView.EntityRef) return;   // only my view
        ToggleMotionTrailFX(true);
    }

    public void OnSpeedsterEnded(EventOnSpeedsterEnded e)
    {
        if (e.PlayerEntityRef != _entityView.EntityRef) return;

        // If dash is still active, keep the trail; otherwise stop it.
        var game = QuantumRunner.Default?.Game;
        var frame = game?.Frames?.Predicted;
        bool keep = false;

        if (frame != null && frame.Exists(_entityView.EntityRef))
        {
            AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(_entityView.EntityRef);
            keep = IsAbilityActive(inv, AbilityType.Dash); // you already have this helper
        }

        ToggleMotionTrailFX(keep);
    }


    public void OnPlayerHit(EventOnPlayerHit e)
    {
        _hitParticleSystem.Play();
        _hitCameraShakeSource.Shake();
    }

    internal void OnPlayerBlockHit(EventOnPlayerBlockHit e)
    {
        _audioSource.PlayOneShot(_blockHitSound);
        float rotationAngle = Vector3.SignedAngle(transform.forward, -e.HitLateralDirection.ToUnityVector3(), Vector3.up);
        RotateAndPlayFX(_blockHitParticleSystem, rotationAngle);
        _hitCameraShakeSource.Shake();
    }

    public void OnPlayerCaughtBall(EventOnPlayerCaughtBall e)
    {
        _animator.SetTrigger(CATCH_BALL_ANIM_HASH);
        _audioSource.PlayOneShot(_catchBallSound);
    }

    public void OnPlayerThrewBall(EventOnPlayerThrewBall e)
    {
        if (e.IsLongThrow)
        {
            _animator.SetTrigger(THROW_LONG_BALL_ANIM_HASH);
            _audioSource.PlayOneShot(_throwLongBallSound);
        }
        else
        {
            _animator.SetTrigger(THROW_SHORT_BALL_ANIM_HASH);
            _audioSource.PlayOneShot(_throwShortBallSound);
        }
    }

    public void OnPlayerStunned(EventOnPlayerStunned e)
    {
        _audioSource.PlayOneShot(_stunnedSound);
    }

    public void OnGoalScored(EventOnGoalScored e)
    {
        _goalParticleSystem.Play();
        _goalCameraShakeSource.Shake();
    }

    public void OnPlayerEnteredVoid(EventOnPlayerEnteredVoid e)
    {
        _audioSource.PlayOneShot(_enterVoidSound);
    }

    private void RotateAndPlayFX(ParticleSystem ps, float rotationAngle)
    {
        var main = ps.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(rotationAngle * Mathf.Deg2Rad);
        ps.Play();
    }

    private bool _invisApplied;
    private void ToggleInvisibilityFX(PlayerStatus* playerStatus, AbilityInventory* abilityInventory)
    {
        if (playerStatus->IsRespawning) { InvisibilityOff_All(); return; }
        if ((int)AbilityType.Invisibility >= abilityInventory->Abilities.Length) { InvisibilityOff_All(); return; }

        bool active = abilityInventory->GetAbility(AbilityType.Invisibility).IsDelayedOrActive;
        if (!active) { InvisibilityOff_All(); return; }

        bool allyOrSelf = IsAllyOrSelfForAnyLocalViewer();
        if (allyOrSelf)
        {
            if (!_invisAppliedMaterials) InvisibilityOn_Materials();
            if (_invisHiddenRenderers) InvisibilityOff_Hidden();
        }
        else
        {
            if (!_invisHiddenRenderers) InvisibilityOn_HideRenderers();
            if (_invisAppliedMaterials) InvisibilityOff_Materials();
        }
    }

    private void InvisibilityOn_HideRenderers()
    {
        if (_allRenderers != null)
            for (int i = 0; i < _allRenderers.Length; ++i)
                if (_allRenderers[i]) _allRenderers[i].enabled = false;

        if (_possessionIndicator) _possessionIndicator.gameObject.SetActive(false);
        for (int i = 0; i < _teamIndicators.Count; ++i)
            if (_teamIndicators[i]) _teamIndicators[i].gameObject.SetActive(false);
        if (_nicknameText) _nicknameText.gameObject.SetActive(false);

        _invisHiddenRenderers = true;
    }

    private void InvisibilityOff_Hidden()
    {
        if (_allRenderers != null)
            for (int i = 0; i < _allRenderers.Length; ++i)
                if (_allRenderers[i]) _allRenderers[i].enabled = true;

        if (_possessionIndicator) _possessionIndicator.gameObject.SetActive(true);
        for (int i = 0; i < _teamIndicators.Count; ++i)
            if (_teamIndicators[i]) _teamIndicators[i].gameObject.SetActive(true);
        if (_nicknameText) _nicknameText.gameObject.SetActive(true);

        _invisHiddenRenderers = false;
    }

    private void InvisibilityOff_All()
    {
        if (_invisAppliedMaterials) InvisibilityOff_Materials();
        if (_invisHiddenRenderers) InvisibilityOff_Hidden();
    }

    private void InvisibilityOn_Materials()
    {
        if (_invisibleMaterial == null || _invisTargets == null) return;

        for (int i = 0; i < _invisTargets.Length; i++)
        {
            var slot = _invisTargets[i];
            var r = slot.renderer;
            if (!r) continue;

            var mats = (Material[])r.sharedMaterials.Clone();

            if (slot.materialIndices != null && slot.materialIndices.Length > 0)
            {
                for (int k = 0; k < slot.materialIndices.Length; k++)
                {
                    int idx = slot.materialIndices[k];
                    if (idx >= 0 && idx < mats.Length) mats[idx] = _invisibleMaterial;
                }
            }
            else
            {
                for (int m = 0; m < mats.Length; m++) mats[m] = _invisibleMaterial;
            }

            r.sharedMaterials = mats;
        }

        _invisAppliedMaterials = true;
    }

    private void InvisibilityOff_Materials()
    {
        if (_invisTargets == null) { _invisAppliedMaterials = false; return; }

        for (int i = 0; i < _invisTargets.Length; i++)
        {
            var slot = _invisTargets[i];
            var r = slot.renderer;
            var original = slot._original;
            if (!r || original == null) continue;
            r.sharedMaterials = original;
        }

        _invisAppliedMaterials = false;
    }

    private bool IsAllyOrSelfForAnyLocalViewer()
    {
        if (LocalPlayersManager.Instance.GetLocalPlayerAccess(PlayerRef) != null)
            return true;

        foreach (var access in LocalPlayersManager.Instance.LocalPlayerAccessCollection)
            if (access?.LocalPlayer != null && access.LocalPlayer.PlayerTeam == this.PlayerTeam)
                return true;

        return false;
    }

    private void CacheInvisibilityOriginals()
    {
        if (_invisTargets == null || _invisAppliedMaterials) return;
        for (int i = 0; i < _invisTargets.Length; i++)
        {
            var r = _invisTargets[i].renderer;
            if (!r) continue;
            _invisTargets[i]._original = (Material[])r.sharedMaterials.Clone();
        }
    }

    private static void BuildCircle(LineRenderer lr, Vector3 center, float radius, int segments = 64, float yLift = 0.03f)
    {
        if (!lr) return;
        lr.loop = true;
        lr.positionCount = segments;

        float step = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, yLift, Mathf.Sin(a) * radius));
        }
    }

    public void BeginAbilityIndicatorAim(Camera cam)
    {
        if (_abilityIndicatorInstance == null && _abilityIndicatorPrefab != null)
        {
            _abilityIndicatorInstance = Instantiate(_abilityIndicatorPrefab, null);
            _abilityIndicatorInstance.Initialize(_playerCenterTransform);
        }

        if (_abilityIndicatorInstance == null)
        {
            Debug.LogWarning("[VIEW] AbilityIndicator: prefab missing");
            return;
        }

        _abilityIndicatorInstance.BeginAimFromCamera(cam);
        _abilityIndicatorInstance.SetVisible(true);
    }

    public void EndAbilityIndicatorAim()
    {
        if (_abilityIndicatorInstance == null) return;
        _abilityIndicatorInstance.EndAim();
        _abilityIndicatorInstance.SetVisible(false);
    }

    public void UpdateAbilityIndicator(Vector2 input)
    {
        if (_abilityIndicatorInstance == null) return;
        _abilityIndicatorInstance.UpdateDirection(input);
        if (input.sqrMagnitude > 0.001f) _lastAbilityIndicatorDir = input;
    }


    public Vector2? PendingAbilityDirection;

    private Vector2? _pendingAimDir;

    public void StorePendingAbilityDirection(Vector2 input)
    {
        _pendingAimDir = input;
    }

    public Vector2? ConsumePendingAimDir()
    {
        var v = _pendingAimDir;
        _pendingAimDir = null;
        return v;
    }

    public Vector2? ConsumePendingAbilityDirection()
    {
        var dir = PendingAbilityDirection;
        PendingAbilityDirection = null;
        return dir;
    }

    public void HideAbilityIndicator()
    {
        if (_abilityIndicatorInstance)
            _abilityIndicatorInstance.SetVisible(false);
    }

    private Vector2 _lastAbilityIndicatorDir;

    public Vector2 GetLastIndicatorDirection()
    {
        if (_lastAbilityIndicatorDir.sqrMagnitude > 0.001f)
            return _lastAbilityIndicatorDir;
        var fwd = transform.forward;
        return new Vector2(fwd.x, fwd.z).normalized;
    }

    public void UpdateDropIndicator(Vector2 offset01)
    {
        _dropIndicator?.UpdateOffset(offset01);
    }

    public Vector2 DropOffset01ToWorld(Vector2 offset01)
    {
        if (!_dropHasBasis) return Vector2.zero;
        Vector3 w = _dropBasisR * offset01.x + _dropBasisF * offset01.y;
        w.y = 0f;
        return new Vector2(w.x, w.z) * _dropRadiusM;
    }

    private Vector2? _pendingDropWorld01;

    public void StorePendingDropOffset(Vector2 world01)
    {
        float m = world01.magnitude;
        if (m > 1f) world01 /= m;
        _pendingDropWorld01 = world01;
    }

    public Vector2? PeekPendingDropOffset() => _pendingDropWorld01;

    public Vector2? ConsumePendingDropOffset()
    {
        var v = _pendingDropWorld01;
        _pendingDropWorld01 = null;
        return v;
    }

    public Vector2 DropOffset01ToWorld01(Vector2 offset01)
    {
        if (!_dropHasBasis) return Vector2.zero;
        Vector3 w = _dropBasisR * offset01.x + _dropBasisF * offset01.y;
        w.y = 0f;
        var v = new Vector2(w.x, w.z);
        float m = v.magnitude;
        if (m > 1f) v /= m;
        return v;
    }

    public void MarkPendingDropUsed()
    {
        _pendingDropWorld01 = null;
    }

    public void BeginDropIndicatorAim(Camera cam, float radiusMeters)
    {
        _dropRadiusM = Mathf.Max(0f, radiusMeters);
        if (!_dropIndicator && _dropIndicatorPrefab)
        {
            _dropIndicator = Instantiate(_dropIndicatorPrefab, null);
            _dropIndicator.Initialize(_playerCenterTransform, _dropRadiusM);
        }
        if (!cam) { _dropHasBasis = false; _dropIndicator?.EndAim(); return; }
        _dropBasisF = cam.transform.forward; _dropBasisF.y = 0f; _dropBasisF.Normalize();
        _dropBasisR = cam.transform.right; _dropBasisR.y = 0f; _dropBasisR.Normalize();
        _dropHasBasis = true;

        if (_dropIndicator)
            _dropIndicator.BeginAimWithBasis(_dropBasisF, _dropBasisR);

    }
    public void EndDropIndicatorAim()
    {
        _dropHasBasis = false;
        _dropIndicator?.EndAim();
    }

    public Vector2? ConsumePendingDropOffsetMeters(float radiusMeters)
    {
        if (!_pendingDropWorld01.HasValue) return null;
        var scaled = _pendingDropWorld01.Value * radiusMeters;
        _pendingDropWorld01 = null;
        return scaled;
    }

    public bool WasTrajectoryAimed { get; private set; }

    public void BeginTrajectoryIndicatorAim(Camera cam)
    {
        if (_trajectoryIndicatorInstance == null && _trajectoryIndicatorPrefab != null)
        {
            _trajectoryIndicatorInstance = Instantiate(_trajectoryIndicatorPrefab, null);
            _trajectoryIndicatorInstance.Initialize(_playerCenterTransform);
        }

        if (_trajectoryIndicatorInstance == null) return;

        _trajectoryIndicatorInstance.BeginAimFromCamera(cam);
        _trajectoryIndicatorInstance.SetVisible(true);
        SeedMinimalTrajectory();
        WasTrajectoryAimed = true;
        LastTrajectoryPoints = null;
    }

    public void EndTrajectoryIndicatorAim()
    {
        if (_trajectoryIndicatorInstance == null) return;

        var pts = _trajectoryIndicatorInstance.GetCurrentPoints();
        if (pts != null && pts.Length >= 2)
            LastTrajectoryPoints = pts;

        _trajectoryIndicatorInstance.EndAim();
        WasTrajectoryAimed = false;
    }

    public void UpdateTrajectoryIndicator(Vector2 input)
    {
        if (_trajectoryIndicatorInstance == null)
            return;

        _trajectoryIndicatorInstance.UpdateDirection(input);

        if (input.sqrMagnitude > 0.001f)
            _lastAbilityIndicatorDir = input;

        var pts = _trajectoryIndicatorInstance.GetCurrentPoints();
        if (pts != null && pts.Length >= 2)
            LastTrajectoryPoints = pts;
    }

    public Vector3[] SnapshotTrajectoryPoints()
    {
        if (_trajectoryIndicatorInstance == null) return null;
        var src = _trajectoryIndicatorInstance.GetCurrentPoints();
        if (src == null || src.Length < 2) return null;
        var copy = new Vector3[src.Length];
        Array.Copy(src, copy, src.Length);
        return copy;
    }

    public void SeedMinimalTrajectory()
    {
        if (_trajectoryIndicatorInstance == null) return;
        var fwd = _playerCenterTransform ? _playerCenterTransform.forward : transform.forward;
        var seed = new Vector2(fwd.x, fwd.z).normalized * 0.06f; // tiny but non-zero
        UpdateTrajectoryIndicator(seed);
    }

    public Vector3[] LastTrajectoryPoints { get; private set; }


    public FP CastStrength { get; private set; }

    public void StorePendingAbilityStrength(float joystickMagnitude)
    {
        CastStrength = FP.FromFloat_UNSAFE(joystickMagnitude);
    }

    static readonly int PID_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int PID_Color = Shader.PropertyToID("_Color");
    static readonly int PID_TintColor = Shader.PropertyToID("_TintColor");
    static readonly int PID_Emission = Shader.PropertyToID("_EmissionColor");
    static readonly int PID_Emissive = Shader.PropertyToID("_EmissiveColor");

    static void TintMaterial(Material m, Color tint, bool killEmission)
    {
        if (!m) return;
        if (m.HasProperty(PID_BaseColor)) m.SetColor(PID_BaseColor, tint);
        else if (m.HasProperty(PID_Color)) m.SetColor(PID_Color, tint);
        else if (m.HasProperty(PID_TintColor)) m.SetColor(PID_TintColor, tint);
        else m.color = tint;

        if (killEmission)
        {
            if (m.HasProperty(PID_Emission)) m.SetColor(PID_Emission, Color.black);
            if (m.HasProperty(PID_Emissive)) m.SetColor(PID_Emissive, Color.black);
            m.DisableKeyword("_EMISSION");
        }
        else
        {
            if (m.HasProperty(PID_Emission)) m.SetColor(PID_Emission, tint);
            if (m.HasProperty(PID_Emissive)) m.SetColor(PID_Emissive, tint);
            m.EnableKeyword("_EMISSION");
        }
    }

    private static void RetintIndicatorHierarchy(DecalIndicator root, Color tint, bool killEmission = true)
    {
        if (!root) return;
        foreach (var p in root.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true))
        {
            if (!p) continue;
            if (p.material) p.material = new Material(p.material);
            if (p.material) TintMaterial(p.material, tint, killEmission);
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r) continue;
            var mats = r.sharedMaterials;
            if (mats == null) continue;
            for (int m = 0; m < mats.Length; m++)
                if (mats[m]) mats[m] = new Material(mats[m]);
            r.sharedMaterials = mats;
        }
    }


    private PlayerTeam _appliedTeam = (PlayerTeam)255;
    private Material _cachedOriginalBodyMaterial;

    private void CacheOriginalBodyMat()
    {
        if (_playerBodyRenderer != null && _cachedOriginalBodyMaterial == null)
        {
            _cachedOriginalBodyMaterial = _playerBodyRenderer.sharedMaterial;
        }
    }

    private void ApplyTeamVisuals(bool force = false)
    {
        if (!force && (PlayerTeam == default || _appliedTeam == PlayerTeam))
            return;

        Debug.Log($"[VIEW] ApplyTeamVisuals ENTRY force={force} team={PlayerTeam}");

        var color = (PlayerTeam == PlayerTeam.Red) ? _teamRedColor : _teamBlueColor;

        if (_aimIndicator)
        {
            _aimIndicator.SetColor(color);
            Debug.Log($"Set aim indicator color to: {color}");
            RetintIndicatorHierarchy(_aimIndicator, color, false);
        }

        if (_playerBodyRenderer)
        {
            if (PlayerTeam == PlayerTeam.Red && _redPlayerBodyMaterial)
                _playerBodyRenderer.sharedMaterial = _redPlayerBodyMaterial;
            else if (_cachedOriginalBodyMaterial)
                _playerBodyRenderer.sharedMaterial = _cachedOriginalBodyMaterial;
        }

        if (_teamRing)
        {
            RetintIndicatorHierarchy(_teamRing, color, true);
        }
        if (_localOverlay)
        {
            RetintIndicatorHierarchy(_localOverlay, color, true);

        }

        _appliedTeam = PlayerTeam;
        Debug.Log($"[VIEW] ApplyTeamVisuals => {PlayerTeam}, color={color}");
    }
    private void ToggleMotionTrailFX(bool on)
    {
        if (on)
        {
            if (!_dashParticleSystem.isPlaying) _dashParticleSystem.Play();
        }
        else
        {
            if (_dashParticleSystem.isPlaying)
                _dashParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static int FindAbilityIndexByName(Frame frame, AbilityInventory* inv, string className)
    {
        for (int i = 0; i < inv->Abilities.Length; i++)
        {
            if (!inv->Abilities[i].AbilityData.IsValid) continue;
            var data = frame.FindAsset<AbilityData>(inv->Abilities[i].AbilityData.Id);
            if (data != null && data.GetType().Name == className) return i;
        }
        return -1;
    }

    private static bool IsAbilityActiveByName(Frame frame, AbilityInventory* inv, string className)
    {
        int idx = FindAbilityIndexByName(frame, inv, className);
        if (idx < 0) return false;
        if (inv->ActiveAbilityInfo.ActiveAbilityIndex == idx) return true;
        return inv->Abilities[idx].IsDelayedOrActive;
    }

    public void OnInvisibilityEnded(EventOnInvisibilityEnded e)
    {
        if (e.PlayerEntityRef != _entityView.EntityRef) return;

        InvisibilityOff_All();

        PlayerTeam = ResolveTeamFromFrame();

        ApplyTeamVisuals(force: true);
    }


    public void OnPlayerHookHit(EventOnPlayerHookHit e)
    {
        _audioSource.PlayOneShot(_hookHitSound, 0.6f);
    }

    private PlayerTeam ResolveTeamFromFrame()
    {
        var game = QuantumRunner.Default?.Game;
        var frame = game?.Frames?.Predicted;
        if (frame != null && frame.Exists(_entityView.EntityRef))
        {
            var ps = frame.Unsafe.GetPointer<PlayerStatus>(_entityView.EntityRef);
            return ps->PlayerTeam;
        }
        return PlayerTeam;
    }

    public void OnBananaConsumed(EventOnBananaConsumed e)
    {
        if (e.PlayerEntityRef != _entityView.EntityRef) return;

        if (_bananaConsumeSound)
            _audioSource.PlayOneShot(_bananaConsumeSound, 0.6f);

        // Nice feedback: if ally boost, ensure trail shows right away
        if (e.IsAlly)
            ToggleMotionTrailFX(true);
    }

    public void OnBombCast(EventOnBombCast e)
    {
        _animator.SetTrigger(THROW_LONG_BALL_ANIM_HASH);

        _audioSource.PlayOneShot(_bombCastSound);
    }



}