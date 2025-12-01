namespace Quantum
{
  using Photon.Realtime;
  using Quantum.Menu;
  using UnityEngine;

  public class SportsArenaBrawlerMenuConnectionBehaviourSDK : QuantumMenuConnectionBehaviourSDK
  {
    /// <summary>
    /// The manager responsible to adjusts the lobby for multiple local players.
    /// </summary>
    [SerializeField]
    private SportsArenaBrawlerLocalPlayerController _localPlayersCountSelector;

    protected override void OnConnect(QuantumMenuConnectArgs connectArgs, ref MatchmakingArguments args)
    {
      args.RandomMatchingType = MatchmakingMode.FillRoom;
      args.Lobby = LocalPlayerCountManager.SQL_LOBBY;
      args.CustomLobbyProperties = new string[] { LocalPlayerCountManager.TOTAL_PLAYERS_PROP_KEY };
      args.SqlLobbyFilter = $"{LocalPlayerCountManager.TOTAL_PLAYERS_PROP_KEY} <= {Input.MAX_COUNT - _localPlayersCountSelector.GetLastSelectedLocalPlayersCount()}";
    }
  }
}
