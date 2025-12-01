using Quantum;
using Quantum.Menu;
using TMPro;
using UnityEngine;

public class SportsArenaBrawlerLocalPlayerController : MonoBehaviour
{
  public QuantumMenuUIController MenuUIController;

    [SerializeField] private AssetRef<Quantum.EntityPrototype> _characterPrototype;

    [SerializeField] private TMP_Dropdown _playerCountDropdown;

  public void OnDropdownChanged()
  {
    SetupLocalPlayers(GetLastSelectedLocalPlayersCount());
  }

  private void SetupLocalPlayers(int localPlayersCount)
  {
    MenuUIController.ConnectArgs.RuntimePlayers = new RuntimePlayer[localPlayersCount];
    for (int i = 0; i < localPlayersCount; i++)
    {
      MenuUIController.ConnectArgs.RuntimePlayers[i] = new RuntimePlayer();
      MenuUIController.ConnectArgs.RuntimePlayers[i].PlayerAvatar = _characterPrototype;
      MenuUIController.ConnectArgs.RuntimePlayers[i].PlayerNickname = $"Local player {i}";
    }
  }

  public int GetLastSelectedLocalPlayersCount()
  {
    return _playerCountDropdown.value + 1;
  }
}