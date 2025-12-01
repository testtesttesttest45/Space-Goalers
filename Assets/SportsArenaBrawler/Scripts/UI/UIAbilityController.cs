using System;
using System.Collections;
using System.Collections.Generic;
using Quantum;
using UnityEngine;
using UnityEngine.UI;
using UIButton = UnityEngine.UI.Button;

public class UIAbilityController : MonoBehaviour
{
    [SerializeField]
    private AbilityType[] _uiAbilityOrder = new AbilityType[] {
        AbilityType.Attack, AbilityType.Block, AbilityType.Jump,
        AbilityType.ThrowShort, AbilityType.ThrowLong,
        AbilityType.Dash, AbilityType.Invisibility, AbilityType.Banana, AbilityType.Speedster
    };

    private Dictionary<AbilityType, UIAbility> _uiAbilitiesByAbilityTypes;
    private Dictionary<AbilityType, Vector2> _layout;

    private LocalPlayerAccess _cachedAccess;
    private QuantumDemoInputTopDownMobile _mobile;
    private bool _built;

    private static readonly Vector2 POS_UTILITY = new Vector2(-765f, -176f);
    private static readonly Vector2 POS_MAIN2 = new Vector2(-550f, 0f);
    private static readonly Vector2 POS_MAIN1 = new Vector2(-285f, 100f);
    private static readonly Vector2 POS_JUMP = new Vector2(-389, -179f);
    private bool _hadBallLastFrame = false;
    private void Awake()
    {
        _mobile = FindObjectOfType<QuantumDemoInputTopDownMobile>(true);
        var cg = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    public void Initialize(LocalPlayerAccess localPlayerAccess)
    {
        _cachedAccess = localPlayerAccess;
        StartCoroutine(WaitUntilReadyAndBuildOnce());
    }

    private IEnumerator WaitUntilReadyAndBuildOnce()
    {
        yield return new WaitUntil(() =>
        {
            if (_built) return true;
            var runner = QuantumRunner.Default;
            if (runner == null || runner.Game == null) return false;
            var frames = runner.Game.Frames;
            if (frames == null || frames.Predicted == null) return false;
            return _cachedAccess != null && _cachedAccess.LocalPlayer != null && _cachedAccess.LocalPlayer.EntityView != null;
        });

        if (!_built) TryBuildUI();
    }

    private void TryBuildUI()
    {
        var runner = QuantumRunner.Default;
        if (runner == null || runner.Game == null || runner.Game.Frames?.Predicted == null) return;
        if (_cachedAccess?.LocalPlayer?.EntityView == null) return;
        if (_built) return;

        BuildUIUnsafe();
        _built = true;
    }

    private static bool IsUtility(AbilityType t) =>
        t == AbilityType.Invisibility || t == AbilityType.Speedster || t == AbilityType.Dash || t == AbilityType.Banana;

    private unsafe AbilityType FindSingleWiredUtility(AbilityInventory* inv)
    {
        AbilityType? found = null;
        int count = 0;
        for (int i = 0; i < inv->Abilities.Length; i++)
        {
            ref var a = ref inv->Abilities[i];
            if (!a.AbilityData.IsValid) continue;
            var t = a.AbilityType;
            if (IsUtility(t)) { count++; if (found == null) found = t; }
        }
#if UNITY_EDITOR || DEBUG
        if (count != 1) Debug.LogWarning($"[AbilityUI] Expected exactly one utility slot populated post-spawn, found {count}.");
#endif
        return found ?? AbilityType.Invisibility;
    }

    private static unsafe int FindSlotIndex(AbilityInventory* inv, AbilityType t)
    {
        for (int i = 0; i < inv->Abilities.Length; i++)
            if (inv->Abilities[i].AbilityData.IsValid && inv->Abilities[i].AbilityType == t)
                return i;
        return -1;
    }

    private static readonly AbilityType[] kMainCandidates = {
        AbilityType.Attack, AbilityType.Block, AbilityType.Bomb, AbilityType.Hook
    };

    private static bool IsMainCandidate(AbilityType t) =>
        t == AbilityType.Attack || t == AbilityType.Block || t == AbilityType.Bomb || t == AbilityType.Hook;

    private static unsafe void ResolveSelectedMainOwnersUI(
        Frame frame, PlayerRef player, AbilityInventory* inv,
        out AbilityType main1Owner, out AbilityType main2Owner)
    {
        AbilityType? first = null, second = null;
        for (int i = 0; i < inv->Abilities.Length; i++)
        {
            ref var a = ref inv->Abilities[i];
            if (!a.AbilityData.IsValid) continue;
            var t = a.AbilityType;
            if (!IsMainCandidate(t)) continue;
            if (first == null) first = t;
            else if (t != first.Value) { second = t; break; }
        }

        main1Owner = first ?? AbilityType.Attack;

        if (second != null)
            main2Owner = second.Value;
        else
        {
            bool hasBlock = false;
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                ref var a = ref inv->Abilities[i];
                if (!a.AbilityData.IsValid) continue;
                if (a.AbilityType == AbilityType.Block) { hasBlock = true; break; }
            }
            if (hasBlock && main1Owner != AbilityType.Block) main2Owner = AbilityType.Block;
            else
            {
                AbilityType fallback = main1Owner;
                for (int i = 0; i < inv->Abilities.Length; i++)
                {
                    ref var a = ref inv->Abilities[i];
                    if (!a.AbilityData.IsValid) continue;
                    var t = a.AbilityType;
                    if (IsMainCandidate(t) && t != main1Owner) { fallback = t; break; }
                }
                main2Owner = (fallback == main1Owner) ? AbilityType.Block : fallback;
            }
        }
    }

    private unsafe void BuildUIUnsafe()
    {
        if (_uiAbilitiesByAbilityTypes != null) return;

        Frame frame = QuantumRunner.Default.Game.Frames.Predicted;
        var entity = _cachedAccess.LocalPlayer.EntityView.EntityRef;

        PlayerStatus* ps = frame.Unsafe.GetPointer<PlayerStatus>(entity);
        AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(entity);
        if (ps == null || inv == null) return;

        AbilityType wiredUtilitySlot = FindSingleWiredUtility(inv);
        _mobile?.SetWiredUtility(wiredUtilitySlot);

        AbilityType main1Owner, main2Owner;
        ResolveSelectedMainOwnersUI(frame, ps->PlayerRef, inv, out main1Owner, out main2Owner);

        var wanted = new List<AbilityType>(6) {
            main1Owner, main2Owner,
            AbilityType.ThrowShort, AbilityType.ThrowLong,
            AbilityType.Jump, wiredUtilitySlot
        };

        _uiAbilitiesByAbilityTypes = new Dictionary<AbilityType, UIAbility>(wanted.Count);

        bool MustAlwaysShow(AbilityType t) =>
            t == AbilityType.Jump ||
            t == AbilityType.ThrowShort || t == AbilityType.ThrowLong ||
            t == main1Owner || t == main2Owner;

        foreach (var abilityType in wanted)
        {
            int idx = FindSlotIndex(inv, abilityType);
            if (idx < 0 || !inv->Abilities[idx].AbilityData.IsValid)
            {
                if (MustAlwaysShow(abilityType))
                    CreateFabricatedButton(abilityType, main1Owner, main2Owner, wiredUtilitySlot);
                continue;
            }

            var slot = inv->Abilities[idx];
            var asset = QuantumUnityDB.GetGlobalAsset<AbilityData>(slot.AbilityData.Id);
            if (asset == null || !asset.HasUIPrefab)
            {
                if (MustAlwaysShow(abilityType))
                    CreateFabricatedButton(abilityType, main1Owner, main2Owner, wiredUtilitySlot);
                continue;
            }

            var instance = Instantiate(asset.UIAbilityPrefab, transform, false);
            var uiAbility = instance.GetComponent<UIAbility>() ?? instance.AddComponent<UIAbility>();
            _uiAbilitiesByAbilityTypes[abilityType] = uiAbility;

            var btn = instance.GetComponentInChildren<UIButton>(true) ?? instance.AddComponent<UIButton>();
            var img = instance.GetComponentInChildren<Graphic>(true);
            if (img == null) img = instance.AddComponent<Image>();
            img.raycastTarget = true;

            btn.targetGraphic = img;
            btn.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            btn.onClick.RemoveAllListeners();

            uiAbility.AbilityKind = abilityType;

            BindTrigger(uiAbility, btn, abilityType, main1Owner, main2Owner, wiredUtilitySlot);
        }

        if (!_uiAbilitiesByAbilityTypes.ContainsKey(AbilityType.Jump))
            _uiAbilitiesByAbilityTypes[AbilityType.Jump] = CreateJumpFallbackButton();

        _layout = new Dictionary<AbilityType, Vector2>(8) {
            { AbilityType.Jump,       POS_JUMP  },
            { main1Owner,             POS_MAIN1 },
            { main2Owner,             POS_MAIN2 },
            { AbilityType.ThrowShort, POS_MAIN1 },
            { AbilityType.ThrowLong,  POS_MAIN2 },
            { wiredUtilitySlot,       POS_UTILITY }
        };

        ApplyPositionsFromLayout();

        if (_mobile)
        {
            _mobile.DisableLegacyUtilityButtonsPublic();

            bool haveM1 = _uiAbilitiesByAbilityTypes.ContainsKey(main1Owner);
            bool haveM2 = _uiAbilitiesByAbilityTypes.ContainsKey(main2Owner);
            bool haveJump = _uiAbilitiesByAbilityTypes.ContainsKey(AbilityType.Jump);

            if (haveM1 && haveM2)
                _mobile.DisableLegacyActionButtonsPublic();

            if (!haveJump && _mobile.jumpBtn)
            {
                _mobile.jumpBtn.onClick.RemoveAllListeners();
                _mobile.jumpBtn.onClick.AddListener(_mobile.PressJump);
                _mobile.jumpBtn.interactable = true;
                var img = _mobile.jumpBtn.GetComponent<Image>();
                if (img) img.raycastTarget = true;
                _mobile.jumpBtn.gameObject.SetActive(true);
            }
        }
    }

    private void BindTrigger(UIAbility uiAbility, UIButton btn, AbilityType abilityType,
                         AbilityType main1Owner, AbilityType main2Owner, AbilityType wiredUtilitySlot)
    {
        Func<QuantumDemoInputTopDownMobile> getMobile = () =>
            _mobile ? _mobile : (_mobile = FindObjectOfType<QuantumDemoInputTopDownMobile>(true));

        Action trig = null;

        if (abilityType == wiredUtilitySlot) trig = () => getMobile()?.PressUtilityExclusive(abilityType);
        else if (abilityType == main1Owner) trig = () => getMobile()?.PressFire();
        else if (abilityType == main2Owner) trig = () => getMobile()?.PressAltFire();
        else if (abilityType == AbilityType.ThrowShort) trig = () => getMobile()?.PressFire();
        else if (abilityType == AbilityType.ThrowLong) trig = () => getMobile()?.PressAltFire();
        else if (abilityType == AbilityType.Jump) trig = () => getMobile()?.PressJump();
        else if (abilityType == AbilityType.Dash) trig = () => getMobile()?.PressDash();
        else if (abilityType == AbilityType.Hook) trig = () => getMobile()?.PressHook();
        else if (abilityType == AbilityType.Invisibility) trig = () => getMobile()?.PressInvisibility();
        else if (abilityType == AbilityType.Speedster) trig = () => getMobile()?.PressSpeedster();
        else if (abilityType == AbilityType.Banana) trig = () => getMobile()?.PressBanana();
        else if (abilityType == AbilityType.Bomb) trig = () => getMobile()?.PressBomb();

        if (trig != null)
        {
            uiAbility.SetTrigger(trig);
        }

        btn.onClick.RemoveAllListeners();
        btn.interactable = false;
        btn.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
    }


    private void CreateFabricatedButton(AbilityType abilityType, AbilityType main1Owner, AbilityType main2Owner, AbilityType wiredUtilitySlot)
    {
        var go = new GameObject($"UIAbility_{abilityType}_Fabricated", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var img = go.AddComponent<Image>();
        img.color = new Color(1, 1, 1, 0.001f);
        img.raycastTarget = true;

        var btn = go.AddComponent<UIButton>();
        btn.targetGraphic = img;
        btn.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };

        var uiAbility = go.AddComponent<UIAbility>();
        uiAbility.AbilityKind = abilityType;
        _uiAbilitiesByAbilityTypes[abilityType] = uiAbility;

        BindTrigger(uiAbility, btn, abilityType, main1Owner, main2Owner, wiredUtilitySlot);
    }

    private UIAbility CreateJumpFallbackButton()
    {
        var go = new GameObject("UIAbility_Jump_Fallback", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = POS_JUMP;

        var img = go.AddComponent<Image>();
        img.color = new Color(1, 1, 1, 0.001f);
        img.raycastTarget = true;

        var btn = go.AddComponent<UIButton>();
        btn.targetGraphic = img;

        var uiAbility = go.AddComponent<UIAbility>();
        uiAbility.AbilityKind = AbilityType.Jump;
        _uiAbilitiesByAbilityTypes[AbilityType.Jump] = uiAbility;

        BindTrigger(uiAbility, btn, AbilityType.Jump, AbilityType.Attack, AbilityType.Block, AbilityType.Invisibility);
        return uiAbility;
    }

    private void ApplyPositionsFromLayout()
    {
        var parent = (RectTransform)transform;
        parent.anchorMin = parent.anchorMax = new Vector2(1f, 0f);
        parent.pivot = new Vector2(1f, 0f);

        if (_uiAbilitiesByAbilityTypes == null || _layout == null) return;

        foreach (var kv in _uiAbilitiesByAbilityTypes)
        {
            var rt = (RectTransform)kv.Value.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            if (_layout.TryGetValue(kv.Key, out var pos))
                rt.anchoredPosition = pos;
        }
    }

    public unsafe void UpdateAbilities(Frame frame,
                                       PlayerViewController player,
                                       PlayerStatus* playerStatus,
                                       AbilityInventory* abilityInventory)
    {
        UpdateAbilitiesUnsafe(frame, player, playerStatus, abilityInventory);
    }

    public unsafe void UpdateAbilitiesUnsafe(Frame frame, PlayerViewController player, PlayerStatus* ps, AbilityInventory* inv)
    {
        QuantumDemoInputTopDown input = *frame.GetPlayerInput(player.PlayerRef);
        bool hasBall = ps->IsHoldingBall && !inv->IsThrowingBall;

        if (hasBall && !_hadBallLastFrame && _uiAbilitiesByAbilityTypes != null)
        {
            foreach (var kv in _uiAbilitiesByAbilityTypes)
            {
                var abilityType = kv.Key;
                if (IsMainCandidate(abilityType))
                {
                    kv.Value.CancelOwnAimIfActive();
                }
            }
        }

        AbilityType main1Owner, main2Owner;
        ResolveSelectedMainOwnersUI(frame, ps->PlayerRef, inv, out main1Owner, out main2Owner);
        if (hasBall) { main1Owner = AbilityType.ThrowShort; main2Owner = AbilityType.ThrowLong; }

        bool firePressed = input.Fire.WasPressed;
        bool altPressed = input.AltFire.WasPressed;

        if (_uiAbilitiesByAbilityTypes == null) { _hadBallLastFrame = hasBall; return; }

        foreach (var kv in _uiAbilitiesByAbilityTypes)
        {
            var abilityType = kv.Key;
            var uiAbility = kv.Value;

            int idx = FindSlotIndex(inv, abilityType);
            bool isFabricated = idx < 0;

            Ability abilityRefOrDummy = default;
            if (!isFabricated)
            {
                ref var ability = ref inv->Abilities[idx];
                abilityRefOrDummy = ability;
            }

            bool visible;
            if (abilityType == AbilityType.ThrowShort || abilityType == AbilityType.ThrowLong)
                visible = hasBall;
            else if (IsMainCandidate(abilityType))
                visible = !hasBall;
            else if (abilityType == AbilityType.Jump || abilityType == AbilityType.Hook || IsUtility(abilityType))
                visible = true;
            else
                visible = false;

            uiAbility.ToggleVisibility(visible);

            bool pressed = false;
            if (visible)
            {
                if (!hasBall)
                {
                    if (abilityType == main1Owner) pressed = firePressed;
                    else if (abilityType == main2Owner) pressed = altPressed;
                    else if (abilityType == AbilityType.Jump || abilityType == AbilityType.Hook || IsUtility(abilityType))
                        pressed = input.GetAbilityInputWasPressed(abilityType);
                }
                else
                {
                    if (abilityType == AbilityType.ThrowShort) pressed = firePressed;
                    else if (abilityType == AbilityType.ThrowLong) pressed = altPressed;
                    else if (abilityType == AbilityType.Jump || abilityType == AbilityType.Hook || IsUtility(abilityType))
                        pressed = input.GetAbilityInputWasPressed(abilityType);
                }
            }

            uiAbility.UpdateAbility(frame, in abilityRefOrDummy, pressed);
        }

        _hadBallLastFrame = hasBall;
    }

}
