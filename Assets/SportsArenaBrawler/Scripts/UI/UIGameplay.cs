using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Client;
using Quantum;
using Quantum.Menu;
using TMPro;
using UnityEngine;

public unsafe class UIGameplay : MonoBehaviour
{
    private static readonly int ANIMATE_HASH = Animator.StringToHash("Animate");

    private const string PLAYER_SCORED_TEXT = "{0} SCORED!";
    private const string VICTORY_TEXT = "VICTORY!";
    private const string DEFEAT_TEXT = "DEFEAT!";
    private const string TIE_TEXT = "TIE!";

    [Header("Settings")]
    [SerializeField] private float _lowTimeLeftThreshold = 20f;

    [Header("Scoreboard (In-Game HUD)")]
    [SerializeField] private Animator _scoreboardAnimator;
    [SerializeField] private TextMeshProUGUI _gameTimerText;
    [SerializeField] private Animator _blueTeamScoreAnimator;
    [SerializeField] private TextMeshProUGUI _blueTeamScoreText;
    [SerializeField] private Animator _redTeamScoreAnimator;
    [SerializeField] private TextMeshProUGUI _redTeamScoreText;
    [SerializeField] private GameObject _lowTimeObject;

    [Header("Game Starting")]
    [SerializeField] private Animator _gameStartingAnimator;
    [SerializeField] private AudioClip _gameStartingFirstSound;
    [SerializeField] private AudioClip _gameStartingNormalSound;

    [Header("Goal")]
    [SerializeField] private Animator _goalAnimator;
    [SerializeField] private TextMeshProUGUI _goalText;
    [SerializeField] private AudioClip _goalSound;

    [Header("Game Over")]
    [SerializeField] private Animator _gameOverAnimator;
    [SerializeField] private TextMeshProUGUI _gameOverText;
    [SerializeField] private AudioClip _victorySound;
    [SerializeField] private AudioClip _defeatSound;
    [SerializeField] private GameObject _gameOverPanel;

    [Header("Game Over > TrueScoreboard")]
    [SerializeField] private Transform _blueTeamHolder;
    [SerializeField] private Transform _redTeamHolder;
    [SerializeField] private GameObject _rowNamePrefab;
    [SerializeField] private GameObject _clickAnywhereHint;

    [Header("Hierarchy")]
    [SerializeField] private AudioSource _audioSource;

    private LocalPlayerAccess _localPlayerAccess;

    [SerializeField] private UnityEngine.UI.Button _exitButton;
    private readonly System.Collections.Generic.Dictionary<int, int> _goalsByPlayerRef = new System.Collections.Generic.Dictionary<int, int>();
    [Header("Ability Prefabs Source (copied from Lobby)")]
    [SerializeField] private List<Quantum.AbilityData> _abilities = new List<Quantum.AbilityData>();
    [SerializeField] private bool _scanResourcesIfListEmpty = true;

    [Header("Win Condition")]
    [SerializeField] private TextMeshProUGUI _raceToText;
    private void Awake()
    {
        QuantumEvent.Subscribe<EventOnGameStarting>(this, OnGameStarting);
        QuantumEvent.Subscribe<EventOnGameRunning>(this, OnGameRunning);
        QuantumEvent.Subscribe<EventOnGoalScored>(this, OnGoalScored);
        QuantumEvent.Subscribe<EventOnGameOver>(this, OnGameOver);
        // QuantumEvent.Subscribe<EventOnGameRestarted>(this, OnGameRestarted);

        Frame frame = QuantumRunner.Default?.Game?.Frames.Predicted;
        if (frame != null)
        {
            GameSettingsData gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);

            _blueTeamScoreText.text = frame.Global->TeamScore[0].ToString();
            _redTeamScoreText.text = frame.Global->TeamScore[1].ToString();

            SetGameTimerText(gameSettingsData.GameDuration.AsFloat);

            if (frame.Global->GameState != GameState.Initializing && frame.Global->GameState != GameState.Starting)
            {
                OnGameRunning(null);
            }
        }

        if (_gameOverPanel) _gameOverPanel.SetActive(false);
        if (_clickAnywhereHint) _clickAnywhereHint.SetActive(false);

        if (_scoreboardAnimator && _scoreboardAnimator.gameObject)
        {
            var go = _scoreboardAnimator.gameObject;
            var btn = go.GetComponent<UnityEngine.UI.Button>();
            if (btn == null)
                btn = go.AddComponent<UnityEngine.UI.Button>();

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnScoreboardClicked);
            btn.interactable = true;
        }
        RefreshRaceToText(QuantumRunner.Default?.Game?.Frames.Verified);
    }

    private bool _trueScoreboardOpen = false;
    private bool _isGameOver = false;
    private UnityEngine.UI.Button _trueScoreboardCloser;

    private void OnScoreboardClicked()
    {
        if (_trueScoreboardOpen) HideTrueScoreboard();
        else ShowTrueScoreboard();
    }

    private void ShowTrueScoreboard()
    {
        var frame = QuantumRunner.Default?.Game?.Frames.Verified;
        if (frame == null) return;

        int blue = frame.Global->TeamScore[0];
        int red = frame.Global->TeamScore[1];

        try { BuildEndScoreboard(frame, blue, red); }
        catch (System.Exception ex) { Debug.LogException(ex); }

        if (_gameOverPanel) _gameOverPanel.SetActive(true);

        if (!_isGameOver && _exitButton) _exitButton.gameObject.SetActive(false);

        if (_clickAnywhereHint) _clickAnywhereHint.SetActive(!_isGameOver);

        EnsureTrueScoreboardClickCloser(!_isGameOver);

        _trueScoreboardOpen = true;
    }


    private void HideTrueScoreboard()
    {
        if (_gameOverPanel) _gameOverPanel.SetActive(false);
        if (_clickAnywhereHint) _clickAnywhereHint.SetActive(false);
        EnsureTrueScoreboardClickCloser(false);
        _trueScoreboardOpen = false;
    }

    private void EnsureTrueScoreboardClickCloser(bool enable)
    {
        if (_gameOverPanel == null) return;

        if (enable)
        {
            if (_trueScoreboardCloser == null)
            {
                var img = _gameOverPanel.GetComponent<UnityEngine.UI.Image>();
                if (img == null) img = _gameOverPanel.AddComponent<UnityEngine.UI.Image>();
                img.raycastTarget = true;

                _trueScoreboardCloser = _gameOverPanel.GetComponent<UnityEngine.UI.Button>();
                if (_trueScoreboardCloser == null)
                    _trueScoreboardCloser = _gameOverPanel.AddComponent<UnityEngine.UI.Button>();

                _trueScoreboardCloser.transition = UnityEngine.UI.Selectable.Transition.None;
            }

            _trueScoreboardCloser.onClick.RemoveAllListeners();
            _trueScoreboardCloser.onClick.AddListener(() =>
            {
                if (!_isGameOver) HideTrueScoreboard();
            });

            var img2 = _trueScoreboardCloser.GetComponent<UnityEngine.UI.Image>();
            if (img2) img2.raycastTarget = true;

            _trueScoreboardCloser.gameObject.SetActive(true);
            _trueScoreboardCloser.interactable = true;
        }
        else
        {
            if (_trueScoreboardCloser != null)
            {
                _trueScoreboardCloser.onClick.RemoveAllListeners();
                _trueScoreboardCloser.interactable = false;
                var img = _trueScoreboardCloser.GetComponent<UnityEngine.UI.Image>();
                if (img) img.raycastTarget = false;
            }
        }
    }

    private void LateUpdate()
    {
        Frame frame = QuantumRunner.Default?.Game?.Frames.Verified;
        if (frame != null && frame.Global->GameState == GameState.Running)
        {
            float gameTimeLeft = frame.Global->MainGameTimer.TimeLeft.AsFloat;
            SetGameTimerText(gameTimeLeft);

            if (gameTimeLeft <= _lowTimeLeftThreshold && !_lowTimeObject.activeSelf)
            {
                _lowTimeObject.SetActive(true);
            }
        }
    }

    public void Initialize(LocalPlayerAccess localPlayerAccess)
    {
        _localPlayerAccess = localPlayerAccess;
    }

    private void OnGameStarting(EventOnGameStarting eventData)
    {
        if (eventData != null && eventData.IsFirst)
        {
            _goalsByPlayerRef.Clear();
        }

        _isGameOver = false;

        _gameStartingAnimator.SetTrigger(ANIMATE_HASH);

        if (eventData.IsFirst)
        {
            PlaySound(_gameStartingFirstSound);
        }
        else
        {
            PlaySound(_gameStartingNormalSound);
        }
        RefreshRaceToText(QuantumRunner.Default?.Game?.Frames.Verified);
    }

    private void OnGameRunning(EventOnGameRunning eventData)
    {
        if (!_scoreboardAnimator.gameObject.activeSelf)
        {
            _scoreboardAnimator.gameObject.SetActive(true);
            _scoreboardAnimator.SetTrigger(ANIMATE_HASH);
        }
        RefreshRaceToText(QuantumRunner.Default?.Game?.Frames.Verified);
    }

    private void OnGoalScored(EventOnGoalScored eventData)
    {
        Frame frame = eventData.Game.Frames.Verified;
        PlayerViewController player = PlayersManager.Instance.GetPlayer(eventData.PlayerEntityRef);

        _goalText.text = string.Format(PLAYER_SCORED_TEXT, NameUtils.CleanName(player.Nickname));
        _goalAnimator.SetTrigger(ANIMATE_HASH);

        _blueTeamScoreText.text = frame.Global->TeamScore[0].ToString();
        _redTeamScoreText.text = frame.Global->TeamScore[1].ToString();

        if (eventData.PlayerTeam == PlayerTeam.Blue)
        {
            _blueTeamScoreAnimator.SetTrigger(ANIMATE_HASH);
        }
        else
        {
            _redTeamScoreAnimator.SetTrigger(ANIMATE_HASH);
        }

        var ps = frame.Unsafe.GetPointer<PlayerStatus>(eventData.PlayerEntityRef);
        int pref = (int)ps->PlayerRef;
        if (_goalsByPlayerRef.TryGetValue(pref, out var current))
            _goalsByPlayerRef[pref] = current + 1;
        else
            _goalsByPlayerRef[pref] = 1;

        PlaySound(_goalSound);

        _lowTimeObject.SetActive(false);
        if (_trueScoreboardOpen && _gameOverPanel && _gameOverPanel.activeSelf)
        {
            var verified = QuantumRunner.Default?.Game?.Frames.Verified;
            if (verified != null)
            {
                int blue = verified.Global->TeamScore[0];
                int red = verified.Global->TeamScore[1];
                BuildEndScoreboard(verified, blue, red);
            }
        }

    }

    private void OnGameOver(EventOnGameOver eventData)
    {
        QuantumMenuConnectionBehaviourSDK.PreventRestart = true;
        _isGameOver = true;
        EnsureTrueScoreboardClickCloser(false);
        if (_clickAnywhereHint) _clickAnywhereHint.SetActive(false);
        var scoreboardButton = _scoreboardAnimator?.GetComponent<UnityEngine.UI.Button>();
        if (scoreboardButton != null)
        {
            scoreboardButton.interactable = false;
            Debug.Log("[UIGameplay] Scoreboard button disabled after GameOver.");
        }
        Frame frame = eventData.Game.Frames.Verified;
        var gameSettings = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);

        int blueTeamScore = frame.Global->TeamScore[0];
        int redTeamScore = frame.Global->TeamScore[1];

        _localPlayerAccess.PlayerInput.enabled = false;

        if (blueTeamScore == redTeamScore)
        {
            _gameOverText.text = TIE_TEXT;
            PlaySound(_defeatSound);
        }
        else if ((blueTeamScore > redTeamScore && _localPlayerAccess.LocalPlayer.PlayerTeam == PlayerTeam.Blue) ||
                 (redTeamScore > blueTeamScore && _localPlayerAccess.LocalPlayer.PlayerTeam == PlayerTeam.Red))
        {
            _gameOverText.text = VICTORY_TEXT;
            PlaySound(_victorySound);
        }
        else
        {
            _gameOverText.text = DEFEAT_TEXT;
            PlaySound(_defeatSound);
        }

        try
        {
            BuildEndScoreboard(frame, blueTeamScore, redTeamScore);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }

        if (_gameOverPanel) _gameOverPanel.SetActive(true);
        var panelImage = _gameOverPanel.GetComponent<UnityEngine.UI.Image>();

        panelImage.raycastTarget = true;
        _gameOverAnimator.SetTrigger(ANIMATE_HASH);
        _lowTimeObject.SetActive(false);
        HoldLobbyGate.SuppressAutoStart = true;

        try
        {
            var conn = FindObjectOfType<Quantum.Menu.QuantumMenuConnectionBehaviourSDK>();
            var client = conn?.Client;
            var room = client?.CurrentRoom;
            var me = client?.LocalPlayer;

            if (room != null && me != null && room.MasterClientId == me.ActorNumber)
            {
                var props = new PhotonHashtable
                {
                    ["started"] = false,
                    ["ended"] = true
                };
                room.SetCustomProperties(props);

                room.IsOpen = true;
                room.IsVisible = false;

                Debug.Log("[UIGameplay] Marked room ended (started=false, ended=true).");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        if (_exitButton != null)
        {
            _exitButton.gameObject.SetActive(true);
            _exitButton.onClick.RemoveAllListeners();
            _exitButton.onClick.AddListener(OnExitPressed);
        }
    }

    private void OnExitPressed()
    {
        if (_gameOverPanel) _gameOverPanel.SetActive(false);
        var panelImage = _gameOverPanel?.GetComponent<UnityEngine.UI.Image>();
        panelImage.raycastTarget = false;
        Quantum.Menu.QuantumMenuConnectionBehaviourSDK.PreventRestart = false;
        HoldLobbyGate.SuppressAutoStart = false;

        var sdk = FindObjectOfType<Quantum.Menu.QuantumMenuConnectionBehaviourSDK>();
        var client = sdk?.Client;
        var me = client?.LocalPlayer;
        if (me != null)
        {
            me.SetCustomProperties(new PhotonHashtable { ["team"] = null });
        }

        QuantumRunner.ShutdownAll();

        if (sdk != null)
        {
            _ = sdk.DisconnectAsync(ConnectFailReason.UserRequest);
        }

        var controller = FindObjectOfType<Quantum.Menu.QuantumMenuUIController>();
        if (controller != null)
        {
            controller.Show<Quantum.Menu.QuantumMenuUIMain>();
        }
    }

    private void OnGameRestarted(EventOnGameRestarted eventData)
    {
        _scoreboardAnimator.gameObject.SetActive(false);
        _blueTeamScoreText.text = "0";
        _redTeamScoreText.text = "0";
    }

    private void SetGameTimerText(float gameTimeLeft)
    {
        int minutes = Mathf.FloorToInt(gameTimeLeft / 60f);
        int seconds = Mathf.FloorToInt(gameTimeLeft % 60f);

        _gameTimerText.text = $"{minutes}:{seconds:00}";
    }

    private void PlaySound(AudioClip sound)
    {
        if (!_localPlayerAccess.IsMainLocalPlayer)
            return;

        _audioSource.PlayOneShot(sound);
    }

    private void BuildEndScoreboard(Frame frame, int blueTeamTotal, int redTeamTotal)
    {

        ClearContainer(_blueTeamHolder);
        ClearContainer(_redTeamHolder);

        var blue = new List<(int pref, string name, int goals)>();
        var red = new List<(int pref, string name, int goals)>();

        var it = frame.Filter<PlayerStatus>();
        int globalMax = 0;

        while (it.NextUnsafe(out var entity, out var ps))
        {
            var team = ps->PlayerTeam;
            int pref = (int)ps->PlayerRef;
            string name = GetPlayerNickname(frame, ps, entity);

            int goals = 0;
            _goalsByPlayerRef.TryGetValue(pref, out goals);

            if (goals > globalMax)
                globalMax = goals;

            if (team == PlayerTeam.Blue)
                blue.Add((pref, name, goals));
            else if (team == PlayerTeam.Red)
                red.Add((pref, name, goals));
        }

        List<int> topPrefs = new List<int>();
        foreach (var e in blue)
            if (e.goals == globalMax) topPrefs.Add(e.pref);
        foreach (var e in red)
            if (e.goals == globalMax) topPrefs.Add(e.pref);

        bool hasUniqueMvp = (_isGameOver && globalMax > 0 && topPrefs.Count == 1);
        int mvpPref = hasUniqueMvp ? topPrefs[0] : -1;

        blue.Sort((a, b) => a.goals != b.goals ? b.goals.CompareTo(a.goals) : string.Compare(a.name, b.name, StringComparison.Ordinal));
        red.Sort((a, b) => a.goals != b.goals ? b.goals.CompareTo(a.goals) : string.Compare(a.name, b.name, StringComparison.Ordinal));

        foreach (var p in blue)
            SpawnRow(_blueTeamHolder, p.name, p.goals, hasUniqueMvp && p.pref == mvpPref);

        foreach (var p in red)
            SpawnRow(_redTeamHolder, p.name, p.goals, hasUniqueMvp && p.pref == mvpPref);
    }

    private void SpawnRow(Transform parent, string playerName, int goals, bool isMvp)
    {
        if (!parent || !_rowNamePrefab) return;

        var go = Instantiate(_rowNamePrefab, parent);

        TextMeshProUGUI nameTMP = null, scoreTMP = null;
        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in tmps)
        {
            var n = t.gameObject.name.ToLowerInvariant();
            if (nameTMP == null && (n.Contains("name") || n == "text_name")) nameTMP = t;
            if (scoreTMP == null && (n.Contains("score") || n == "text_score")) scoreTMP = t;
        }
        if (nameTMP == null && tmps.Length > 0) nameTMP = tmps[0];
        if (scoreTMP == null && tmps.Length > 1) scoreTMP = tmps[1];

        var cleanName = NameUtils.CleanName(playerName);
        if (nameTMP) nameTMP.text = cleanName;
        if (scoreTMP) scoreTMP.text = goals.ToString();

        var hostIcon = go.transform.Find("IsPlayerHost");
        if (hostIcon) hostIcon.gameObject.SetActive(false);
        var kickBtn = go.transform.Find("KickButton");
        if (kickBtn) kickBtn.gameObject.SetActive(false);

        var scoreObj = go.transform.Find("Score");
        if (scoreObj) scoreObj.gameObject.SetActive(true);

        var mvpTf = go.transform.Find("IsMVP");
        if (mvpTf)
        {

            mvpTf.gameObject.SetActive(true);

            var mvpImages = mvpTf.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            foreach (var img in mvpImages)
            {
                img.enabled = isMvp;
                Debug.Log($"[MVP] → Image '{img.name}' enabled={img.enabled}");
            }

            var mvpTMPs = mvpTf.GetComponentsInChildren<TextMeshProUGUI>(true);

            foreach (var tmp in mvpTMPs)
            {
                tmp.enabled = isMvp;
            }

            mvpTf.localScale = Vector3.one;
        }

        PopulateAbilitySlotsFromPhoton(go, cleanName);

        try
        {
            var conn = FindObjectOfType<Quantum.Menu.QuantumMenuConnectionBehaviourSDK>();
            var localPlayer = conn?.Client?.LocalPlayer;
            if (localPlayer != null)
            {
                string myName = NameUtils.CleanName(localPlayer.NickName);
                if (string.Equals(myName, cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    if (nameTMP)
                    {
                        nameTMP.color = new Color(1f, 0.9f, 0.2f);
                        Debug.Log($"[Scoreboard] Highlighted local player '{myName}' in gold.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Scoreboard] Failed to color local name: {ex.Message}");
        }
    }

    private void PopulateAbilitySlotsFromPhoton(GameObject row, string playerName)
    {
        var abilitiesRoot = row.transform.Find("Abilities");
        if (!abilitiesRoot) return;

        var slot1 = abilitiesRoot.Find("AbilitySlot1");
        var slot2 = abilitiesRoot.Find("AbilitySlot2");
        var slot3 = abilitiesRoot.Find("AbilitySlot3");

        ClearContainer(slot1);
        ClearContainer(slot2);
        ClearContainer(slot3);

        var conn = FindObjectOfType<Quantum.Menu.QuantumMenuConnectionBehaviourSDK>();
        var room = conn?.Client?.CurrentRoom;
        if (room == null) return;

        var photonPlayer = room.Players.Values.FirstOrDefault(p =>
            NameUtils.CleanName(p.NickName).Equals(NameUtils.CleanName(playerName), StringComparison.OrdinalIgnoreCase));
        if (photonPlayer == null) return;

        var utilEnum = ReadAbilityEnum(photonPlayer, "AbilityPref.Utility.Enum", Quantum.AbilityType.Dash);
        var m1Enum = ReadAbilityEnum(photonPlayer, "AbilityPref.Main1.Enum", Quantum.AbilityType.Attack);
        var m2Enum = ReadAbilityEnum(photonPlayer, "AbilityPref.Main2.Enum", Quantum.AbilityType.Block);

        var all = GetAllAbilities();

        var utilData = ResolveFirstOfType(all, TypeFromAbilityEnum(utilEnum));
        var m1Data = ResolveFirstOfType(all, TypeFromAbilityEnum(m1Enum));
        var m2Data = ResolveFirstOfType(all, TypeFromAbilityEnum(m2Enum));

        if (slot1 && utilData != null) SpawnAbilityCard(utilData, slot1);
        if (slot2 && m1Data != null) SpawnAbilityCard(m1Data, slot2);
        if (slot3 && m2Data != null) SpawnAbilityCard(m2Data, slot3);
    }

    private List<Quantum.AbilityData> GetAllAbilities()
    {
        if (_abilities != null && _abilities.Count > 0) return _abilities;
        if (_scanResourcesIfListEmpty)
        {
            var found = Resources.LoadAll<Quantum.AbilityData>(string.Empty);
            if (found != null && found.Length > 0)
                return new List<Quantum.AbilityData>(found);
        }
        return new List<Quantum.AbilityData>();
    }

    private static Quantum.AbilityData ResolveFirstOfType(List<Quantum.AbilityData> all, Type t)
    {
        if (t == null) return null;
        return all.FirstOrDefault(a => a && a.GetType() == t);
    }

    private static Type TypeFromAbilityEnum(Quantum.AbilityType e)
    {
        if (e == Quantum.AbilityType.Attack) return typeof(Quantum.AttackAbilityData);
        if (e == Quantum.AbilityType.Block) return typeof(Quantum.BlockAbilityData);
        if (e == Quantum.AbilityType.Dash) return typeof(Quantum.DashAbilityData);
        if (e == Quantum.AbilityType.Hook) return typeof(Quantum.HookshotAbilityData);
        if (e == Quantum.AbilityType.Invisibility) return typeof(Quantum.InvisibilityAbilityData);
        if (e == Quantum.AbilityType.Speedster) return typeof(Quantum.SpeedsterAbilityData);
        if (e == Quantum.AbilityType.Banana) return typeof(Quantum.BananaAbilityData);
        if (e == Quantum.AbilityType.Bomb) return typeof(Quantum.BombAbilityData);
        if (e == Quantum.AbilityType.Jump) return typeof(Quantum.JumpAbilityData);
        return null;
    }

    private Quantum.AbilityType ReadAbilityEnum(Photon.Realtime.Player p, string key, Quantum.AbilityType fallback)
    {
        try
        {
            if (p?.CustomProperties != null &&
                p.CustomProperties.TryGetValue(key, out var v) &&
                v is string s && Enum.TryParse(s, out Quantum.AbilityType parsed))
                return parsed;
        }
        catch { }
        return fallback;
    }

    private void SpawnAbilityCard(Quantum.AbilityData data, Transform parent)
    {
        if (data == null || parent == null || !data.HasUIPrefab || data.UIAbilityPrefab == null)
            return;

        var go = Instantiate(data.UIAbilityPrefab, parent, false);

        go.SendMessage("SetPreviewMode", true, SendMessageOptions.DontRequireReceiver);

        foreach (var ability in go.GetComponentsInChildren<UIAbility>(true))
        {
            ability.SendMessage("SetPreviewMode", true, SendMessageOptions.DontRequireReceiver);

            var hidingField = typeof(UIAbility).GetField("_hidingMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var revealingField = typeof(UIAbility).GetField("_revealingMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var hidingMask = hidingField?.GetValue(ability) as UnityEngine.UI.Image;
            var revealingMask = revealingField?.GetValue(ability) as UnityEngine.UI.Image;

            if (revealingMask)
            {
                revealingMask.fillAmount = 1f;
                revealingMask.enabled = true;
            }

            if (hidingMask)
            {
                hidingMask.fillAmount = 0f;
                hidingMask.enabled = true;
            }
        }

        foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            g.raycastTarget = false;

        if (go.transform is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
        go.transform.localScale = Vector3.one * 0.7f;

        var nameTf = go.transform.Find("Keybinds/Name");
        if (nameTf) nameTf.gameObject.SetActive(false);
    }

    private IEnumerator DisableCooldownMaskNextFrame(GameObject go)
    {
        yield return null;
        foreach (var ability in go.GetComponentsInChildren<UIAbility>(true))
        {
            ability.SendMessage("SetPreviewMode", true, SendMessageOptions.DontRequireReceiver);

            var animator = ability.GetComponent<Animator>();
            if (animator) animator.enabled = false;

            var imgs = ability.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var img in imgs)
            {
                if (img.name.Contains("Cooldown", StringComparison.OrdinalIgnoreCase))
                    img.enabled = false;
            }
        }
    }

    private void ClearContainer(Transform holder)
    {
        if (!holder) return;
        for (int i = holder.childCount - 1; i >= 0; --i)
            Destroy(holder.GetChild(i).gameObject);
    }

    private void SpawnRow(Transform parent, string playerName, int goals)
    {
        if (!parent || !_rowNamePrefab) return;

        var go = Instantiate(_rowNamePrefab, parent);

        TextMeshProUGUI nameTMP = null, scoreTMP = null;
        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in tmps)
        {
            var n = t.gameObject.name.ToLowerInvariant();
            if (nameTMP == null && (n.Contains("name") || n == "text_name")) nameTMP = t;
            if (scoreTMP == null && (n.Contains("score") || n == "text_score")) scoreTMP = t;
        }
        if (nameTMP == null && tmps.Length > 0) nameTMP = tmps[0];
        if (scoreTMP == null && tmps.Length > 1) scoreTMP = tmps[1];

        if (nameTMP) nameTMP.text = playerName;
        if (scoreTMP) scoreTMP.text = goals.ToString();
    }

    private string GetPlayerNickname(Frame frame, PlayerStatus* ps, EntityRef playerEntity)
    {
        var pvc = PlayersManager.Instance?.GetPlayer(playerEntity);
        if (pvc != null && !string.IsNullOrEmpty(pvc.Nickname))
            return pvc.Nickname;

        return $"Player{(int)ps->PlayerRef}";
    }

    private void RefreshRaceToText(Frame f = null)
    {
        if (_raceToText == null) return;

        f ??= QuantumRunner.Default?.Game?.Frames.Verified;
        if (f == null) { _raceToText.gameObject.SetActive(false); return; }

        var settings = f.FindAsset<GameSettingsData>(f.RuntimeConfig.GameSettingsData.Id);

        int target = settings != null ? settings.ScoreToWin : 0;

        if (target > 0)
        {
            _raceToText.text = $"{target}";
            _raceToText.gameObject.SetActive(true);
        }
        else
        {
            _raceToText.gameObject.SetActive(false);
        }
    }

}
