namespace Quantum.Menu
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
#if QUANTUM_ENABLE_TEXTMESHPRO
    using InputField = TMPro.TMP_InputField;
#else
  using InputField = UnityEngine.UI.InputField;
#endif
    using UnityEngine;
    using UnityEngine.UI;
    using Photon.Client;
    using System.Linq;
    using static Quantum.Menu.QuantumMenuConnectionBehaviourSDK;

    /// <summary>
    /// The party screen shows two modes. Creating a new game or joining a game with a party code.
    /// After creating a game the session party code can be obtained via the in-game menu.
    /// One specialty is that a region list is requested from the connection when entering the screen in order to create a matching session codes.
    /// </summary>
    public partial class QuantumMenuUIParty : QuantumMenuUIScreen
    {
        /// <summary>
        /// The session code input field.
        /// </summary>
        [InlineHelp, SerializeField] protected InputField _sessionCodeField;
        /// <summary>
        /// The create game button.
        /// </summary>
        [InlineHelp, SerializeField] protected Button _createButton;
        /// <summary>
        /// The join game button.
        /// </summary>
        [InlineHelp, SerializeField] protected Button _joinButton;
        /// <summary>
        /// The back button.
        /// </summary>
        [InlineHelp, SerializeField] protected Button _backButton;

        /// <summary>
        /// The task of requesting the regions.
        /// </summary>
        protected Task<List<QuantumMenuOnlineRegion>> _regionRequest;

        partial void AwakeUser();
        partial void InitUser();
        partial void ShowUser();
        partial void HideUser();

        /// <summary>
        /// The Unity awake method. Calls partial method <see cref="AwakeUser"/> to be implemented on the SDK side.
        /// </summary>
        public override void Awake()
        {
            base.Awake();
            AwakeUser();
        }

        /// <summary>
        /// The screen init method. Calls partial method <see cref="InitUser"/> to be implemented on the SDK side.
        /// </summary>
        public override void Init()
        {
            base.Init();
            InitUser();
        }

        /// <summary>
        /// The screen show method. Calls partial method <see cref="ShowUser"/> to be implemented on the SDK side.
        /// When entering this screen an async request to retrieve the available regions is started.
        /// </summary>
        public override void Show()
        {
            base.Show();
            SessionReset.ResetSessionStatics();

            var client = Connection?.Client;
            var me = client?.LocalPlayer;
            if (me != null)
            {
                me.SetCustomProperties(new Photon.Client.PhotonHashtable { ["team"] = null });
            }
            if (Config.CodeGenerator == null)
            {
                Debug.LogError("Add a CodeGenerator to the QuantumMenuConfig");
            }

            _sessionCodeField.SetTextWithoutNotify("".PadLeft(Config.CodeGenerator.Length, '-'));
            _sessionCodeField.characterLimit = Config.CodeGenerator.Length;

            if (_regionRequest == null || _regionRequest.IsFaulted)
            {
                _regionRequest = Connection.RequestAvailableOnlineRegionsAsync(ConnectionArgs);
            }

            ShowUser();
        }

        /// <summary>
        /// The screen hide method. Calls partial method <see cref="HideUser"/> to be implemented on the SDK side.
        /// </summary>
        public override void Hide()
        {
            base.Hide();
            HideUser();
        }

        /// <summary>
        /// Is called when the <see cref="_createButton"/> is pressed using SendMessage() from the UI object.
        /// </summary>
        protected virtual async void OnCreateButtonPressed()
        {
            await ConnectAsync(true);
        }

        /// <summary>
        /// Is called when the <see cref="_joinButton"/> is pressed using SendMessage() from the UI object.
        /// </summary>
        protected virtual async void OnJoinButtonPressed()
        {
            await ConnectAsync(false);
        }

        /// <summary>
        /// Is called when the <see cref="_backButton"/> is pressed using SendMessage() from the UI object.
        /// </summary>
        public virtual void OnBackButtonPressed()
        {
            Controller.Show<QuantumMenuUIMain>();
        }

        /// <summary>
        /// The connect method to handle create and join.
        /// Internally the region request is awaited.
        /// </summary>
        /// <param name="creating">Create or join</param>
        /// <returns></returns>
        protected virtual async System.Threading.Tasks.Task ConnectAsync(bool creating)
        {
            var inputRegionCode = _sessionCodeField.text.ToUpper();
            if (!creating && !Config.CodeGenerator.IsValid(inputRegionCode))
            {
                await Controller.PopupAsync(
                    $"The session code '{inputRegionCode}' is not a valid session code. Please enter {Config.CodeGenerator.Length} characters or digits.",
                    "Invalid Session Code"
                );
                return;
            }

            if (_regionRequest == null || _regionRequest.IsFaulted)
            {
                _regionRequest = Connection.RequestAvailableOnlineRegionsAsync(ConnectionArgs);
            }

            Controller.Show<QuantumMenuUILoading>();
            Controller.Get<QuantumMenuUILoading>().SetStatusText(creating ? "Creating Lobby..." : "Joining Lobby...");

            List<QuantumMenuOnlineRegion> regions = null;
            try
            {
                regions = await _regionRequest;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (regions == null || regions.Count == 0)
            {
                await Controller.PopupAsync("Failed to fetch available regions.", "Connection Failed");
                Controller.Show<QuantumMenuUIParty>();
                return;
            }

            // 6. Pick region
            int regionIndex = -1;
            if (creating)
            {
                // Force ASIA for creation (ignore PreferredRegion / best-ping)
                regionIndex = FindAsiaRegionIndex(regions);

                if (regionIndex == -1)
                {
                    await Controller.PopupAsync("ASIA region is not available right now.", "Connection Failed");
                    Controller.Show<QuantumMenuUIParty>();
                    return;
                }

                ConnectionArgs.Session = Config.CodeGenerator.EncodeRegion(Config.CodeGenerator.Create(), regionIndex);
                ConnectionArgs.Region = regions[regionIndex].Code; // should be "asia"
            }
            else
            {
                regionIndex = Config.CodeGenerator.DecodeRegion(inputRegionCode);
                if (regionIndex < 0 || regionIndex >= regions.Count)
                {
                    await Controller.PopupAsync(
                        $"The session code '{inputRegionCode}' is not a valid session code (cannot decode the region).",
                        "Invalid Session Code"
                    );
                    return;
                }

                ConnectionArgs.Session = inputRegionCode;
                ConnectionArgs.Region = regions[regionIndex].Code;
            }


            ConnectionArgs.Creating = creating;

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

            // Latejoin handling
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
                            if (ti == 0) blue++;
                            else if (ti == 1) red++;
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

            Controller.Show<QuantumMenuUILobby>();

            static string TeamName(int t) => (t == 1) ? "Red(1)" : (t == 0) ? "Blue(0)" : "None";
        }



        /// <summary>
        /// Find the region with the lowest ping.
        /// </summary>
        /// <param name="regions">Region list</param>
        /// <returns>The index of the region with the lowest ping</returns>
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

        protected static int FindAsiaRegionIndex(List<QuantumMenuOnlineRegion> regions)
        {
            if (regions == null) return -1;
            // Photon region code is "asia" (Singapore). Case-insensitive match.
            return regions.FindIndex(r => string.Equals(r.Code, "asia", StringComparison.OrdinalIgnoreCase));
        }

    }
}
