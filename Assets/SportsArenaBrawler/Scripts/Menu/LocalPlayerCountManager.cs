using Photon.Client;
using Photon.Realtime;
using Quantum.Menu;
using UnityEngine;
using UnityEngine.Serialization;

public class LocalPlayerCountManager : MonoBehaviour, IInRoomCallbacks
{
  public const string LOCAL_PLAYERS_PROP_KEY = "LP";
  public const string TOTAL_PLAYERS_PROP_KEY = "C0";
  public static readonly TypedLobby SQL_LOBBY = new TypedLobby("customSqlLobby", LobbyType.Sql);
  
  [SerializeField]private SportsArenaBrawlerLocalPlayerController _menuController;
  private QuantumMenuConnectionBehaviour _connection => _menuController.MenuUIController.Connection;

  private void UpdateLocalPlayersCount()
  {
    _connection.Client?.LocalPlayer.SetCustomProperties(new PhotonHashtable()
    {
      { LOCAL_PLAYERS_PROP_KEY, _menuController.GetLastSelectedLocalPlayersCount() }
    });
  }

  private void OnEnable()
  {
    _connection.Client?.AddCallbackTarget(this);
    UpdateLocalPlayersCount();
  }

  private void OnDisable()
  {
    _connection.Client?.RemoveCallbackTarget(this);
  }

  /// <summary>
  /// Update the room properties
  /// </summary>
  private void UpdateRoomTotalPlayers()
  {
    if (_connection != null && _connection.Client.InRoom && _connection.Client.LocalPlayer.IsMasterClient)
    {
      int totalPlayers = 0;
      foreach (var player in _connection.Client.CurrentRoom.Players.Values)
      {
        if (player.CustomProperties.TryGetValue(LOCAL_PLAYERS_PROP_KEY, out var localPlayersCount))
        {
          totalPlayers += (int)localPlayersCount;
        }
      }

      _connection.Client.CurrentRoom.SetCustomProperties(new PhotonHashtable
      {
        { TOTAL_PLAYERS_PROP_KEY, totalPlayers }
      });
    }
  }

  public void OnPlayerEnteredRoom(Player newPlayer)
  {
    Debug.Log("OnPlayerEnteredRoom");
    UpdateLocalPlayersCount();
  }

  public void OnPlayerLeftRoom(Player otherPlayer)
  {
    UpdateRoomTotalPlayers();
  }

  public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
  {
    if (propertiesThatChanged.TryGetValue(TOTAL_PLAYERS_PROP_KEY, out object totalPlayersCount))
    {
      Debug.Log($"Total players in room: {totalPlayersCount}");
    }
  }

  public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
  {
    if (changedProps.TryGetValue(LOCAL_PLAYERS_PROP_KEY, out object localPlayersCount))
    {
      UpdateRoomTotalPlayers();
    }
  }

  public void OnMasterClientSwitched(Player newMasterClient)
  {
  }
}