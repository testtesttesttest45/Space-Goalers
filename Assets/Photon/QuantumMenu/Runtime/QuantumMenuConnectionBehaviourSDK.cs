namespace Quantum.Menu
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Photon.Client;
    using Photon.Deterministic;
    using Photon.Realtime;
    using Quantum;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Input = Quantum.Input;

    partial class QuantumMenuConnectionBehaviour
    {
        public event Action<ShutdownCause, SessionRunner> SessionShutdownEvent;

        private GameObject _connHandlerGO;
        private Photon.Realtime.ConnectionHandler _connHandler;

        public virtual RealtimeClient Client { get; }

        protected void OnSessionShutdown(ShutdownCause shutdownCause, SessionRunner sessionRunner)
        {
            SessionShutdownEvent?.Invoke(shutdownCause, sessionRunner);
        }
    }

    public class QuantumMenuConnectionBehaviourSDK : QuantumMenuConnectionBehaviour
    {
        private CancellationTokenSource _cancellation;
        private CancellationTokenSource _linkedCancellation;
        private string _loadedScene;
        private QuantumMenuConnectionShutdownFlag _shutdownFlags;
        private DisconnectCause _disconnectCause;
        private IDisposable _disconnectSubscription;
        private RealtimeClient _client;

        [InlineHelp] public bool EnableMppm = false;

        public override string SessionName => Client?.CurrentRoom?.Name;
        public override string Region => Client?.CurrentRegion;
        public override string AppVersion => Client?.AppSettings?.AppVersion;
        public override int MaxPlayerCount => Client?.CurrentRoom != null ? Client.CurrentRoom.MaxPlayers : 0;

        public static bool PreventRestart = false;

        private const string ROOM_KEY_HOSTADDED = "hostAdded";
        private const string ROOM_KEY_PHASE = "startPhase";
        private const string ROOM_KEY_STARTED = "started";
        private const string ROOM_KEY_ENDED = "ended";
        public Quantum.RuntimeConfig CustomRuntimeConfig;

        public static SelectedAbilities CachedAbilities;
        public override List<string> Usernames
        {
            get
            {
                var frame = Runner ? Runner.Game?.Frames?.Verified : null;
                if (frame != null)
                {
                    var result = new List<string>(frame.MaxPlayerCount);
                    for (int i = 0; i < frame.MaxPlayerCount; i++)
                    {
                        var isPlayerConnected = (frame.GetPlayerInputFlags(i) & DeterministicInputFlags.PlayerNotPresent) == 0;
                        if (isPlayerConnected)
                        {
                            var playerNickname = frame.GetPlayerData(i)?.PlayerNickname;
                            if (string.IsNullOrEmpty(playerNickname)) playerNickname = $"Player{i:02}";
                            result.Add(playerNickname);
                        }
                        else
                        {
                            result.Add(null);
                        }
                    }
                    return result;
                }
                return null;
            }
        }

        // --- keep Realtime client ticking in lobby ---
        private GameObject _serviceGO;
        private ServiceUpdater _serviceUpdater;
        private int _activeSlotCount = 0;   // already there
        private int _mySlot = -1;

        private class ServiceUpdater : MonoBehaviour { public RealtimeClient Client; void Update() { Client?.Service(); } }

        private void ForcePreserveSelectedAbilities(Quantum.RuntimeConfig config)
        {
            if (config.SelectedBySlot == null || config.SelectedBySlot.Length == 0)
                config.SelectedBySlot = new SelectedAbilities[6];
        }

        private void EnsureMenuService(RealtimeClient client)
        {
            if (_serviceGO == null)
            {
                _serviceGO = new GameObject("Photon Service (Menu)");
                UnityEngine.Object.DontDestroyOnLoad(_serviceGO);
                _serviceUpdater = _serviceGO.AddComponent<ServiceUpdater>();
            }
            _serviceUpdater.Client = client;
        }
        private void StopMenuService()
        {
            if (_serviceGO != null) { UnityEngine.Object.Destroy(_serviceGO); _serviceGO = null; _serviceUpdater = null; }
        }

        public override bool IsConnected => Client == null ? false : Client.IsConnected;
        public override int Ping => (Runner != null && Runner.Session != null) ? Runner.Session.Stats.Ping : 0;

        public override RealtimeClient Client => _client;
        public QuantumRunner Runner { get; private set; }

        protected virtual void OnConnect(QuantumMenuConnectArgs connectArgs, ref MatchmakingArguments args) { }
        protected virtual void OnConnected(RealtimeClient client) { EnsureMenuService(client); }
        protected virtual void OnStart(ref SessionRunner.Arguments args) { }
        protected virtual void OnStarted(QuantumRunner runner)
        {
            StopMenuService();
        }


        protected virtual void OnCleanup() { }

        private void Awake()
        {
            SessionShutdownEvent += OnSessionEndedMarkRoom;
        }

        private void OnDestroy()
        {
            SessionShutdownEvent -= OnSessionEndedMarkRoom;
        }

        // Called by the event when the runner shuts down
        private void OnSessionEndedMarkRoom(ShutdownCause cause, SessionRunner runner)
        {
            try
            {
                var room = Client?.CurrentRoom;
                if (room == null) return;

                room.SetCustomProperties(new Photon.Client.PhotonHashtable
                {
                    [ROOM_KEY_STARTED] = false,
                    [ROOM_KEY_ENDED] = true
                });

                // keep joinable by code so  can show "Game has ended"
                room.IsOpen = true;     // allow joiners to get the friendly message
                room.IsVisible = false;

                Debug.Log("[Room] Marked ended (started=false, ended=true).");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        private async void EnsureLocalTeamAssignedBalanced()
        {
            var room = Client?.CurrentRoom;
            var me = Client?.LocalPlayer;
            if (room == null || me == null) return;

            // If the room already ended, don't assign anything (prevents "blueing" late visitors).
            if (room.CustomProperties != null &&
                room.CustomProperties.TryGetValue(ROOM_KEY_ENDED, out var endedObj) &&
                endedObj is bool endedFlag && endedFlag)
            {
                Debug.Log("[Connect] Room is ended; skipping auto team assignment.");
                return;
            }

            // Wait until MasterClientId is known (non-zero) or timeout
            var t0 = Time.realtimeSinceStartup;
            while (room.MasterClientId == 0 && Time.realtimeSinceStartup - t0 < 2.0f)
            {
                Client?.Service();
                await System.Threading.Tasks.Task.Delay(50);
            }

            // already has a team? don't touch it
            if (me.CustomProperties != null && me.CustomProperties.ContainsKey(PLAYER_KEY_TEAM))
                return;

            // Count current teams
            int blue = 0, red = 0;
            foreach (var p in room.Players.Values)
            {
                if (p.CustomProperties != null &&
                    p.CustomProperties.TryGetValue(PLAYER_KEY_TEAM, out var teamObj) &&
                    teamObj is int teamInt)
                {
                    if (teamInt == 0) blue++;
                    else if (teamInt == 1) red++;
                }
            }

            int team;
            if (me.ActorNumber == room.MasterClientId)
                team = 0;                    // host starts Blue
            else if (blue == 0 && red == 0)
                team = 1;                    // first non-host joins → Red
            else
                team = (blue <= red) ? 0 : 1;

            me.SetCustomProperties(new Photon.Client.PhotonHashtable { [PLAYER_KEY_TEAM] = team });
            Debug.Log($"[Connect] Auto-assign local team: {(team == 0 ? "Blue(0)" : "Red(1)")} (Actor={me.ActorNumber})");
        }


        protected override async Task<ConnectResult> ConnectAsyncInternal(QuantumMenuConnectArgs connectArgs)
        {
            PatchConnectArgs(connectArgs);
            _lastConnectArgs = connectArgs;

            if (string.IsNullOrEmpty(connectArgs.AppSettings.AppIdQuantum))
            {
                return ConnectResult.Fail(ConnectFailReason.NoAppId,
#if UNITY_EDITOR
                    "AppId missing.\n\nOpen the Quantum Hub and follow the installation steps to create a Quantum 3 AppId.");
#else
            "AppId missing");
#endif
            }

            if (_cancellation != null)
                throw new Exception("Connection instance still in use");

            _cancellation = new CancellationTokenSource();
            _linkedCancellation = AsyncSetup.CreateLinkedSource(_cancellation.Token);
            _shutdownFlags = connectArgs.ShutdownFlags;
            _disconnectCause = DisconnectCause.None;

            var arguments = new MatchmakingArguments
            {
                PhotonSettings = new AppSettings(connectArgs.AppSettings)
                {
                    AppVersion = connectArgs.AppVersion,
                    FixedRegion = connectArgs.Region
                },
                ReconnectInformation = connectArgs.ReconnectInformation,
                EmptyRoomTtlInSeconds = connectArgs.ServerSettings.EmptyRoomTtlInSeconds,
                EnableCrc = connectArgs.ServerSettings.EnableCrc,
                PlayerTtlInSeconds = connectArgs.ServerSettings.PlayerTtlInSeconds,
                MaxPlayers = connectArgs.MaxPlayerCount,
                RoomName = connectArgs.Session,
                CanOnlyJoin = string.IsNullOrEmpty(connectArgs.Session) == false && !connectArgs.Creating,
                PluginName = connectArgs.PhotonPluginName,
                AsyncConfig = new AsyncConfig()
                {
                    TaskFactory = AsyncConfig.CreateUnityTaskFactory(),
                    CancellationToken = _linkedCancellation.Token
                },
                NetworkClient = connectArgs.Client,
                AuthValues = connectArgs.AuthValues,
            };

            try
            {
                OnConnect(connectArgs, ref arguments);
                if (connectArgs.Reconnecting == false)
                {
                    ReportProgress("Connecting..");
                    _client = await MatchmakingExtensions.ConnectToRoomAsync(arguments);
                    OnConnected(_client);
                }
                else
                {
                    ReportProgress("Reconnecting..");
                    _client = await MatchmakingExtensions.ReconnectToRoomAsync(arguments);
                }
                OnConnected(_client);

                if (!string.IsNullOrEmpty(connectArgs.Username))
                {
                    try
                    {
                        var nickWithAbilities = BuildNickWithAbilities(connectArgs.Username);
                        _client.NickName = nickWithAbilities;
                        if (_client.LocalPlayer != null)
                            _client.LocalPlayer.NickName = nickWithAbilities;
                        Debug.Log($"[NickSet] Photon nickname = {nickWithAbilities}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed setting Photon nick: {ex.Message}");
                    }
                }

                try
                {
                    var selSync = ReadLocalSelectedAbilities();
                    CachedAbilities = selSync;

                    if (_client?.LocalPlayer != null)
                    {
                        _client.LocalPlayer.SetCustomProperties(new Photon.Client.PhotonHashtable
                        {
                            ["AbilityPref.Utility.Enum"] = selSync.Utility.ToString(),
                            ["AbilityPref.Main1.Enum"] = selSync.Main1.ToString(),
                            ["AbilityPref.Main2.Enum"] = selSync.Main2.ToString()
                        });
                        Debug.Log($"[LobbySync] Cached + Preloaded: {selSync.Utility}, {selSync.Main1}, {selSync.Main2}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LobbySync] Failed to set custom props early: {e.Message}");
                }

                EnsureLocalTeamAssignedBalanced();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new ConnectResult
                {
                    FailReason =
                        AsyncConfig.Global.IsCancellationRequested ? ConnectFailReason.ApplicationQuit :
                        _disconnectCause == DisconnectCause.None ? ConnectFailReason.RunnerFailed : ConnectFailReason.Disconnect,
                    DisconnectCause = (int)_disconnectCause,
                    DebugMessage = e.Message,
                    WaitForCleanup = CleanupAsync()
                };
            }

            if (!string.IsNullOrEmpty(Client.SummaryToCache))
            {
                connectArgs.ServerSettings.BestRegionSummary = Client.SummaryToCache;
            }

            _disconnectSubscription = Client.CallbackMessage.ListenManual<OnDisconnectedMsg>(m =>
            {
                if (_cancellation != null && _cancellation.IsCancellationRequested == false)
                {
                    _disconnectCause = m.cause;
                    _cancellation.Cancel();
                }
            });

            var preloadMap = false;
            if (connectArgs.RuntimeConfig != null
                && connectArgs.RuntimeConfig.Map.Id.IsValid
                && connectArgs.RuntimeConfig.SimulationConfig.Id.IsValid)
            {
                if (QuantumUnityDB.TryGetGlobalAsset(connectArgs.RuntimeConfig.SimulationConfig, out Quantum.SimulationConfig simulationConfigAsset))
                {
                    preloadMap = simulationConfigAsset.AutoLoadSceneFromMap == SimulationConfig.AutoLoadSceneFromMapMode.Disabled;
                }
            }

            if (HoldLobbyGate.SuppressAutoStart)
                preloadMap = false;

            if (preloadMap)
            {
                ReportProgress("Loading..");
                if (QuantumUnityDB.TryGetGlobalAsset(connectArgs.RuntimeConfig.Map, out Quantum.Map map) == false)
                {
                    return new ConnectResult
                    {
                        FailReason = ConnectFailReason.MapNotFound,
                        DebugMessage = $"Requested map {connectArgs.RuntimeConfig.Map} not found.",
                        WaitForCleanup = CleanupAsync()
                    };
                }

                using (new ConnectionServiceScope(Client))
                {
                    try
                    {
                        await SceneManager.LoadSceneAsync(map.Scene, LoadSceneMode.Additive);
                        SceneManager.SetActiveScene(SceneManager.GetSceneByName(map.Scene));
                        _loadedScene = map.Scene;

                        if (_linkedCancellation.Token.IsCancellationRequested)
                            throw new TaskCanceledException();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        return new ConnectResult
                        {
                            FailReason =
                                AsyncConfig.Global.IsCancellationRequested ? ConnectFailReason.ApplicationQuit :
                                _disconnectCause == DisconnectCause.None ? ConnectFailReason.RunnerFailed : ConnectFailReason.Disconnect,
                            DisconnectCause = (int)_disconnectCause,
                            DebugMessage = e.Message,
                            WaitForCleanup = CleanupAsync()
                        };
                    }
                }
            }

            ReportProgress("Starting..");

            _lastConnectArgs = connectArgs;
            if (HoldLobbyGate.SuppressAutoStart)
            {
                ReportProgress("Connected. Waiting in lobby…");
                return new ConnectResult { Success = true };
            }

            await WaitForTeamMapAsync();
            ApplySlotOrderAndTeams(connectArgs);

            int capacity = Client?.CurrentRoom?.MaxPlayers ?? connectArgs.MaxPlayerCount;
            if (capacity <= 0) capacity = 2;

            var sel = ReadLocalSelectedAbilities();
            sel.IsSet = true;

            int slot = _mySlot >= 0 ? _mySlot : 0;
            connectArgs.RuntimeConfig.SelectedBySlot[slot] = sel;

            ForcePreserveSelectedAbilities(connectArgs.RuntimeConfig);

            Debug.Log($"[RuntimeConfig] Injected abilities for slot {slot}: {sel.Utility}, {sel.Main1}, {sel.Main2}");

            // debug aid
            PlayerPrefs.SetString("LastRuntimeAbilities", $"{sel.Utility},{sel.Main1},{sel.Main2}");
            PlayerPrefs.Save();

            if (connectArgs.SessionConfig?.Config != null)
            {
                connectArgs.SessionConfig.Config.PlayerCount = capacity;
            }

            var sessionRunnerArguments = new SessionRunner.Arguments
            {
                RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
                GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
                ClientId =
                    string.IsNullOrEmpty(connectArgs.QuantumClientId) == false ? connectArgs.QuantumClientId :
                    string.IsNullOrEmpty(Client.UserId) == false ? Client.UserId :
                    Guid.NewGuid().ToString(),
                RuntimeConfig = connectArgs.RuntimeConfig,
                SessionConfig = (connectArgs.SessionConfig != null ? connectArgs.SessionConfig.Config : null)
                    ?? QuantumDeterministicSessionConfigAsset.DefaultConfig,
                GameMode = DeterministicGameMode.Multiplayer,
                PlayerCount = capacity,
                Communicator = new QuantumNetworkCommunicator(Client),
                CancellationToken = _linkedCancellation.Token,
                RecordingFlags = connectArgs.RecordingFlags,
                InstantReplaySettings = connectArgs.InstantReplaySettings,
                DeltaTimeType = connectArgs.DeltaTimeType,
                StartGameTimeoutInSeconds = connectArgs.StartGameTimeoutInSeconds,
                GameFlags = connectArgs.GameFlags,
                OnShutdown = OnSessionShutdown,
            };

            if (EnableMppm)
            {
                QuantumMppm.MainEditor?.Send(new QuantumMenuMppmJoinCommand()
                {
                    AppVersion = connectArgs.AppVersion,
                    Session = Client.CurrentRoom.Name,
                    Region = Client.CurrentRegion,
                });
            }

            string pluginDisconnectReason = null;
            var pluginDisconnectListener = QuantumCallback.SubscribeManual<CallbackPluginDisconnect>(m => pluginDisconnectReason = m.Reason);

            try
            {
                OnStart(ref sessionRunnerArguments);
                Runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);
                OnStarted(Runner);
                StopMenuService();
            }
            catch (Exception e)
            {
                pluginDisconnectListener.Dispose();
                Debug.LogException(e);
                return new ConnectResult
                {
                    FailReason = DetermineFailReason(_disconnectCause, pluginDisconnectReason),
                    DisconnectCause = (int)_disconnectCause,
                    DebugMessage = pluginDisconnectReason ?? e.Message,
                    WaitForCleanup = CleanupAsync()
                };
            }

            pluginDisconnectListener.Dispose();
            _cancellation.Dispose(); _cancellation = null;
            _linkedCancellation.Dispose(); _linkedCancellation = null;
            _disconnectSubscription.Dispose(); _disconnectSubscription = null;

            int my = (_mySlot >= 0 && _mySlot < connectArgs.RuntimePlayers.Length) ? _mySlot : 0;
            int myTeam = GetMyTeamFromRuntime(connectArgs); // 0 = Blue, 1 = Red

            if (IsHostNow())
            {
                Client?.CurrentRoom?.SetCustomProperties(new Photon.Client.PhotonHashtable
                {
                    ["startPhase"] = (myTeam == 0) ? "blue" : "red"
                });
                await Task.Delay(50);
            }

            if (myTeam == 0)
            {
                await WaitForPhaseAsync("blue");
                Runner.Game.AddPlayer(my, connectArgs.RuntimePlayers[my]);

                if (IsHostNow())
                {
                    bool hasRed = connectArgs.RuntimeConfig.InitialTeamBySlot != null &&
                                  connectArgs.RuntimeConfig.InitialTeamBySlot.Any(t => t == 1);
                    if (hasRed)
                    {
                        await Task.Delay(50);
                        Client?.CurrentRoom?.SetCustomProperties(new Photon.Client.PhotonHashtable
                        {
                            ["startPhase"] = "red"
                        });
                    }
                }
            }
            else
            {
                await WaitForPhaseAsync("red");
                Runner.Game.AddPlayer(my, connectArgs.RuntimePlayers[my]);

                if (IsHostNow())
                {
                    bool hasBlue = connectArgs.RuntimeConfig.InitialTeamBySlot != null &&
                                   connectArgs.RuntimeConfig.InitialTeamBySlot.Any(t => t == 0);
                    if (hasBlue)
                    {
                        await Task.Delay(50);
                        Client?.CurrentRoom?.SetCustomProperties(new Photon.Client.PhotonHashtable
                        {
                            ["startPhase"] = "blue"
                        });
                    }
                }
            }

            return new ConnectResult { Success = true };
        }

        private async Task WaitForTeamMapAsync()
        {
            var room = Client?.CurrentRoom;
            float t0 = Time.realtimeSinceStartup;
            while (room != null)
            {
                if (room.CustomProperties != null &&
                    room.CustomProperties.TryGetValue(ROOM_KEY_TEAMMAP, out var tm) &&
                    tm is string s && !string.IsNullOrEmpty(s))
                {
                    return;
                }
                Client?.Service();

                if (Time.realtimeSinceStartup - t0 > 5.0f)
                { // was 2.0f
                    Debug.LogWarning("teamMap not received; proceeding with deterministic fallback");
                    return;
                }
                await Task.Delay(50);
                room = Client?.CurrentRoom;
            }
        }

        protected override Task DisconnectAsyncInternal(int reason)
        {
            if (reason == ConnectFailReason.UserRequest)
            {
                QuantumReconnectInformation.Reset();
            }

            if (_cancellation != null)
            {
                try { _cancellation.Cancel(); } catch { }
                return CleanupAsync();
            }

            return CleanupAsync();
        }

        private QuantumMenuConnectArgs _lastConnectArgs;

        public async Task<ConnectResult> StartGameFromLobbyAsync()
        {
            if (PreventRestart)
            {
                Debug.Log("[Lobby] PreventRestart = true, not starting new game");
                return ConnectResult.Fail(ConnectFailReason.UserRequest, "Game ended manually");
            }
            if (Runner != null)
                return new ConnectResult { Success = true };

            if (Client == null || !Client.IsConnected)
                return ConnectResult.Fail(ConnectFailReason.Disconnect, "Not connected.");

            if (_lastConnectArgs == null)
                return ConnectResult.Fail(ConnectFailReason.RunnerFailed, "Missing connect args.");

            var connectArgs = _lastConnectArgs;

            var preloadMap = false;
            if (connectArgs.RuntimeConfig != null
                && connectArgs.RuntimeConfig.Map.Id.IsValid
                && connectArgs.RuntimeConfig.SimulationConfig.Id.IsValid)
            {
                if (QuantumUnityDB.TryGetGlobalAsset(connectArgs.RuntimeConfig.SimulationConfig, out Quantum.SimulationConfig simCfg))
                    preloadMap = simCfg.AutoLoadSceneFromMap == SimulationConfig.AutoLoadSceneFromMapMode.Disabled;
            }

            if (preloadMap)
            {
                ReportProgress("Loading..");
                if (!QuantumUnityDB.TryGetGlobalAsset(connectArgs.RuntimeConfig.Map, out Quantum.Map map))
                {
                    return new ConnectResult
                    {
                        FailReason = ConnectFailReason.MapNotFound,
                        DebugMessage = $"Requested map {connectArgs.RuntimeConfig.Map} not found.",
                        WaitForCleanup = CleanupAsync()
                    };
                }

                try
                {
                    using (new ConnectionServiceScope(Client))
                    {
                        await UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(map.Scene, UnityEngine.SceneManagement.LoadSceneMode.Additive);
                        UnityEngine.SceneManagement.SceneManager.SetActiveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByName(map.Scene));
                        _loadedScene = map.Scene;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return new ConnectResult
                    {
                        FailReason = ConnectFailReason.RunnerFailed,
                        DebugMessage = e.Message,
                        WaitForCleanup = CleanupAsync()
                    };
                }
            }

            ReportProgress("Starting..");

            await WaitForTeamMapAsync();
            ApplySlotOrderAndTeams(connectArgs);

            // Capacity for arrays
            int capacity = Client?.CurrentRoom?.MaxPlayers ?? connectArgs.MaxPlayerCount;
            if (capacity <= 0) capacity = 2;

            if (connectArgs.SessionConfig?.Config != null)
                connectArgs.SessionConfig.Config.PlayerCount = capacity;

            int mySlot = _mySlot >= 0 ? _mySlot : 0;
            var sel = (CachedAbilities.IsSet ? CachedAbilities : ReadLocalSelectedAbilities());
            sel.IsSet = true;

            EnsureSized(ref connectArgs.RuntimeConfig.SelectedBySlot, Math.Max(2, mySlot + 1));
            connectArgs.RuntimeConfig.SelectedBySlot[mySlot] = sel;

            Debug.Log($"[RC.Apply] Using CachedAbilities for slot {mySlot}: {sel.Utility}, {sel.Main1}, {sel.Main2}");

            if (Client?.LocalPlayer != null)
            {
                Client.LocalPlayer.SetCustomProperties(new Photon.Client.PhotonHashtable
                {
                    ["AbilityPref.Utility.Enum"] = sel.Utility.ToString(),
                    ["AbilityPref.Main1.Enum"] = sel.Main1.ToString(),
                    ["AbilityPref.Main2.Enum"] = sel.Main2.ToString()
                });
            }

            var sessionRunnerArguments = new SessionRunner.Arguments
            {
                RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
                GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
                ClientId =
                    string.IsNullOrEmpty(connectArgs.QuantumClientId) == false ? connectArgs.QuantumClientId :
                    string.IsNullOrEmpty(Client.UserId) == false ? Client.UserId : Guid.NewGuid().ToString(),
                RuntimeConfig = connectArgs.RuntimeConfig,
                SessionConfig = (connectArgs.SessionConfig != null ? connectArgs.SessionConfig.Config : null)
                    ?? QuantumDeterministicSessionConfigAsset.DefaultConfig,
                GameMode = DeterministicGameMode.Multiplayer,
                PlayerCount = capacity,
                Communicator = new QuantumNetworkCommunicator(Client),
                CancellationToken = _linkedCancellation?.Token ?? AsyncConfig.Global.CancellationToken,
                RecordingFlags = connectArgs.RecordingFlags,
                InstantReplaySettings = connectArgs.InstantReplaySettings,
                DeltaTimeType = connectArgs.DeltaTimeType,
                StartGameTimeoutInSeconds = connectArgs.StartGameTimeoutInSeconds,
                GameFlags = connectArgs.GameFlags,
                OnShutdown = OnSessionShutdown,
            };

            if (EnableMppm)
            {
                QuantumMppm.MainEditor?.Send(new QuantumMenuMppmJoinCommand()
                {
                    AppVersion = connectArgs.AppVersion,
                    Session = Client.CurrentRoom.Name,
                    Region = Client.CurrentRegion,
                });
            }

            string pluginDisconnectReason = null;
            var pluginDisconnectListener = QuantumCallback.SubscribeManual<CallbackPluginDisconnect>(m => pluginDisconnectReason = m.Reason);

            try
            {
                OnStart(ref sessionRunnerArguments);

                if (connectArgs.RuntimeConfig is Quantum.RuntimeConfig rcFromArgs &&
                    sessionRunnerArguments.RuntimeConfig is Quantum.RuntimeConfig rcInArgs)
                {
                    rcInArgs.SelectedBySlot = rcFromArgs.SelectedBySlot;
                }
                else
                {
                }

                var pre = (Quantum.RuntimeConfig)sessionRunnerArguments.RuntimeConfig;
                for (int i = 0; i < pre.SelectedBySlot?.Length; i++)
                {
                    var s = pre.SelectedBySlot[i];
                }
                if (Client?.LocalPlayer != null && !string.IsNullOrEmpty(Client.LocalPlayer.NickName))
                {
                    var encodedNick = BuildNickWithAbilities(NameUtils.CleanName(Client.LocalPlayer.NickName));
                    int slotIndex = _mySlot >= 0 ? _mySlot : 0;

                    if (connectArgs.RuntimePlayers != null && slotIndex < connectArgs.RuntimePlayers.Length)
                        connectArgs.RuntimePlayers[slotIndex].PlayerNickname = encodedNick;

                    Debug.Log($"[Lobby→StartFix] Injected nickname for slot {slotIndex}: {encodedNick}");
                }

                Runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);

                OnStarted(Runner);
                StopMenuService();
            }
            catch (Exception e)
            {
                pluginDisconnectListener.Dispose();
                Debug.LogException(e);
                return new ConnectResult
                {
                    FailReason = DetermineFailReason(_disconnectCause, pluginDisconnectReason),
                    DisconnectCause = (int)_disconnectCause,
                    DebugMessage = pluginDisconnectReason ?? e.Message,
                    WaitForCleanup = CleanupAsync()
                };
            }

            pluginDisconnectListener.Dispose();
            _cancellation?.Dispose(); _cancellation = null;
            _linkedCancellation?.Dispose(); _linkedCancellation = null;
            _disconnectSubscription?.Dispose(); _disconnectSubscription = null;

            // phased add logic
            int my = (_mySlot >= 0 && _mySlot < connectArgs.RuntimePlayers.Length) ? _mySlot : 0;
            int myTeam = GetMyTeamFromRuntime(connectArgs);

            if (IsHostNow())
            {
                Client?.CurrentRoom?.SetCustomProperties(new Photon.Client.PhotonHashtable
                {
                    ["startPhase"] = (myTeam == 0) ? "blue" : "red"
                });
                await Task.Delay(50);
            }

            if (myTeam == 0)
            {
                await WaitForPhaseAsync("blue");
                Runner.Game.AddPlayer(my, connectArgs.RuntimePlayers[my]);
                if (IsHostNow())
                {
                    bool hasRed = connectArgs.RuntimeConfig.InitialTeamBySlot != null &&
                                  connectArgs.RuntimeConfig.InitialTeamBySlot.Any(t => t == 1);
                    if (hasRed)
                    {
                        await Task.Delay(50);
                        Client?.CurrentRoom?.SetCustomProperties(new Photon.Client.PhotonHashtable
                        {
                            ["startPhase"] = "red"
                        });
                    }
                }
            }
            else
            {
                await WaitForPhaseAsync("red");
                Runner.Game.AddPlayer(my, connectArgs.RuntimePlayers[my]);
                if (IsHostNow())
                {
                    bool hasBlue = connectArgs.RuntimeConfig.InitialTeamBySlot != null &&
                                   connectArgs.RuntimeConfig.InitialTeamBySlot.Any(t => t == 0);
                    if (hasBlue)
                    {
                        await Task.Delay(50);
                        Client?.CurrentRoom?.SetCustomProperties(new Photon.Client.PhotonHashtable
                        {
                            ["startPhase"] = "blue"
                        });
                    }
                }
            }

            return new ConnectResult { Success = true };
        }

        public override Task<List<QuantumMenuOnlineRegion>> RequestAvailableOnlineRegionsAsync(QuantumMenuConnectArgs connectArgs)
        {
            var client = connectArgs.Client ?? new RealtimeClient();
            var appSettings = connectArgs.AppSettings ?? PhotonServerSettings.Global.AppSettings;
            if (string.IsNullOrEmpty(appSettings.AppIdQuantum))
            {
                return Task.FromException<List<QuantumMenuOnlineRegion>>(new Exception("AppId Missing"));
            }

            var regionHandler = client.ConnectToNameserverAndWaitForRegionsAsync(appSettings);
            return regionHandler.ContinueWith(x => {
                return x.Result.EnabledRegions.Select(r => new QuantumMenuOnlineRegion { Code = r.Code, Ping = r.Ping }).ToList();
            }, AsyncConfig.Global.TaskScheduler);
        }

        private static void PatchConnectArgs(QuantumMenuConnectArgs connectArgs)
        {
            if (connectArgs.ServerSettings == null) connectArgs.ServerSettings = PhotonServerSettings.Global;
            if (connectArgs.SessionConfig == null) connectArgs.SessionConfig = QuantumDeterministicSessionConfigAsset.Global;

            connectArgs.MaxPlayerCount = Math.Min(connectArgs.MaxPlayerCount, Input.MaxCount);

            connectArgs.RuntimeConfig = JsonUtility.FromJson<RuntimeConfig>(JsonUtility.ToJson(connectArgs.Scene.RuntimeConfig));
            if (connectArgs.RuntimeConfig.SelectedBySlot == null)
                connectArgs.RuntimeConfig.SelectedBySlot = new SelectedAbilities[6];
            if (connectArgs.RuntimeConfig.Seed == 0)
                connectArgs.RuntimeConfig.Seed = Guid.NewGuid().GetHashCode();

            if (connectArgs.RuntimeConfig.SimulationConfig.Id.IsValid == false &&
                QuantumDefaultConfigs.TryGetGlobal(out var defaultConfigs))
            {
                connectArgs.RuntimeConfig.SimulationConfig = defaultConfigs.SimulationConfig;
            }

            var baseName = string.IsNullOrEmpty(connectArgs.Username) ? "Player" : connectArgs.Username;
            var nickWithAbilities = baseName;

            if (connectArgs.RuntimePlayers == null || connectArgs.RuntimePlayers.Length == 0)
                connectArgs.RuntimePlayers = new RuntimePlayer[] { new RuntimePlayer() };

            connectArgs.RuntimePlayers[0].PlayerNickname = nickWithAbilities;

            if (connectArgs.AuthValues == null ||
                (connectArgs.AuthValues.AuthType == Photon.Realtime.CustomAuthenticationType.None &&
                 string.IsNullOrEmpty(connectArgs.AuthValues.UserId)))
            {
                connectArgs.AuthValues ??= new Photon.Realtime.AuthenticationValues();
                connectArgs.AuthValues.UserId = $"{baseName}({new System.Random().Next(99999999):00000000})";
            }
        }

        private static string BuildNickWithAbilities(string baseName)
        {
            string u = PlayerPrefs.GetString("AbilityPref.Utility.Enum", "Dash");
            string m1 = PlayerPrefs.GetString("AbilityPref.Main1.Enum", "Attack");
            string m2 = PlayerPrefs.GetString("AbilityPref.Main2.Enum", "Block");

            if (string.Equals(m1, m2, StringComparison.OrdinalIgnoreCase))
            {
                if (m1.Equals("Bomb", StringComparison.OrdinalIgnoreCase)) m2 = "Bomb";
            }

            return $"{baseName} [U:{u};M:{m1},{m2}]";
        }

        public static int DetermineFailReason(DisconnectCause disconnectCause, string pluginDisconnectReason)
        {
            if (AsyncConfig.Global.IsCancellationRequested) return ConnectFailReason.ApplicationQuit;

            switch (disconnectCause)
            {
                case DisconnectCause.None: return ConnectFailReason.RunnerFailed;
                case DisconnectCause.DisconnectByClientLogic:
                    if (string.IsNullOrEmpty(pluginDisconnectReason) == false) return ConnectFailReason.PluginError;
                    return ConnectFailReason.Disconnect;
                default:
                    return ConnectFailReason.Disconnect;
            }
        }

        private async Task CleanupAsync()
        {
            try { OnCleanup(); } catch (Exception e) { Debug.LogException(e); }
            StopMenuService();

            _cancellation?.Dispose(); _cancellation = null;
            _linkedCancellation?.Dispose(); _linkedCancellation = null;
            _disconnectSubscription?.Dispose(); _disconnectSubscription = null;

            if (Runner != null && (_shutdownFlags & QuantumMenuConnectionShutdownFlag.ShutdownRunner) >= 0)
            {
                try
                {
                    if (AsyncConfig.Global.IsCancellationRequested) Runner.Shutdown();
                    else await Runner.ShutdownAsync();
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            Runner = null;

            if (Client != null && (_shutdownFlags & QuantumMenuConnectionShutdownFlag.Disconnect) >= 0)
            {
                try
                {
                    if (AsyncConfig.Global.IsCancellationRequested) Client.Disconnect();
                    else await Client.DisconnectAsync();
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            _client = null;

            if (!string.IsNullOrEmpty(_loadedScene) &&
                (_shutdownFlags & QuantumMenuConnectionShutdownFlag.ShutdownRunner) >= 0 &&
                AsyncConfig.Global.IsCancellationRequested == false)
            {
                try { await SceneManager.UnloadSceneAsync(_loadedScene); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _loadedScene = null;
        }

        private const string ROOM_KEY_TEAMMAP = "teamMap";
        private const string PLAYER_KEY_TEAM = "team";

        private static int[] ParseSlotOrder(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            var arr = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = int.TryParse(parts[i], out var v) ? v : -1;
            return arr;
        }

        private static Dictionary<int, int> ParseTeamMapDict(string s)
        {
            var dict = new Dictionary<int, int>();
            if (string.IsNullOrEmpty(s)) return dict;
            foreach (var pair in s.Split(';'))
            {
                var kv = pair.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out var actor) && int.TryParse(kv[1], out var team))
                    dict[actor] = (team == 1) ? 1 : 0;
            }
            return dict;
        }

        private void ApplySlotOrderAndTeams(QuantumMenuConnectArgs args)
        {
            var room = Client?.CurrentRoom ?? args?.Client?.CurrentRoom;
            if (room == null || args == null) return;

            const string ROOM_KEY_SLOTORDER = "slotOrder";
            const string ROOM_KEY_TEAMMAP = "teamMap";
            const string PLAYER_KEY_TEAM = "team";

            int capacity = room.MaxPlayers > 0 ? room.MaxPlayers : Math.Max(args.MaxPlayerCount, 1);

            int[] slotOrder = null;
            if (room.CustomProperties != null &&
                room.CustomProperties.TryGetValue(ROOM_KEY_SLOTORDER, out var so) &&
                so is string sos)
            {
                slotOrder = ParseSlotOrder(sos);
            }

            var orderedActors = room.Players.Values
                .OrderBy(p => p.ActorNumber)
                .Select(p => p.ActorNumber)
                .ToArray();

            if (slotOrder == null || slotOrder.Length == 0)
                slotOrder = orderedActors;

            if (slotOrder.Length < capacity)
            {
                var extended = new int[capacity];
                Array.Copy(slotOrder, extended, slotOrder.Length);
                for (int i = slotOrder.Length; i < capacity; i++) extended[i] = -1;
                slotOrder = extended;
            }
            else if (slotOrder.Length > capacity)
            {
                Array.Resize(ref slotOrder, capacity);
            }

            var actorToSlot = new Dictionary<int, int>(capacity);
            for (int i = 0; i < capacity; i++)
                if (slotOrder[i] >= 0) actorToSlot[slotOrder[i]] = i;

            int[] initial = args.RuntimeConfig?.InitialTeamBySlot;
            if (initial == null || initial.Length != capacity)
            {
                var teamMap = new Dictionary<int, int>();
                if (room.CustomProperties != null &&
                    room.CustomProperties.TryGetValue(ROOM_KEY_TEAMMAP, out var tm) &&
                    tm is string tms && !string.IsNullOrEmpty(tms))
                {
                    teamMap = ParseTeamMapDict(tms); // normalizes to 0/1
                }

                initial = new int[capacity];
                for (int i = 0; i < capacity; i++)
                {
                    int actor = slotOrder[i];
                    int team = 0; // default filler

                    if (actor >= 0)
                    {
                        if (teamMap.TryGetValue(actor, out var fromMap))
                        {
                            team = fromMap; // authoritative from host snapshot
                        }
                        else if (room.Players.TryGetValue(actor, out var p) &&
                                 p.CustomProperties != null &&
                                 p.CustomProperties.TryGetValue(PLAYER_KEY_TEAM, out var tv) &&
                                 tv is int ti)
                        {
                            team = (ti == 1) ? 1 : 0; // 0=Blue, 1=Red
                        }
                        else
                        {
                            // deterministic fallback if no info
                            team = (actor % 2 == 1) ? 0 : 1;
                        }
                    }

                    initial[i] = team;
                }

                args.RuntimeConfig.InitialTeamBySlot = initial;
            }
            else
            {
                // make sure it really is sized to capacity
                if (initial.Length != capacity)
                {
                    Array.Resize(ref initial, capacity);
                    args.RuntimeConfig.InitialTeamBySlot = initial;
                }
            }

            var template = (args.RuntimePlayers != null && args.RuntimePlayers.Length > 0)
                 ? args.RuntimePlayers[0]
                 : new RuntimePlayer();
            var newRp = new RuntimePlayer[capacity];
            for (int i = 0; i < capacity; i++) newRp[i] = template;
            args.RuntimePlayers = newRp;

            var me = Client?.LocalPlayer;
            int myActor = me?.ActorNumber ?? -1;

            if (!actorToSlot.TryGetValue(myActor, out _mySlot))
            {
                _mySlot = Array.FindIndex(slotOrder, x => x < 0);
                if (_mySlot < 0) _mySlot = 0;
            }

            int blueCount = 0, redCount = 0;
            for (int i = 0; i < capacity; i++)
            {
                int actor = slotOrder[i];
                if (actor < 0) continue;
                int t = initial[i];
                if (t == 0) blueCount++;
                else if (t == 1) redCount++;
            }

            int myTeamProp = -1;
            if (me?.CustomProperties != null &&
                me.CustomProperties.TryGetValue(PLAYER_KEY_TEAM, out var myTeamObj) &&
                myTeamObj is int myTeamParsed)
            {
                myTeamProp = (myTeamParsed == 1) ? 1 : 0;
            }

            int myTeam = (myTeamProp == 0 || myTeamProp == 1)
                ? myTeamProp
                : (blueCount <= redCount ? 0 : 1);

            initial[_mySlot] = myTeam;
            args.RuntimeConfig.InitialTeamBySlot = initial;

            if (me != null)
            {
                me.SetCustomProperties(new Photon.Client.PhotonHashtable { [PLAYER_KEY_TEAM] = myTeam });
            }

            if (room.CustomProperties != null)
            {
                string finalMapStr = null;
                if (room.CustomProperties.TryGetValue(ROOM_KEY_TEAMMAP, out var mapObj) && mapObj is string mapStr)
                {
                    var map = ParseTeamMapDict(mapStr);
                    if (!map.ContainsKey(myActor) && myActor >= 0)
                    {
                        map[myActor] = myTeam;
                        finalMapStr = string.Join(";", map.Select(kv => $"{kv.Key}:{kv.Value}"));
                    }
                }
                else if (myActor >= 0)
                {
                    finalMapStr = $"{myActor}:{myTeam}";
                }

                if (!string.IsNullOrEmpty(finalMapStr))
                {
                    room.SetCustomProperties(new Photon.Client.PhotonHashtable
                    {
                        [ROOM_KEY_TEAMMAP] = finalMapStr
                    });
                }
            }

            _activeSlotCount = capacity;

            //Debug.Log($"[Order] started={room.CustomProperties?["started"]} capacity={capacity} " +
            //          $"slotOrder={string.Join(",", slotOrder)} " +
            //          $"InitialTeamBySlot={string.Join(",", args.RuntimeConfig.InitialTeamBySlot)} " +
            //          $"mySlot={_mySlot} myTeam={myTeam}");
        }

        private bool IsHostNow()
        {
            var room = Client?.CurrentRoom;
            var me = Client?.LocalPlayer;
            return room != null && me != null && room.MasterClientId == me.ActorNumber;
        }

        private static SelectedAbilities LoadSelectedFromPrefsOrDefault()
        {
            var sel = new SelectedAbilities();
            try
            {
                sel.Utility = PlayerPrefs.HasKey("AbilityPref.Utility.Enum")
                  ? (AbilityType)Enum.Parse(typeof(AbilityType), PlayerPrefs.GetString("AbilityPref.Utility.Enum"))
                  : AbilityType.Dash;
                sel.Main1 = PlayerPrefs.HasKey("AbilityPref.Main1.Enum")
                  ? (AbilityType)Enum.Parse(typeof(AbilityType), PlayerPrefs.GetString("AbilityPref.Main1.Enum"))
                  : AbilityType.Attack;
                sel.Main2 = PlayerPrefs.HasKey("AbilityPref.Main2.Enum")
                  ? (AbilityType)Enum.Parse(typeof(AbilityType), PlayerPrefs.GetString("AbilityPref.Main2.Enum"))
                  : AbilityType.Block;

                sel.IsSet = true; // force true so SpawnSystem applies
            }
            catch
            {
                sel.Utility = AbilityType.Dash;
                sel.Main1 = AbilityType.Attack;
                sel.Main2 = AbilityType.Block;
                sel.IsSet = true;
            }
            return sel;
        }

        static void EnsureSized<T>(ref T[] arr, int n) { if (arr == null) arr = new T[n]; else if (arr.Length < n) Array.Resize(ref arr, n); }

        private int GetMyTeamFromRuntime(QuantumMenuConnectArgs args)
        {
            var init = args?.RuntimeConfig?.InitialTeamBySlot;
            return (init != null && _mySlot >= 0 && _mySlot < init.Length) ? init[_mySlot] : -1; // 0=Blue,1=Red
        }

        private async Task WaitForPhaseAsync(string expected, float timeout = 5f)
        {
            var t0 = Time.realtimeSinceStartup;
            while (true)
            {
                var room = Client?.CurrentRoom;
                if (room?.CustomProperties != null &&
                    room.CustomProperties.TryGetValue(ROOM_KEY_PHASE, out var v) &&
                    v is string s && s == expected)
                {
                    return;
                }
                Client?.Service();
                if (Time.realtimeSinceStartup - t0 > timeout)
                {
                    Debug.LogWarning($"[Order] Phase '{expected}' not seen; proceeding anyway");
                    return;
                }
                await Task.Delay(25);
            }
        }

        public static class SessionReset
        {
            public static void ResetSessionStatics()
            {
                Quantum.Menu.QuantumMenuConnectionBehaviourSDK.PreventRestart = false;
                HoldLobbyGate.SuppressAutoStart = false;
            }
        }

        private SelectedAbilities ReadLocalSelectedAbilities()
        {
            var sel = new SelectedAbilities();

            try
            {
                // Load saved preferences
                if (PlayerPrefs.HasKey("AbilityPref.Utility.Enum"))
                    sel.Utility = (AbilityType)Enum.Parse(typeof(AbilityType), PlayerPrefs.GetString("AbilityPref.Utility.Enum"));
                else
                    sel.Utility = AbilityType.Dash; // fallback

                if (PlayerPrefs.HasKey("AbilityPref.Main1.Enum"))
                    sel.Main1 = (AbilityType)Enum.Parse(typeof(AbilityType), PlayerPrefs.GetString("AbilityPref.Main1.Enum"));
                else
                    sel.Main1 = AbilityType.Attack;

                if (PlayerPrefs.HasKey("AbilityPref.Main2.Enum"))
                    sel.Main2 = (AbilityType)Enum.Parse(typeof(AbilityType), PlayerPrefs.GetString("AbilityPref.Main2.Enum"));
                else
                    sel.Main2 = AbilityType.Block;

                sel.IsSet = PlayerPrefs.GetInt("AbilityPref.IsSet", 0) == 1;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Abilities] Failed to parse PlayerPrefs: {e.Message}");
                sel.Utility = AbilityType.Dash;
                sel.Main1 = AbilityType.Attack;
                sel.Main2 = AbilityType.Block;
                sel.IsSet = true;
            }

            Debug.Log($"[Abilities] Loaded from prefs: {sel.Utility}, {sel.Main1}, {sel.Main2}");
            return sel;
        }


    }

}

public static class NameUtils
{
    public static string CleanName(string nick)
    {
        if (string.IsNullOrEmpty(nick))
            return string.Empty;
        int idx = nick.IndexOf('[');
        return (idx > 0) ? nick.Substring(0, idx).Trim() : nick;
    }
}
