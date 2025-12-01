using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UIButton = UnityEngine.UI.Button;
using System.Collections;

#if QUANTUM_ENABLE_TEXTMESHPRO
using InfoText = TMPro.TMP_Text;
#else
using InfoText = UnityEngine.UI.Text;
#endif

namespace Quantum.Menu
{
    public class QuantumMenuUIAbilitiesSelection : QuantumMenuUIScreen
    {
        [Header("UI - Lists")]
        [SerializeField] private Transform _mainAbilitiesHolder;
        [SerializeField] private Transform _utilityAbilitiesHolder;
        [InlineHelp, SerializeField] protected UIButton _backButton;
        [SerializeField] private GameObject _groupHeaderPrefab;

        [Header("UI - Active Abilities Holder")]
        [Tooltip("Parent with 3 border Images (Utility, Main1, Main2).")]
        [SerializeField] private Transform _activeAbilitiesHolder;
        [SerializeField] private Image _activeUtilityBorder;
        [SerializeField] private Image _activeMain1Border;
        [SerializeField] private Image _activeMain2Border;

        [Header("Selection Visuals")]
        [Tooltip("Sprite used for the tick/check mark on each selectable ability item.")]
        [SerializeField] private Sprite _tickSprite;
        [Tooltip("Size of the tick overlay on the card (in local pixels).")]
        [SerializeField] private Vector2 _tickSize = new Vector2(36, 36);
        [Tooltip("Where to place the tick on the card.")]
        [SerializeField] private TextAnchor _tickAnchor = TextAnchor.UpperRight;
        [Tooltip("Inset (px) from the chosen corner for the tick.")]
        [SerializeField] private Vector2 _tickInset = new Vector2(8, 8);

        [Header("Data (optional if using Resources)")]
        [SerializeField] private List<Quantum.AbilityData> _abilities = new List<Quantum.AbilityData>();

        [Header("Behavior")]
        [SerializeField] private bool _clearOnOpen = true;
        [SerializeField] private bool _scanResourcesIfListEmpty = true;

        [Header("Actions")]
        [SerializeField] private UIButton _saveButton;
        private bool _isDirty = false;

        private Image _saveBackgroundImage;
        private static readonly Color32 _saveDisabledBg = new Color32(0x68, 0x68, 0x68, 0xFF);

        // ───────────────────────── Info panel (Hold) ─────────────────────────
        [Header("Info Panel (Hold)")]
        [SerializeField] private GameObject _infoPanel;
        [SerializeField] private InfoText _infoDescription;
        [SerializeField] private InfoText _infoCooldown;
        [SerializeField] private InfoText _infoEffects;
        [SerializeField] private InfoText _infoDuration;
        [SerializeField] private RectTransform _infoAnchorRoot;
        [SerializeField] private float _holdSeconds = 0.35f;

        [Header("Hold Hint UI")]
        [SerializeField] private Image _holdArrowImage;
        [SerializeField] private InfoText _holdHintText;

        private const string PP_UTILITY = "AbilityPref.Utility";
        private const string PP_MAIN1 = "AbilityPref.Main1";
        private const string PP_MAIN2 = "AbilityPref.Main2";
        private const string PP_UTILITY_ENUM = "AbilityPref.Utility.Enum";
        private const string PP_MAIN1_ENUM = "AbilityPref.Main1.Enum";
        private const string PP_MAIN2_ENUM = "AbilityPref.Main2.Enum";
        private const string PP_IS_SET = "AbilityPref.IsSet";

        private static readonly Type DefaultUtilityType = typeof(Quantum.DashAbilityData);
        private static readonly Type DefaultMain1Type = typeof(Quantum.AttackAbilityData);
        private static readonly Type DefaultMain2Type = typeof(Quantum.BlockAbilityData);

        private enum AbilityGroup { Main = 0, Utility = 1, Unknown = 2 }

        private static readonly HashSet<Type> _compulsoryTypes = new() {
            typeof(Quantum.JumpAbilityData),
            typeof(Quantum.ThrowBallAbilityData),
        };

        private readonly Dictionary<Quantum.AbilityData, Toggle> _abilityToggle = new();
        private Quantum.AbilityData _selectedUtility;
        private readonly List<Quantum.AbilityData> _selectedMains = new(2);
        private GameObject _slotUtilityGO, _slotMain1GO, _slotMain2GO;

        private bool _suppressToggleCallbacks = false;
        private void SetHoldHintsVisible(bool visible)
        {
            if (_holdArrowImage) _holdArrowImage.gameObject.SetActive(visible);
            if (_holdHintText) _holdHintText.gameObject.SetActive(visible);
        }
        public override void Show()
        {
            base.Show();

            if (_backButton)
            {
                _backButton.onClick.RemoveListener(OnBackButtonPressed);
                _backButton.onClick.AddListener(OnBackButtonPressed);
            }

            if (_saveButton)
            {
                _saveButton.onClick.RemoveListener(OnSavePressed);
                _saveButton.onClick.AddListener(OnSavePressed);
                _saveButton.interactable = false;
                ApplySaveBackgroundColor(false);
            }

            HideAbilityInfo();
            SetHoldHintsVisible(true);
            Build();
        }

        public override void Hide()
        {
            base.Hide();
            if (_backButton) _backButton.onClick.RemoveListener(OnBackButtonPressed);
            if (_saveButton) _saveButton.onClick.RemoveListener(OnSavePressed);
            HideAbilityInfo();
        }

        private void OnSavePressed()
        {
            var all = GetAbilities();
            if (all == null || all.Count == 0)
            {
                Debug.LogWarning("[AbilitiesSelection] Save pressed but no abilities were found.");
                return;
            }

            var util = _selectedUtility
                       ?? ResolveFirstOfType(all, DefaultUtilityType) 
                       ?? ResolveFirstDifferent(all);

            var main1 = _selectedMains.Count > 0 ? _selectedMains[0] : null;
            var main2 = _selectedMains.Count > 1 ? _selectedMains[1] : null;

            if (main1 == null)
            {
                main1 = ResolveFirstOfType(all, DefaultMain1Type)
                        ?? ResolveFirstDifferent(all, util, main2);
            }

            if (main2 == null)
            {
                main2 = ResolveFirstOfType(all, DefaultMain2Type)
                        ?? ResolveFirstDifferent(all, util, main1);
            }

            if (main1 != null && main2 != null && main1.GetType() == main2.GetType())
            {
                var desiredForMain2 = (main1.GetType() == DefaultMain1Type) ? DefaultMain2Type : DefaultMain1Type;
                var alt = ResolveFirstOfType(all, desiredForMain2) ?? ResolveFirstDifferent(all, util, main1);
                if (alt != null) main2 = alt;
            }

            if (util == null || main1 == null || main2 == null)
            {
                Debug.LogError("[AbilitiesSelection] Could not resolve defaults; save aborted.");
                return;
            }

            PlayerPrefs.SetString(PP_UTILITY, util.GetType().FullName);
            PlayerPrefs.SetString(PP_MAIN1, main1.GetType().FullName);
            PlayerPrefs.SetString(PP_MAIN2, main2.GetType().FullName);

            var utilEnum = TypeToAbilityEnum(util.GetType());
            var m1Enum = TypeToAbilityEnum(main1.GetType());
            var m2Enum = TypeToAbilityEnum(main2.GetType());
            if (m2Enum == m1Enum)
            {
                m2Enum = Quantum.AbilityType.Block;
            }
            PlayerPrefs.SetString(PP_UTILITY_ENUM, utilEnum.ToString());
            PlayerPrefs.SetString(PP_MAIN1_ENUM, m1Enum.ToString());
            PlayerPrefs.SetString(PP_MAIN2_ENUM, m2Enum.ToString());

            PlayerPrefs.SetInt(PP_IS_SET, 1);
            PlayerPrefs.Save();

            SetSaveStateDirty(false);

            var sdk = Controller?.Connection as Quantum.Menu.QuantumMenuConnectionBehaviourSDK;
            if (sdk != null)
            {
                typeof(Quantum.Menu.QuantumMenuConnectionBehaviourSDK)
                    .GetMethod("PublishMyAbilitiesToLobby", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
                    .Invoke(sdk, null);
            }
        }

        private static Quantum.AbilityData ResolveByTypeFullName(List<Quantum.AbilityData> all, string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            return all.FirstOrDefault(a => a != null && a.GetType().FullName == fullName);
        }

        private static Quantum.AbilityData ResolveFirstOfType(List<Quantum.AbilityData> all, Type t)
        {
            return all.FirstOrDefault(a => a != null && a.GetType() == t);
        }

        private Quantum.AbilityData ResolveFirstDifferent(List<Quantum.AbilityData> all, params Quantum.AbilityData[] exclude)
        {
            var ex = new HashSet<Quantum.AbilityData>(exclude.Where(e => e != null));
            return all.FirstOrDefault(a =>
                a != null &&
                !_compulsoryTypes.Contains(a.GetType()) &&
                !ex.Contains(a)
            );
        }

        public virtual void OnBackButtonPressed() => Controller.Show<QuantumMenuUIMain>();

        public void Build()
        {
            if (_mainAbilitiesHolder == null)
            {
                Debug.LogError("[AbilitiesSelection] MainAbilitiesHolder is not assigned.");
                return;
            }

            if (_clearOnOpen)
            {
                ClearHolder(_mainAbilitiesHolder);
                if (_utilityAbilitiesHolder) ClearHolder(_utilityAbilitiesHolder);
                ClearActiveSlots();
            }

            var all = GetAbilities();
            if (all == null || all.Count == 0)
            {
                Debug.LogWarning("[AbilitiesSelection] No abilities found to display.");
                return;
            }

            var filtered = all.Where(a => a != null && !_compulsoryTypes.Contains(a.GetType()));
            var mains = filtered.Where(a => GetGroup(a) == AbilityGroup.Main).OrderBy(GetSortKey).ToList();
            var utils = filtered.Where(a => GetGroup(a) == AbilityGroup.Utility).OrderBy(GetSortKey).ToList();

            if (mains.Count > 0)
            {
                if (!_utilityAbilitiesHolder && _groupHeaderPrefab) TryAddHeader("Main Abilities", _mainAbilitiesHolder);
                foreach (var a in mains) InstantiateAbilityItem(a, _mainAbilitiesHolder);
            }

            if (utils.Count > 0)
            {
                var utilParent = _utilityAbilitiesHolder ? _utilityAbilitiesHolder : _mainAbilitiesHolder;
                if (!_utilityAbilitiesHolder && _groupHeaderPrefab) TryAddHeader("Utility Abilities", utilParent);
                foreach (var a in utils) InstantiateAbilityItem(a, utilParent);
            }

            ApplySavedOrDefaultSelection(all);
        }

        private void ApplySavedOrDefaultSelection(List<Quantum.AbilityData> all)
        {
            string utilType = PlayerPrefs.GetString(PP_UTILITY, DefaultUtilityType.FullName);
            string m1Type = PlayerPrefs.GetString(PP_MAIN1, DefaultMain1Type.FullName);
            string m2Type = PlayerPrefs.GetString(PP_MAIN2, DefaultMain2Type.FullName);

            var util = ResolveByTypeFullName(all, utilType) ?? ResolveFirstOfType(all, DefaultUtilityType);
            var m1 = ResolveByTypeFullName(all, m1Type) ?? ResolveFirstOfType(all, DefaultMain1Type);
            var m2 = ResolveByTypeFullName(all, m2Type) ?? ResolveFirstOfType(all, DefaultMain2Type);

            if (util == null) util = ResolveFirstDifferent(all, m1, m2);
            if (m1 == null) m1 = ResolveFirstDifferent(all, util, m2);
            if (m2 == null) m2 = ResolveFirstDifferent(all, util, m1);

            if (util == null && m1 == null && m2 == null) return;

            _suppressToggleCallbacks = true;

            if (_slotUtilityGO) { Destroy(_slotUtilityGO); _slotUtilityGO = null; }
            if (_slotMain1GO) { Destroy(_slotMain1GO); _slotMain1GO = null; }
            if (_slotMain2GO) { Destroy(_slotMain2GO); _slotMain2GO = null; }
            foreach (var kv in _abilityToggle) kv.Value.isOn = false;

            _selectedUtility = null;
            _selectedMains.Clear();

            if (util != null)
            {
                _selectedUtility = util;
                if (_activeUtilityBorder) _slotUtilityGO = SpawnSlotVisual(util, _activeUtilityBorder.transform);
                if (_abilityToggle.TryGetValue(util, out var tUtil)) tUtil.isOn = true;
            }

            if (m1 != null) _selectedMains.Add(m1);
            if (m2 != null && m2 != m1) _selectedMains.Add(m2);
            RefreshMainSlots();
            if (m1 != null && _abilityToggle.TryGetValue(m1, out var t1)) t1.isOn = true;
            if (m2 != null && _abilityToggle.TryGetValue(m2, out var t2)) t2.isOn = true;

            _suppressToggleCallbacks = false;

            SetSaveStateDirty(false);
        }


        private List<Quantum.AbilityData> GetAbilities()
        {
            if (_abilities != null && _abilities.Count > 0) return _abilities;
            if (_scanResourcesIfListEmpty)
            {
                var found = Resources.LoadAll<Quantum.AbilityData>(string.Empty);
                if (found != null && found.Length > 0) return new List<Quantum.AbilityData>(found);
            }
            return new List<Quantum.AbilityData>();
        }

        private static void ClearHolder(Transform holder)
        {
            for (int i = holder.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(holder.GetChild(i).gameObject);
        }

        private void ClearActiveSlots()
        {
            if (_slotUtilityGO) Destroy(_slotUtilityGO); _slotUtilityGO = null;
            if (_slotMain1GO) Destroy(_slotMain1GO); _slotMain1GO = null;
            if (_slotMain2GO) Destroy(_slotMain2GO); _slotMain2GO = null;
            _selectedUtility = null;
            _selectedMains.Clear();
            _abilityToggle.Clear();
        }

        private void InstantiateAbilityItem(Quantum.AbilityData ability, Transform parent)
        {
            if (ability == null || !ability.HasUIPrefab || ability.UIAbilityPrefab == null)
                return;

            var go = UnityEngine.Object.Instantiate(ability.UIAbilityPrefab, parent, false);

            var binder = go.GetComponentInChildren<IAbilityUIBinder>(true);
            if (binder != null) binder.Bind(ability);

            var ctx = go.GetComponent<AbilityUIContext>();
            if (ctx == null) ctx = go.AddComponent<AbilityUIContext>();
            ctx.Data = ability;

            go.SendMessage("SetPreviewMode", true, SendMessageOptions.DontRequireReceiver);

            var toggle = EnsureToggle(go);
            _abilityToggle[ability] = toggle;

            toggle.onValueChanged.AddListener(isOn => OnAbilityToggled(ability, toggle, isOn));
        }

        private void CacheSaveBackgroundImage()
        {
            if (_saveButton == null || _saveBackgroundImage != null) return;
            var bgTf = _saveButton.transform.Find("Background");
            if (bgTf) _saveBackgroundImage = bgTf.GetComponent<Image>();
        }

        private void ApplySaveBackgroundColor(bool interactable)
        {
            CacheSaveBackgroundImage();
            if (_saveBackgroundImage)
            {
                _saveBackgroundImage.color = interactable ? new Color32(0x52, 0xCC, 0x8F, 0xFF) : _saveDisabledBg;
            }
        }

        private Toggle EnsureToggle(GameObject cardRoot)
        {
            var toggle = cardRoot.GetComponent<Toggle>();
            if (!toggle) toggle = cardRoot.AddComponent<Toggle>();

            toggle.transition = Selectable.Transition.None;
#if UNITY_2022_1_OR_NEWER
            var nav = UnityEngine.UI.Navigation.defaultNavigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            toggle.navigation = nav;
#else
            toggle.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
#endif
            toggle.targetGraphic = null;

            var hitbox = cardRoot.transform.Find("Hitbox") as RectTransform;
            if (hitbox == null)
            {
                var go = new GameObject("Hitbox", typeof(RectTransform), typeof(Image));
                hitbox = (RectTransform)go.transform;
                hitbox.SetParent(cardRoot.transform, false);
                hitbox.SetAsFirstSibling();
            }
            hitbox.anchorMin = Vector2.zero;
            hitbox.anchorMax = Vector2.one;
            hitbox.pivot = new Vector2(0.5f, 0.5f);
            hitbox.anchoredPosition = Vector2.zero;
            hitbox.sizeDelta = Vector2.zero;

            var hitboxImg = hitbox.GetComponent<Image>();
            hitboxImg.color = new Color(1, 1, 1, 0.001f);
            hitboxImg.raycastTarget = true;

            toggle.targetGraphic = hitboxImg;

            var graphics = cardRoot.GetComponentsInChildren<Graphic>(true);
            foreach (var g in graphics)
                if (g != hitboxImg)
                    g.raycastTarget = false;

            var tickGO = cardRoot.transform.Find("Tick") as RectTransform;
            if (tickGO == null)
            {
                var go = new GameObject("Tick", typeof(RectTransform), typeof(Image));
                tickGO = (RectTransform)go.transform;
                tickGO.SetParent(cardRoot.transform, false);
            }
            tickGO.sizeDelta = _tickSize;
            SetRectAnchor(tickGO, _tickAnchor, _tickInset);

            var tickImg = tickGO.GetComponent<Image>();
            tickImg.sprite = _tickSprite;
            tickImg.raycastTarget = false;

            toggle.graphic = tickImg;
            toggle.isOn = false;

            var ctx = cardRoot.GetComponent<AbilityUIContext>();
            var hold = cardRoot.GetComponent<HoldToInfo>();
            if (hold == null) hold = cardRoot.AddComponent<HoldToInfo>();
            hold.owner = this;
            hold.data = ctx ? ctx.Data : null;
            hold.toggle = toggle;
            hold.hitbox = hitbox;
            hold.holdSeconds = Mathf.Max(0.1f, _holdSeconds);

            return toggle;
        }

        private static void SetRectAnchor(RectTransform rt, TextAnchor anchor, Vector2 inset)
        {
            Vector2 pivot;
            switch (anchor)
            {
                case TextAnchor.UpperLeft: pivot = new Vector2(0, 1); break;
                case TextAnchor.UpperCenter: pivot = new Vector2(0.5f, 1); break;
                case TextAnchor.UpperRight: pivot = new Vector2(1, 1); break;
                case TextAnchor.MiddleLeft: pivot = new Vector2(0, 0.5f); break;
                case TextAnchor.MiddleCenter: pivot = new Vector2(0.5f, 0.5f); break;
                case TextAnchor.MiddleRight: pivot = new Vector2(1, 0.5f); break;
                case TextAnchor.LowerLeft: pivot = new Vector2(0, 0); break;
                case TextAnchor.LowerCenter: pivot = new Vector2(0.5f, 0); break;
                default: pivot = new Vector2(1, 0); break; // lower right
            }
            rt.anchorMin = rt.anchorMax = pivot;
            rt.pivot = pivot;
            rt.anchoredPosition = new Vector2((pivot.x == 0 ? inset.x : -inset.x), (pivot.y == 0 ? inset.y : -inset.y));
        }


        private void OnAbilityToggled(Quantum.AbilityData ability, Toggle toggle, bool isOn)
        {
            if (_suppressToggleCallbacks) return;

            var group = GetGroup(ability);
            if (!isOn)
            {
                RemoveFromActive(ability, group);
                return;
            }

            switch (group)
            {
                case AbilityGroup.Utility: SelectUtility(ability, toggle); break;
                case AbilityGroup.Main: SelectMain(ability, toggle); break;
                default: toggle.isOn = false; break;
            }
        }

        private void SelectUtility(Quantum.AbilityData ability, Toggle toggle)
        {
            if (_selectedUtility != null && _abilityToggle.TryGetValue(_selectedUtility, out var prevT))
            {
                prevT.isOn = false;
            }
            _selectedUtility = ability;

            if (_activeUtilityBorder)
            {
                if (_slotUtilityGO) Destroy(_slotUtilityGO);
                _slotUtilityGO = SpawnSlotVisual(ability, _activeUtilityBorder.transform);
            }
            SetSaveStateDirty(true);
        }

        private void SelectMain(Quantum.AbilityData ability, Toggle toggle)
        {
            if (_selectedMains.Contains(ability)) return;

            // If we have 2 already, replace the oldest (index 0)
            if (_selectedMains.Count >= 2)
            {
                var toRemove = _selectedMains[0];
                _selectedMains.RemoveAt(0);
                if (_abilityToggle.TryGetValue(toRemove, out var t)) t.isOn = false;
            }

            _selectedMains.Add(ability);
            RefreshMainSlots();
            SetSaveStateDirty(true);
        }

        private void RemoveFromActive(Quantum.AbilityData ability, AbilityGroup group)
        {
            if (group == AbilityGroup.Utility)
            {
                if (_selectedUtility == ability)
                {
                    _selectedUtility = null;
                    if (_slotUtilityGO) { Destroy(_slotUtilityGO); _slotUtilityGO = null; }
                }
            }
            else if (group == AbilityGroup.Main)
            {
                if (_selectedMains.Remove(ability))
                {
                    RefreshMainSlots();
                }
            }
            SetSaveStateDirty(true);
        }

        private void RefreshMainSlots()
        {
            if (_slotMain1GO) { Destroy(_slotMain1GO); _slotMain1GO = null; }
            if (_slotMain2GO) { Destroy(_slotMain2GO); _slotMain2GO = null; }

            if (_selectedMains.Count > 0 && _activeMain1Border)
                _slotMain1GO = SpawnSlotVisual(_selectedMains[0], _activeMain1Border.transform);

            if (_selectedMains.Count > 1 && _activeMain2Border)
                _slotMain2GO = SpawnSlotVisual(_selectedMains[1], _activeMain2Border.transform);
        }

        private GameObject SpawnSlotVisual(Quantum.AbilityData ability, Transform parent)
        {
            if (ability == null || parent == null || !ability.HasUIPrefab || ability.UIAbilityPrefab == null)
                return null;

            var go = Instantiate(ability.UIAbilityPrefab, parent, false);
            go.SendMessage("SetPreviewMode", true, SendMessageOptions.DontRequireReceiver);

            var g = go.GetComponentInChildren<Graphic>(true);
            if (g) g.raycastTarget = false;

            var rt = go.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }

            return go;
        }

        private void TryAddHeader(string text, Transform parent)
        {
            if (!_groupHeaderPrefab || !parent) return;
            var go = Instantiate(_groupHeaderPrefab, parent, false);
#if QUANTUM_ENABLE_TEXTMESHPRO
            var tmp = go.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp) tmp.text = text;
#else
            var uText = go.GetComponentInChildren<Text>(true);
            if (uText) uText.text = text;
#endif
        }

        private static AbilityGroup GetGroup(Quantum.AbilityData data)
        {
            if (data == null) return AbilityGroup.Unknown;
            var e = TypeToAbilityEnum(data.GetType());

            // Mains (2 picks): Attack/Block + offensive
            switch (e)
            {
                case Quantum.AbilityType.Attack:
                case Quantum.AbilityType.Block:
                case Quantum.AbilityType.Bomb:
                case Quantum.AbilityType.Hook:
                    return AbilityGroup.Main;
            }

            // Utilities (1 pick): movement/stealth/boosts
            switch (e)
            {
                case Quantum.AbilityType.Dash:
                case Quantum.AbilityType.Invisibility:
                case Quantum.AbilityType.Speedster:
                case Quantum.AbilityType.Banana:
                    return AbilityGroup.Utility;
            }

            // Not selectable on this screen (throws/jump, etc.)
            return AbilityGroup.Unknown;
        }

        private static string GetSortKey(Quantum.AbilityData data)
        {
            return string.IsNullOrEmpty(data.name) ? data.GetType().Name : data.name;
        }

        private static Quantum.AbilityType TypeToAbilityEnum(Type t)
        {
            if (t == typeof(Quantum.AttackAbilityData)) return Quantum.AbilityType.Attack;
            if (t == typeof(Quantum.BlockAbilityData)) return Quantum.AbilityType.Block;
            if (t == typeof(Quantum.DashAbilityData)) return Quantum.AbilityType.Dash;
            if (t == typeof(Quantum.HookshotAbilityData)) return Quantum.AbilityType.Hook;
            if (t == typeof(Quantum.InvisibilityAbilityData)) return Quantum.AbilityType.Invisibility;
            if (t == typeof(Quantum.SpeedsterAbilityData)) return Quantum.AbilityType.Speedster;
            if (t == typeof(Quantum.BananaAbilityData)) return Quantum.AbilityType.Banana;
            if (t == typeof(Quantum.BombAbilityData)) return Quantum.AbilityType.Bomb;
            if (t == typeof(Quantum.JumpAbilityData)) return Quantum.AbilityType.Jump;

            return Quantum.AbilityType.Attack;
        }

        private void SetSaveStateDirty(bool dirty)
        {
            _isDirty = dirty;
            if (_saveButton)
            {
                _saveButton.interactable = dirty;
                ApplySaveBackgroundColor(dirty);
            }
        }


        private static string FPSeconds(Photon.Deterministic.FP fp)
        {
            float s = (float)fp.AsDouble;
            return (Mathf.Abs(s - Mathf.Round(s)) < 0.001f) ? Mathf.RoundToInt(s).ToString() : s.ToString("0.##");
        }

        private void MakeInfoPanelNonBlocking()
        {
            if (!_infoPanel) return;

            var cg = _infoPanel.GetComponent<CanvasGroup>();
            if (!cg) cg = _infoPanel.AddComponent<CanvasGroup>();

            cg.blocksRaycasts = false;
            cg.interactable = false;

            var gs = _infoPanel.GetComponentsInChildren<Graphic>(true);
            foreach (var g in gs) g.raycastTarget = false;
        }

        private void ShowAbilityInfo(Quantum.AbilityData data)
        {
            if (!_infoPanel || data == null) return;

            if (_infoDescription)
                _infoDescription.text = string.IsNullOrWhiteSpace(data.MenuShortDescription)
                    ? "No description."
                    : data.MenuShortDescription;

            if (_infoCooldown)
                _infoCooldown.text = $"Cooldown: {FPSeconds(data.Cooldown)}s";

            if (_infoDuration)
                _infoDuration.text = $"Duration: {FPSeconds(data.Duration)}s";

            if (_infoEffects)
                _infoEffects.text = string.IsNullOrWhiteSpace(data.MenuEffects)
                    ? "No effects"
                    : "Effects: " + data.MenuEffects;

            MakeInfoPanelNonBlocking();
            SetHoldHintsVisible(false);
            _infoPanel.SetActive(true);

        }

        private void HideAbilityInfo()
        {
            if (_infoPanel) _infoPanel.SetActive(false);
            SetHoldHintsVisible(true);
        }

        private class HoldToInfo : MonoBehaviour,
  IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
        {

            public QuantumMenuUIAbilitiesSelection owner;
            public Quantum.AbilityData data;
            public Toggle toggle;
            public RectTransform hitbox;
            public float holdSeconds = 0.35f;

            private bool _holding;
            private bool _fired;
            private float _downAt;
            private Coroutine _reenableCoro;

            public void OnPointerDown(PointerEventData eventData)
            {
                _downAt = Time.unscaledTime;
                _fired = false;
                _holding = true;

                if (_reenableCoro != null)
                {
                    StopCoroutine(_reenableCoro);
                    _reenableCoro = null;
                }
            }

            public void Update()
            {
                if (_holding && !_fired)
                {
                    if (Time.unscaledTime - _downAt >= holdSeconds)
                    {
                        _fired = true;

                        if (toggle)
                        {
                            toggle.enabled = false;
                            toggle.interactable = false;
                        }

                        if (owner) owner.ShowAbilityInfo(data);
                    }
                }
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _holding = false;
                if (_fired)
                {
                    eventData.Use();

                    if (owner) owner.HideAbilityInfo();
                    if (_reenableCoro != null) StopCoroutine(_reenableCoro);
                    _reenableCoro = StartCoroutine(ReenableToggleNextFrame());
                }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (_holding || _fired)
                {
                    if (owner) owner.HideAbilityInfo();
                }
                _holding = false;

                if (_fired)
                {
                    if (_reenableCoro != null) StopCoroutine(_reenableCoro);
                    _reenableCoro = StartCoroutine(ReenableToggleNextFrame());
                }
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_fired) eventData.Use();
            }

            private IEnumerator ReenableToggleNextFrame()
            {
                yield return null;

                if (toggle)
                {
                    bool wasOn = toggle.isOn;
                    toggle.enabled = true;
                    toggle.interactable = true;
                    toggle.isOn = wasOn;
                }

                _fired = false;
                _reenableCoro = null;
            }
        }

    }

    public class AbilityUIContext : MonoBehaviour
    {
        public Quantum.AbilityData Data;
    }

    public interface IAbilityUIBinder
    {
        void Bind(Quantum.AbilityData data);
    }
}
