using Quantum;
using System.Collections.Generic;

public class PlayersManager : QuantumCallbacks
{
    public static PlayersManager Instance { get; private set; }

    private Dictionary<EntityRef, PlayerViewController> _playersByEntityRefs = new Dictionary<EntityRef, PlayerViewController>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        QuantumEvent.Subscribe<EventOnPlayerJumped>(this, OnPlayerJumped);
        QuantumEvent.Subscribe<EventOnPlayerAirJumped>(this, OnPlayerAirJumped);
        QuantumEvent.Subscribe<EventOnPlayerLanded>(this, OnPlayerLanded);

        QuantumEvent.Subscribe<EventOnPlayerDashed>(this, OnPlayerDashed);

        QuantumEvent.Subscribe<EventOnPlayerAttacked>(this, OnPlayerAttacked);
        QuantumEvent.Subscribe<EventOnPlayerBlocked>(this, OnPlayerBlocked);

        QuantumEvent.Subscribe<EventOnPlayerHit>(this, OnPlayerHit);
        QuantumEvent.Subscribe<EventOnPlayerBlockHit>(this, OnPlayerBlockHit);

        QuantumEvent.Subscribe<EventOnPlayerCaughtBall>(this, OnPlayerCaughtBall);
        QuantumEvent.Subscribe<EventOnPlayerThrewBall>(this, OnPlayerThrewBall);

        QuantumEvent.Subscribe<EventOnPlayerStunned>(this, OnPlayerStunned);

        QuantumEvent.Subscribe<EventOnGoalScored>(this, OnGoalScored);
        QuantumEvent.Subscribe<EventOnPlayerEnteredVoid>(this, OnPlayerEnteredVoid);

        QuantumEvent.Subscribe<EventOnPlayerHookshot>(this, OnPlayerHookshot);
        QuantumEvent.Subscribe<EventOnInvisibilityActivated>(this, OnInvisibilityActivated);
        QuantumEvent.Subscribe<EventOnSpeedsterActivated>(this, OnSpeedsterActivated);
        QuantumEvent.Subscribe<EventOnSpeedsterEnded>(this, OnSpeedsterEnded);
        QuantumEvent.Subscribe<EventOnPlayerHookHit>(this, OnPlayerHookHit);
        QuantumEvent.Subscribe<EventOnInvisibilityEnded>(this, OnInvisibilityEnded);
        QuantumEvent.Subscribe<EventOnBananaConsumed>(this, OnBananaConsumed);
        QuantumEvent.Subscribe<EventOnBananaActivated>(this, OnBananaActivated);
        QuantumEvent.Subscribe<EventOnBombCast>(this, OnBombCast);
    }

    private void OnPlayerHookshot(EventOnPlayerHookshot eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerHookshot(eventData);
        }
    }

    private void OnInvisibilityActivated(EventOnInvisibilityActivated eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnInvisibilityActivated(eventData);
        }
    }

    private void OnBananaActivated(EventOnBananaActivated eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnBananaActivated(eventData);
        }
    }

    private void OnSpeedsterActivated(EventOnSpeedsterActivated eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnSpeedsterActivated(eventData);
        }
    }

    private void OnSpeedsterEnded(EventOnSpeedsterEnded e)
    {
        if (_playersByEntityRefs.TryGetValue(e.PlayerEntityRef, out var player))
        {
            player.OnSpeedsterEnded(e);
        }
    }

    public void OnPlayerJumped(EventOnPlayerJumped eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerJumped(eventData);
        }
    }

    public void OnPlayerAirJumped(EventOnPlayerAirJumped eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerAirJumped(eventData);
        }
    }

    public void OnPlayerLanded(EventOnPlayerLanded eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerLanded(eventData);
        }
    }

    public void OnPlayerDashed(EventOnPlayerDashed eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerDashed(eventData);
        }
    }

    public void OnPlayerAttacked(EventOnPlayerAttacked eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerAttacked(eventData);
        }
    }

    public void OnPlayerBlocked(EventOnPlayerBlocked eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerBlocked(eventData);
        }
    }

    public void OnPlayerHit(EventOnPlayerHit eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerHit(eventData);
        }
    }

    public void OnPlayerBlockHit(EventOnPlayerBlockHit eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerBlockHit(eventData);
        }
    }

    public void OnPlayerCaughtBall(EventOnPlayerCaughtBall eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerCaughtBall(eventData);
        }
    }

    public void OnPlayerThrewBall(EventOnPlayerThrewBall eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerThrewBall(eventData);
        }
    }

    private void OnPlayerStunned(EventOnPlayerStunned eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerStunned(eventData);
        }
    }

    private void OnGoalScored(EventOnGoalScored eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnGoalScored(eventData);
        }
    }

    private void OnPlayerEnteredVoid(EventOnPlayerEnteredVoid eventData)
    {
        if (_playersByEntityRefs.TryGetValue(eventData.PlayerEntityRef, out PlayerViewController player))
        {
            player.OnPlayerEnteredVoid(eventData);
        }
    }

    public void RegisterPlayer(QuantumGame game, PlayerViewController player)
    {
        _playersByEntityRefs.Add(player.EntityView.EntityRef, player);

        if (game.PlayerIsLocal(player.PlayerRef))
        {
            LocalPlayerAccess playerAccess = LocalPlayersManager.Instance.InitializeLocalPlayer(player);

            foreach (var currentPlayer in _playersByEntityRefs.Values)
            {
                bool isLocalPlayer = currentPlayer.PlayerRef == player.PlayerRef;
                currentPlayer.InitializeTeamIndicators(isLocalPlayer, playerAccess.CameraController.VirtualCamera.gameObject.layer);
            }

            EnvironmentController.Instance.InitializeTeamIndicators(player.PlayerTeam, playerAccess.CameraController.VirtualCamera.gameObject.layer);
        }

        foreach (var localPlayerAccess in LocalPlayersManager.Instance.LocalPlayerAccessCollection)
        {
            bool isCorrespondingLocalPlayer = player.PlayerRef == localPlayerAccess.LocalPlayer?.PlayerRef;
            localPlayerAccess.CameraController.AddPlayerTransform(player.transform, isCorrespondingLocalPlayer);

            if (!isCorrespondingLocalPlayer && localPlayerAccess.LocalPlayer != null)
            {
                player.InitializeTeamIndicators(false, localPlayerAccess.CameraController.VirtualCamera.gameObject.layer);
            }
        }
    }

    public void DeregisterPlayer(PlayerViewController player)
    {
        _playersByEntityRefs.Remove(player.EntityView.EntityRef);

        foreach (var localPlayerAccess in LocalPlayersManager.Instance.LocalPlayerAccessCollection)
        {
            localPlayerAccess.CameraController.RemoveTransform(player.transform);
        }
    }

    public PlayerViewController GetPlayer(EntityRef playerEntityRef)
    {
        return _playersByEntityRefs[playerEntityRef];
    }

    public void RegisterBall(BallViewController ball)
    {
        foreach (var localPlayerAccess in LocalPlayersManager.Instance.LocalPlayerAccessCollection)
        {
            localPlayerAccess.CameraController.AddBallTransform(ball.transform);
        }
    }

    public void DeregisterBall(BallViewController ball)
    {
        foreach (var localPlayerAccess in LocalPlayersManager.Instance.LocalPlayerAccessCollection)
        {
            localPlayerAccess.CameraController.RemoveTransform(ball.transform);
        }
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
        if (Instance == this) Instance = null;
        _playersByEntityRefs.Clear();
    }

    private void OnPlayerHookHit(EventOnPlayerHookHit e)
    {
        if (_playersByEntityRefs.TryGetValue(e.Attacker, out var player))
        {
            player.OnPlayerHookHit(e);
        }
    }
    private void OnInvisibilityEnded(EventOnInvisibilityEnded e)
    {
        if (_playersByEntityRefs.TryGetValue(e.PlayerEntityRef, out var player))
        {
            player.OnInvisibilityEnded(e);
        }
    }
    private void OnBananaConsumed(EventOnBananaConsumed e)
    {
        if (_playersByEntityRefs.TryGetValue(e.PlayerEntityRef, out var player))
        {
            player.OnBananaConsumed(e);
        }
    }
    private void OnBombCast(EventOnBombCast e)
    {
        if (_playersByEntityRefs.TryGetValue(e.PlayerEntityRef, out var player))
        {
            player.OnBombCast(e);
        }
    }

}
