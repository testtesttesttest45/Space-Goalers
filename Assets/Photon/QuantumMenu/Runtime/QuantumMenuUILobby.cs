using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using PhotonHashtable = Photon.Client.PhotonHashtable;
using UIButton = UnityEngine.UI.Button;

#if QUANTUM_ENABLE_TEXTMESHPRO
using Text = TMPro.TMP_Text;
#else
using Text = UnityEngine.UI.Text;
#endif

namespace Quantum.Menu
{
    public class QuantumMenuUILobby : QuantumMenuUIScreen
    {
        [Header("UI")]
        [SerializeField] private Text _lobbyCodeText;
        [SerializeField] private UIButton _startButton;
        [SerializeField] private UIButton _backButton;
        [SerializeField] private Transform _blueTeamHolder;
        [SerializeField] private Transform _redTeamHolder;
        [SerializeField] private GameObject _nameEntryPrefab;
        [SerializeField] private UIButton _swapButton;

        [Header("Ability Prefabs Source")]
        [Tooltip("Optional list. If empty and scan enabled, will load from Resources.")]
        [SerializeField] private List<Quantum.AbilityData> _abilities = new List<Quantum.AbilityData>();
        [SerializeField] private bool _scanResourcesIfListEmpty = true;

        private RealtimeClient Client => Connection.Client;

        private bool _startedLocally;
        private Coroutine _poll;

        private const string ROOM_KEY_STARTED = "started";
        private const string ROOM_KEY_TEAMMAP = "teamMap";
        private const string ROOM_KEY_SLOTORDER = "slotOrder";
        private const string ROOM_KEY_ENDED = "ended";

        private const string PLAYER_KEY_TEAM = "team"; // 0 = Blue, 1 = Red
        private const string PLAYER_KEY_KICK = "kick"; // host sets 1 on a player to kick them

        // Ability enum keys (same as Abilities Selection page)
        private const string PPROP_UTILITY_ENUM = "AbilityPref.Utility.Enum";
        private const string PPROP_MAIN1_ENUM = "AbilityPref.Main1.Enum";
        private const string PPROP_MAIN2_ENUM = "AbilityPref.Main2.Enum";

        private const float ABILITY_SLOT_SCALE = 0.7f;

        private System.Threading.CancellationTokenSource _startCts;

        private string _lastRosterSignature;

        [SerializeField] private Text _blueTeamPlayersText;
        [SerializeField] private Text _redTeamPlayersText;

        public override void Show()
        {
            base.Show();
            _startedLocally = false;

            if (_swapButton) _swapButton.onClick.AddListener(OnSwapPressed);
            if (_backButton) _backButton.onClick.AddListener(OnBackPressed);
            if (_startButton) _startButton.onClick.AddListener(OnStartPressed);

            if (_lobbyCodeText)
                _lobbyCodeText.text = Connection.SessionName;

            AssignInitialTeamIfUnset();

            if (IsRoomReadyToStart())
            {
                _ = StartGameIfNotStartedAsync();
                return;
            }

            StartCoroutine(WaitAndBuildLobbyUI());
            _poll = StartCoroutine(PollLoop());

            UpdateTeamCountLabels();
        }

        private IEnumerator WaitAndBuildLobbyUI()
        {
            yield return new WaitForSeconds(0.15f);
            yield return EnsureTeamAssignmentThenBuildUI();
        }

        public override void Hide()
        {
            base.Hide();
            _startedLocally = false;

            if (_swapButton) _swapButton.onClick.RemoveListener(OnSwapPressed);
            if (_backButton) _backButton.onClick.RemoveListener(OnBackPressed);
            if (_startButton) _startButton.onClick.RemoveListener(OnStartPressed);

            if (_poll != null)
            {
                StopCoroutine(_poll);
                _poll = null;
            }
        }

        private void OnSwapPressed()
        {
            if (Client?.LocalPlayer == null || Client.CurrentRoom == null) return;
            if (IsRoomReadyToStart()) return;

            var p = Client.LocalPlayer;
            int curr = ReadTeamProp(p);
            if (curr != 0 && curr != 1) curr = 0;
            int next = 1 - curr;

            p.SetCustomProperties(new PhotonHashtable { [PLAYER_KEY_TEAM] = next });
            StartCoroutine(WaitForLocalTeamProp(next, "AfterSwap"));

            RebuildRosterIfChanged(force: true);
            UpdateStartButtonState();

            UpdateTeamCountLabels();
        }

        private void AssignInitialTeamIfUnset()
        {
            var room = Client?.CurrentRoom;
            var lp = Client?.LocalPlayer;
            if (room == null || lp == null) return;

            if (lp.CustomProperties.TryGetValue(PLAYER_KEY_TEAM, out var v) && v is int i && (i == 0 || i == 1))
                return;

            int blue = 0, red = 0;
            foreach (var p in room.Players.Values)
            {
                if (p.CustomProperties.TryGetValue(PLAYER_KEY_TEAM, out var tv) && tv is int t)
                {
                    if (t == 0) blue++;
                    else if (t == 1) red++;
                }
            }

            int assigned = (blue <= red) ? 0 : 1;

            lp.CustomProperties[PLAYER_KEY_TEAM] = assigned;
            lp.SetCustomProperties(new PhotonHashtable { [PLAYER_KEY_TEAM] = assigned });
            Debug.Log($"[LobbyTeamAssign] Auto-assigned local player to {(assigned == 0 ? "Blue" : "Red")} team");

        }

        private IEnumerator EnsureTeamAssignmentThenBuildUI()
        {
            while (Client == null || Client.CurrentRoom == null || Client.LocalPlayer == null)
                yield return null;

            RebuildRosterIfChanged(force: true);
            UpdateStartButtonState();

            yield return null; // let late props arrive
            RebuildRosterIfChanged(force: true);
            UpdateStartButtonState();
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                if (Client == null || Client.CurrentRoom == null)
                    yield break;

                if (WasLocallyKicked())
                {
                    HandleLocalKick();
                    yield break;
                }

                if (IsRoomReadyToStart())
                {
                    _ = StartGameIfNotStartedAsync();
                    yield break;
                }

                RebuildRosterIfChanged();
                UpdateStartButtonState();

                UpdateTeamCountLabels();

                yield return wait;
            }
        }

        private bool WasLocallyKicked()
        {
            var lp = Client?.LocalPlayer;
            if (lp?.CustomProperties == null) return false;
            if (lp.CustomProperties.TryGetValue(PLAYER_KEY_KICK, out var v) && v is int i && i == 1)
                return true;
            return false;
        }

        private async void HandleLocalKick()
        {
            // Clear the kick flag so it doesn't trigger again
            Client?.LocalPlayer?.SetCustomProperties(new PhotonHashtable { [PLAYER_KEY_KICK] = null });

            // Disconnect immediately
            try { await Connection.DisconnectAsync(ConnectFailReason.UserRequest); } catch { }

            // Go back to party menu
            Controller.Show<QuantumMenuUIParty>();

            // Once on Party screen, show the popup there
            var popup = Controller.Get<QuantumMenuUIPopup>();
            if (popup != null)
            {
                popup.OpenPopup("You have been kicked from the lobby by the host.", "Kicked");
            }

            // Also update party screen status label for redundancy
            var party = Controller.Get<QuantumMenuUIParty>();
            if (party)
                party.gameObject.SendMessage("SetStatusText", "You have been kicked", SendMessageOptions.DontRequireReceiver);
        }

        private async System.Threading.Tasks.Task StartGameIfNotStartedAsync()
        {
            if (_startedLocally) return;

            _startedLocally = true;
            Controller.Show<QuantumMenuUILoading>();
            Controller.Get<QuantumMenuUILoading>().SetStatusText("Starting Game...");

            _startCts = new System.Threading.CancellationTokenSource();
            var sdk = Connection as QuantumMenuConnectionBehaviourSDK;
            if (sdk == null)
            {
                Debug.LogError("Connection is not SDK type; cannot launch game.");
                _startedLocally = false;
                return;
            }

            try
            {
                var res = await sdk.StartGameFromLobbyAsync();
                if (_startCts.IsCancellationRequested) { _startedLocally = false; return; }

                if (res.Success && Connection?.Client?.IsConnectedAndReady == true)
                {
                    HoldLobbyGate.SuppressAutoStart = false;
                    Controller.Show<QuantumMenuUIGameplay>();
                }
                else
                {
                    _startedLocally = false;
                    Controller.Show<QuantumMenuUIParty>();
                }
            }
            catch
            {
                _startedLocally = false;
                Controller.Show<QuantumMenuUIParty>();
            }
        }

        private async void OnBackPressed()
        {
            HoldLobbyGate.SuppressAutoStart = false;
            await Connection.DisconnectAsync(ConnectFailReason.UserRequest);
            Controller.Show<QuantumMenuUIParty>();
        }

        public void OnBackButtonPressed() => OnBackPressed();

        private bool IsHost()
        {
            if (Client?.CurrentRoom == null || Client.LocalPlayer == null) return false;
            return Client.CurrentRoom.MasterClientId == Client.LocalPlayer.ActorNumber;
        }

        private bool IsRoomReadyToStart()
        {
            var room = Client?.CurrentRoom;
            if (room == null) return false;

            bool started = false;
            if (room.CustomProperties != null &&
                room.CustomProperties.TryGetValue(ROOM_KEY_STARTED, out var v) &&
                v is bool b && b)
            {
                started = true;
            }
            return started && TryGetTeamMap(out _);
        }

        private void UpdateStartButtonState()
        {
            if (_startButton == null) return;
            if (Client?.CurrentRoom == null || Client.LocalPlayer == null) return;

            var label = _startButton.GetComponentInChildren<Text>(true);
            var background = _startButton.transform.Find("Background")?.GetComponent<Image>();
            if (!background) background = _startButton.GetComponent<Image>();

            bool isHost = IsHost();

            if (isHost)
            {
                if (label) label.text = "Start";
                _startButton.interactable = true;
                background.color = new Color(0.3f, 1f, 0.3f); // green
            }
            else
            {
                if (label) label.text = "Waiting for Host";
                _startButton.interactable = false;
                background.color = new Color(0.35f, 0.35f, 0.35f); // grey
            }
        }


        private async void OnStartPressed()
        {
            if (!IsHost() || Client?.CurrentRoom == null) return;

            Client.Service();
            var room = Client.CurrentRoom;

            var orderedPlayers = room.Players.Values.OrderBy(p => p.ActorNumber).ToArray();
            await WaitForEveryoneTeamPropAsync(orderedPlayers, 1.0f);

            var slotOrderActors = orderedPlayers.Select(p => p.ActorNumber).ToArray();
            string slotOrderStr = string.Join(",", slotOrderActors);

            var presentTeams = new int[orderedPlayers.Length];
            var sb = new StringBuilder();

            for (int slot = 0; slot < orderedPlayers.Length; slot++)
            {
                var p = orderedPlayers[slot];
                int team = ReadTeamProp(p);

                // if team not yet assigned (Photon prop hasn't arrived)
                if (team != 0 && team != 1)
                    team = -1; //  neutral / unassigned, prevents red flash

                Transform parent;
                if (team == 0) parent = _blueTeamHolder;
                else if (team == 1) parent = _redTeamHolder;
                else parent = _blueTeamHolder; // temporarily show in blue list (or make a “pending” group)


                presentTeams[slot] = team;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(p.ActorNumber).Append(':').Append(team);
            }

            string mapStr = sb.ToString();

            int capacity = (room.MaxPlayers > 0) ? room.MaxPlayers : Mathf.Max(Connection.MaxPlayerCount, 1);
            int blue = presentTeams.Count(x => x == 0);
            int red = presentTeams.Count(x => x == 1);
            var initialTeamBySlot = new int[capacity];

            for (int i = 0; i < capacity; i++)
            {
                if (i < presentTeams.Length) initialTeamBySlot[i] = presentTeams[i];
                else
                {
                    initialTeamBySlot[i] = (blue <= red) ? 0 : 1;
                    if (initialTeamBySlot[i] == 0) blue++; else red++;
                }
            }

            var props = new PhotonHashtable
            {
                [ROOM_KEY_SLOTORDER] = slotOrderStr,
                [ROOM_KEY_TEAMMAP] = mapStr,
                [ROOM_KEY_STARTED] = true
            };

            room.SetCustomProperties(props);
            room.IsOpen = true;
            room.IsVisible = false;

            if (Connection is QuantumMenuConnectionBehaviourSDK sdk)
            {
                var argsField = typeof(QuantumMenuConnectionBehaviourSDK)
                    .GetField("_lastConnectArgs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var connectArgs = (QuantumMenuConnectArgs)argsField?.GetValue(sdk);
                if (connectArgs?.RuntimeConfig != null)
                    connectArgs.RuntimeConfig.InitialTeamBySlot = (int[])initialTeamBySlot.Clone();
            }

            Controller.Show<QuantumMenuUILoading>();
            Controller.Get<QuantumMenuUILoading>().SetStatusText("Starting Game...");
            _ = StartGameIfNotStartedAsync();
        }


        private void WireKickButton(GameObject rowGO, Photon.Realtime.Player player)
        {
            var kickTf = rowGO.transform.Find("KickButton");
            if (!kickTf) return;

            bool show = IsHost() &&
                        Client?.LocalPlayer != null &&
                        player.ActorNumber != Client.LocalPlayer.ActorNumber;

            kickTf.gameObject.SetActive(show);

            var btn = kickTf.GetComponent<UnityEngine.UI.Button>();
            if (!btn) return;

            btn.onClick.RemoveAllListeners();
            if (show)
            {
                int actorId = player.ActorNumber;
                btn.onClick.AddListener(() => OnKickPressed(actorId));
            }
        }

        // When host presses kick, update labels immediately (we ignore kick-flagged players)
        private void OnKickPressed(int actorNumber)
        {
            if (!IsHost()) return;
            var room = Client?.CurrentRoom;
            if (room == null || !room.Players.TryGetValue(actorNumber, out var target)) return;

            target.SetCustomProperties(new PhotonHashtable { [PLAYER_KEY_KICK] = 1 });

            // NEW
            UpdateTeamCountLabels();
        }



        private string ComputeRosterSignature()
        {
            var room = Client?.CurrentRoom;
            if (room == null) return null;

            var sb = new StringBuilder();
            sb.Append("MC:").Append(room.MasterClientId).Append('|');

            var players = room.Players.Values.OrderBy(p => p.ActorNumber);
            foreach (var p in players)
            {
                int team = ReadTeamProp(p);
                if (team != 0 && team != 1)
                    team = (p.ActorNumber == room.MasterClientId) ? 0 : 1;

                var nick = string.IsNullOrEmpty(p.NickName) ? $"Player{p.ActorNumber:00}" : p.NickName;

                string util = ReadAbilityEnumString(p, PPROP_UTILITY_ENUM);
                string m1 = ReadAbilityEnumString(p, PPROP_MAIN1_ENUM);
                string m2 = ReadAbilityEnumString(p, PPROP_MAIN2_ENUM);

                sb.Append(p.ActorNumber).Append('|').Append(team).Append('|')
                  .Append(nick.Length).Append(':').Append(nick.GetHashCode()).Append('|')
                  .Append(util).Append(',').Append(m1).Append(',').Append(m2).Append(';');
            }
            return sb.ToString();
        }

        private void RebuildRosterIfChanged(bool force = false)
        {
            var sig = ComputeRosterSignature();
            if (!force && sig != null && sig == _lastRosterSignature)
                return;

            _lastRosterSignature = sig;
            RebuildRosterNow();

            // NEW
            UpdateTeamCountLabels();
        }


        private void RebuildRosterNow()
        {
            var room = Client?.CurrentRoom;
            if (room == null || _nameEntryPrefab == null || _blueTeamHolder == null || _redTeamHolder == null)
                return;

            // Clear holders
            for (int i = _blueTeamHolder.childCount - 1; i >= 0; --i)
                Destroy(_blueTeamHolder.GetChild(i).gameObject);
            for (int i = _redTeamHolder.childCount - 1; i >= 0; --i)
                Destroy(_redTeamHolder.GetChild(i).gameObject);

            var players = room.Players.Values.OrderBy(p => p.ActorNumber);
            foreach (var p in players)
            {
                int team = ReadTeamProp(p);
                if (team != 0 && team != 1)
                    team = (p.ActorNumber == room.MasterClientId) ? 0 : 1;

                var parent = team == 1 ? _redTeamHolder : _blueTeamHolder;
                var row = Instantiate(_nameEntryPrefab, parent);

                // Name only (prefer a dedicated "Name" child; else first Text)
                var nameTf = row.transform.Find("Name");
                Text nameText = nameTf ? nameTf.GetComponentInChildren<Text>(true) : row.GetComponentInChildren<Text>(true);
                if (nameText)
                {
                    var nick = string.IsNullOrEmpty(p.NickName) ? $"Player{p.ActorNumber:00}" : p.NickName;
                    nameText.text = NameUtils.CleanName(nick);

                    // ✅ Highlight the local player name in yellow
                    if (Client?.LocalPlayer != null && p.ActorNumber == Client.LocalPlayer.ActorNumber)
                    {
                        nameText.color = new Color(1f, 0.9f, 0.2f); // yellowish gold
                    }
                    else
                    {
                        nameText.color = Color.white;
                    }
                }


                // Hide any legacy inline label if present
                var inline = row.transform.Find("AbilitiesInline");
                if (inline) inline.gameObject.SetActive(false);

                // Host logo visible to everyone
                var crown = row.transform.Find("IsPlayerHost");
                if (crown) crown.gameObject.SetActive(p.ActorNumber == room.MasterClientId);

                // Spawn actual ability prefabs into slot frames (Utility→1, Main1→2, Main2→3)
                BuildAbilitySlotsWithPrefabs(row, p);

                // Host-only Kick buttons on others
                WireKickButton(row, p);
            }
        }

        private void BuildAbilitySlotsWithPrefabs(GameObject row, Photon.Realtime.Player p)
        {
            var abilitiesRoot = row.transform.Find("Abilities");
            if (!abilitiesRoot) return;

            var slot1 = abilitiesRoot.Find("AbilitySlot1"); // Utility
            var slot2 = abilitiesRoot.Find("AbilitySlot2"); // Main 1
            var slot3 = abilitiesRoot.Find("AbilitySlot3"); // Main 2

            if (slot1) Clear(slot1);
            if (slot2) Clear(slot2);
            if (slot3) Clear(slot3);

            var utilEnum = ReadAbilityEnumFromProps(p, PPROP_UTILITY_ENUM, Quantum.AbilityType.Dash);
            var m1Enum = ReadAbilityEnumFromProps(p, PPROP_MAIN1_ENUM, Quantum.AbilityType.Attack);
            var m2Enum = ReadAbilityEnumFromProps(p, PPROP_MAIN2_ENUM, Quantum.AbilityType.Block);

            var all = GetAllAbilities();

            var utilData = ResolveFirstOfType(all, TypeFromAbilityEnum(utilEnum));
            var m1Data = ResolveFirstOfType(all, TypeFromAbilityEnum(m1Enum));
            var m2Data = ResolveFirstOfType(all, TypeFromAbilityEnum(m2Enum));

            if (slot1 && utilData != null) SpawnAbilityCard(utilData, slot1);
            if (slot2 && m1Data != null) SpawnAbilityCard(m1Data, slot2);
            if (slot3 && m2Data != null) SpawnAbilityCard(m2Data, slot3);
        }

        private void SpawnAbilityCard(Quantum.AbilityData data, Transform parent)
        {
            if (data == null || !data.HasUIPrefab || data.UIAbilityPrefab == null || parent == null)
                return;

            var go = Instantiate(data.UIAbilityPrefab, parent, false);

            go.SendMessage("SetPreviewMode", true, SendMessageOptions.DontRequireReceiver);

            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;

            if (go.transform is RectTransform rt)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
            go.transform.localScale = Vector3.one * ABILITY_SLOT_SCALE;

            HideAbilityName(go);
        }

        private static void HideAbilityName(GameObject root)
        {
            if (root == null) return;

            var explicitTf = root.transform.Find("Keybinds/Name");
            if (explicitTf != null)
            {
                explicitTf.gameObject.SetActive(false);
                return;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t != null && t.name == "Name")
                {
                    t.gameObject.SetActive(false);
                }
            }

#if QUANTUM_ENABLE_TEXTMESHPRO
            var tmps = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
            foreach (var tmp in tmps)
                if (tmp != null && string.Equals(tmp.gameObject.name, "Name", StringComparison.Ordinal))
                    tmp.enabled = false;
#else
            var texts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            foreach (var txt in texts)
                if (txt != null && string.Equals(txt.gameObject.name, "Name", StringComparison.Ordinal))
                    txt.enabled = false;
#endif
        }

        private static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; --i)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }


        private List<Quantum.AbilityData> GetAllAbilities()
        {
            if (_abilities != null && _abilities.Count > 0) return _abilities;
            if (_scanResourcesIfListEmpty)
            {
                var found = Resources.LoadAll<Quantum.AbilityData>(string.Empty);
                if (found != null && found.Length > 0) return new List<Quantum.AbilityData>(found);
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

        private Quantum.AbilityType ReadAbilityEnumFromProps(Photon.Realtime.Player p, string key, Quantum.AbilityType fallback)
        {
            try
            {
                if (p?.CustomProperties != null &&
                    p.CustomProperties.TryGetValue(key, out var v) &&
                    v is string s && !string.IsNullOrEmpty(s) &&
                    Enum.TryParse<Quantum.AbilityType>(s, out var parsed))
                {
                    return parsed;
                }
            }
            catch { }
            return fallback;
        }

        private string ReadAbilityEnumString(Photon.Realtime.Player p, string key)
        {
            if (p?.CustomProperties != null &&
                p.CustomProperties.TryGetValue(key, out var v) &&
                v is string s) return s;
            return string.Empty;
        }


        private IEnumerator WaitForLocalTeamProp(int expected, string reason, float timeout = 2f)
        {
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < timeout)
            {
                Client?.Service();
                var curr = ReadTeamProp(Client?.LocalPlayer);
                if (curr == expected) yield break;
                yield return null;
            }
        }

        private int ReadTeamProp(Photon.Realtime.Player p)
        {
            if (p?.CustomProperties != null &&
                p.CustomProperties.TryGetValue(PLAYER_KEY_TEAM, out var v) &&
                v is int ti)
            {
                return (ti == 1) ? 1 : 0;
            }
            return -1;
        }

        private bool TryGetTeamMap(out string mapStr)
        {
            mapStr = null;
            var room = Client?.CurrentRoom;
            if (room?.CustomProperties != null &&
                room.CustomProperties.TryGetValue(ROOM_KEY_TEAMMAP, out var v) &&
                v is string s && !string.IsNullOrEmpty(s))
            {
                mapStr = s;
                return true;
            }
            return false;
        }

        public void CancelStartGame()
        {
            if (_startCts != null && !_startCts.IsCancellationRequested)
                _startCts.Cancel();
        }

        private async System.Threading.Tasks.Task WaitForEveryoneTeamPropAsync(Photon.Realtime.Player[] players, float timeoutSec = 1.0f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < timeoutSec)
            {
                Client?.Service();
                bool allSet = true;
                for (int i = 0; i < players.Length; i++)
                {
                    int team = ReadTeamProp(players[i]);
                    if (team != 0 && team != 1) { allSet = false; break; }
                }
                if (allSet) return;
                await System.Threading.Tasks.Task.Delay(30);
            }
        }

        private void UpdateTeamCountLabels()
        {
            int blue = 0, red = 0;

            var room = Client?.CurrentRoom;
            if (room != null)
            {
                foreach (var p in room.Players.Values)
                {
                    // ignore anyone flagged to be kicked (so counts update instantly)
                    if (p?.CustomProperties != null &&
                        p.CustomProperties.TryGetValue(PLAYER_KEY_KICK, out var kv) &&
                        kv is int ki && ki == 1)
                    {
                        continue;
                    }

                    int t = ReadTeamProp(p);
                    if (t == 0) blue++;
                    else if (t == 1) red++;
                }
            }

            if (_blueTeamPlayersText) _blueTeamPlayersText.text = blue.ToString();
            if (_redTeamPlayersText) _redTeamPlayersText.text = red.ToString();
        }

    }


}
