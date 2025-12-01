namespace Quantum.Menu
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
#if QUANTUM_ENABLE_TEXTMESHPRO
    using InputField = TMPro.TMP_InputField;
#else
    using InputField = UnityEngine.UI.InputField;
#endif
    using UnityEngine;
    using UnityEngine.UI;
    using Photon.Client;
    using static Quantum.Menu.QuantumMenuConnectionBehaviourSDK;

    /// <summary>
    /// Party screen: create or join by session code.
    /// On create we now force the ASIA region by normalizing the fetched region list (asia at index 0),
    /// then encoding regionIndex=0 in the session code. On join we normalize first, then decode.
    /// </summary>
    public partial class QuantumMenuUIParty : QuantumMenuUIScreen
    {
        [InlineHelp, SerializeField] protected InputField _sessionCodeField;
        [InlineHelp, SerializeField] protected Button _createButton;
        [InlineHelp, SerializeField] protected Button _joinButton;
        [InlineHelp, SerializeField] protected Button _backButton;

        protected Task<List<QuantumMenuOnlineRegion>> _regionRequest;

        partial void AwakeUser();
        partial void InitUser();
        partial void ShowUser();
        partial void HideUser();

        public override void Awake()
        {
            base.Awake();
            AwakeUser();
        }

        public override void Init()
        {
            base.Init();
            InitUser();
        }

        public override void Show()
        {
            base.Show();
            SessionReset.ResetSessionStatics();

            var client = Connection?.Client;
            var me = client?.LocalPlayer;
            if (me != null)
                me.SetCustomProperties(new Photon.Client.PhotonHashtable { ["team"] = null });

            if (Config.CodeGenerator == null)
                Debug.LogError("Add a CodeGenerator to the QuantumMenuConfig");

            _sessionCodeField.SetTextWithoutNotify("".PadLeft(Config.CodeGenerator.Length, '-'));
            _sessionCodeField.characterLimit = Config.CodeGenerator.Length;

            if (_regionRequest == null || _regionRequest.IsFaulted)
                _regionRequest = Connection.RequestAvailableOnlineRegionsAsync(ConnectionArgs);

            ShowUser();
        }

        public override void Hide()
        {
            base.Hide();
            HideUser();
        }

        protected virtual async void OnCreateButtonPressed() => await ConnectAsync(true);
        protected virtual async void OnJoinButtonPressed() => await ConnectAsync(false);

        public virtual void OnBackButtonPressed()
        {
            Controller.Show<QuantumMenuUIMain>();
        }

        static void NormalizeRegionOrder(List<QuantumMenuOnlineRegion> regions)
        {
            if (regions == null || regions.Count == 0) return;
            int asiaIdx = regions.FindIndex(r => string.Equals(r.Code, "asia", StringComparison.OrdinalIgnoreCase));
            if (asiaIdx <= 0) return;
            var asia = regions[asiaIdx];
            regions.RemoveAt(asiaIdx);
            regions.Insert(0, asia);
        }

        static bool HasAsia(List<QuantumMenuOnlineRegion> regions)
        {
            return regions != null && regions.Any(r => string.Equals(r.Code, "asia", StringComparison.OrdinalIgnoreCase));
        }
        // ---------------------------------

        protected virtual async System.Threading.Tasks.Task ConnectAsync(bool creating)
        {
            var inputRegionCode = _sessionCodeField.text.ToUpper();
            if (!creating && !Config.CodeGenerator.IsValid(inputRegionCode))
            {
                await Controller.PopupAsync(
                    $"The session code '{inputRegionCode}' is not a valid session code. Please enter {Config.CodeGenerator.Length} characters or digits.",
                    "Invalid Session Code");
                return;
            }

            if (_regionRequest == null || _regionRequest.IsFaulted)
                _regionRequest = Connection.RequestAvailableOnlineRegionsAsync(ConnectionArgs);

            Controller.Show<QuantumMenuUILoading>();
            Controller.Get<QuantumMenuUILoading>().SetStatusText(creating ? "Creating Lobby..." : "Joining Lobby...");

            List<QuantumMenuOnlineRegion> regions = null;
            try { regions = await _regionRequest; } catch (Exception e) { Debug.LogException(e); }

            if (regions == null || regions.Count == 0)
            {
                await Controller.PopupAsync("Failed to fetch available regions.", "Connection Failed");
                Controller.Show<QuantumMenuUIParty>();
                return;
            }

            NormalizeRegionOrder(regions);

#if UNITY_EDITOR
            Debug.Log("[Regions fetched] " + string.Join(", ", regions.Select(r => r.Code)));
#endif

            int regionIndex = -1;

            if (creating)
            {
                if (!HasAsia(regions))
                {
                    await Controller.PopupAsync("ASIA region is not available right now.", "Connection Failed");
                    Controller.Show<QuantumMenuUIParty>();
                    return;
                }

                regionIndex = 0;
                ConnectionArgs.Region = regions[regionIndex].Code;   // "asia"
                ConnectionArgs.PreferredRegion = "asia";
                ConnectionArgs.Session = Config.CodeGenerator.EncodeRegion(
                                                    Config.CodeGenerator.Create(),
                                                    regionIndex);
            }
            else
            {
                regionIndex = Config.CodeGenerator.DecodeRegion(inputRegionCode);
                if (regionIndex < 0 || regionIndex >= regions.Count)
                {
                    await Controller.PopupAsync(
                        $"The session code '{inputRegionCode}' is not a valid session code (cannot decode the region).",
                        "Invalid Session Code");
                    return;
                }

                ConnectionArgs.Session = inputRegionCode;
                ConnectionArgs.Region = regions[regionIndex].Code;
                ConnectionArgs.PreferredRegion = "asia"; // optional hint
            }

            ConnectionArgs.Creating = creating;

#if UNITY_EDITOR
            Debug.Log($"[Connect] creating={creating} -> Region='{ConnectionArgs.Region}', Preferred='{ConnectionArgs.PreferredRegion}'");
#endif

            HoldLobbyGate.SuppressAutoStart = true;
            Controller.Get<QuantumMenuUILoading>().SetStatusText(creating ? "Creating Lobby..." : "Joining Lobby...");

            var result = await Connection.ConnectAsync(ConnectionArgs);
            if (!result.Success)
            {
                await Controller.HandleConnectionResult(result, Controller);
                return;
            }

            if (creating)
            {
                var client = Connection?.Client;
                var room = client?.CurrentRoom;
                var isMaster = client?.LocalPlayer?.IsMasterClient == true;

                if (room != null && isMaster)
                {
                    room.SetCustomProperties(new Photon.Client.PhotonHashtable
                    {
                        ["started"] = false,
                        ["ended"] = false,
                        ["slotOrder"] = null,
                        ["teamMap"] = null
                    });
                    client.LocalPlayer?.SetCustomProperties(new Photon.Client.PhotonHashtable { ["team"] = null });
                }
            }

            var c = Connection.Client;
            var r = c?.CurrentRoom;
            bool started = false, ended = false;
            if (r?.CustomProperties != null)
            {
                if (r.CustomProperties.TryGetValue("started", out var s) && s is bool sb) started = sb;
                if (r.CustomProperties.TryGetValue("ended", out var e) && e is bool eb) ended = eb;
            }

            if (!creating && ended)
            {
                await Controller.PopupAsync("Game has ended.", "Session Over");
                HoldLobbyGate.SuppressAutoStart = false;
                await Connection.DisconnectAsync(ConnectFailReason.UserRequest);
                Controller.Show<QuantumMenuUIParty>();
                return;
            }

            if (!creating && started)
            {
                var local = c?.LocalPlayer;
                if (r != null && local != null)
                {
                    int blue = 0, red = 0;
                    foreach (var p in r.Players.Values)
                    {
                        if (p.ActorNumber == local.ActorNumber) continue;
                        if (p.CustomProperties != null &&
                            p.CustomProperties.TryGetValue("team", out var val) &&
                            val is int ti)
                        {
                            if (ti == 0) blue++; else if (ti == 1) red++;
                        }
                    }

                    int desiredTeam = (blue > red) ? 1 : (red > blue) ? 0 : (local.ActorNumber % 2 == 1 ? 0 : 1);

                    int currentTeam = -1;
                    if (local.CustomProperties != null &&
                        local.CustomProperties.TryGetValue("team", out var myVal) &&
                        myVal is int ct) currentTeam = (ct == 1) ? 1 : 0;

                    if (currentTeam != desiredTeam)
                    {
                        Debug.Log($"[LateJoin] Rebalance: Actor={local.ActorNumber} {TeamName(currentTeam)} -> {TeamName(desiredTeam)} (Blue={blue}, Red={red})");
                        local.SetCustomProperties(new Photon.Client.PhotonHashtable { ["team"] = desiredTeam });

                        float t0 = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - t0 < 1.0f)
                        {
                            c?.Service();
                            if (local.CustomProperties.TryGetValue("team", out var seen) && seen is int seenTeam && seenTeam == desiredTeam)
                                break;
                            await System.Threading.Tasks.Task.Yield();
                        }
                    }
                    else
                    {
                        Debug.Log($"[LateJoin] Already on balanced team: Actor={local.ActorNumber} {TeamName(currentTeam)} (Blue={blue}, Red={red})");
                    }
                }

                var sdk = Connection as QuantumMenuConnectionBehaviourSDK;
                if (sdk != null)
                {
                    var startRes = await sdk.StartGameFromLobbyAsync();
                    HoldLobbyGate.SuppressAutoStart = false;
                    if (startRes.Success)
                    {
                        Controller.Show<QuantumMenuUIGameplay>();
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"Late-join autostart failed: {startRes.DebugMessage}");
                    }
                }
            }

            // 11) Show lobby
            Controller.Show<QuantumMenuUILobby>();

            static string TeamName(int t) => (t == 1) ? "Red(1)" : (t == 0) ? "Blue(0)" : "None";
        }

        /// <summary>
        /// Original helper kept (unused by creation now, but harmless if referenced).
        /// </summary>
        protected static int FindBestAvailableOnlineRegionIndex(List<QuantumMenuOnlineRegion> regions)
        {
            var lowestPing = int.MaxValue;
            var index = -1;
            for (int i = 0; regions != null && i < regions.Count; i++)
            {
                if (regions[i].Ping < lowestPing)
                {
                    lowestPing = regions[i].Ping;
                    index = i;
                }
            }
            return index;
        }
    }
}
