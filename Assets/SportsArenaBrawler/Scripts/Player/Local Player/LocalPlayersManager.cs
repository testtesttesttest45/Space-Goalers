using System.Collections.Generic;
using UnityEngine;
using Quantum;

public class LocalPlayersManager : MonoBehaviour
{
    public static LocalPlayersManager Instance { get; private set; }

    [SerializeField] private LocalPlayersConfig[] _localPlayersConfigPrefabs;
    [SerializeField] private Camera _temporaryCamera;

    private Dictionary<int, LocalPlayerAccess> _localPlayerAccessByPlayerIndices = new Dictionary<int, LocalPlayerAccess>();

    public Dictionary<int, LocalPlayerAccess>.ValueCollection LocalPlayerAccessCollection
    {
        get
        {
            if (_localPlayerAccessByPlayerIndices.Count == 0)
            {
                Initialize();
            }

            return _localPlayerAccessByPlayerIndices.Values;
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    public LocalPlayerAccess InitializeLocalPlayer(PlayerViewController playerViewController)
    {
        int key = (int)playerViewController.PlayerRef; // ensure it is PlayerRef index
        var access = GetLocalPlayerAccess(key);

        if (access == null)
        {
            // Rebuild once if we missed timing
            Debug.LogWarning($"[LPM] No LocalPlayerAccess for PlayerRef={key}. Reinitializing.");
            _localPlayerAccessByPlayerIndices.Clear();
            Initialize();
            _localPlayerAccessByPlayerIndices.TryGetValue(key, out access);
        }

        if (access == null)
        {
            Debug.LogError($"[LPM] Still no LocalPlayerAccess for PlayerRef={key}. " +
                           $"Local players: [{string.Join(",", QuantumRunner.Default.Game.GetLocalPlayers())}]");
            return null;
        }

        access.InitializeLocalPlayer(playerViewController);
        return access;
    }

    public LocalPlayerAccess GetLocalPlayerAccess(int playerIndex)
    {
        if (_localPlayerAccessByPlayerIndices.Count == 0)
            Initialize();

        if (!_localPlayerAccessByPlayerIndices.TryGetValue(playerIndex, out var access))
        {
            Debug.LogWarning($"[LPM] GetLocalPlayerAccess miss for PlayerRef={playerIndex}. " +
                             $"Keys= [{string.Join(",", _localPlayerAccessByPlayerIndices.Keys)}]");
        }
        return access;
    }

    private void Initialize()
    {
        var localPlayerIndices = QuantumRunner.Default.Game.GetLocalPlayers();
        Debug.Log($"[LPM] Initialize. LocalPlayers=[{string.Join(",", localPlayerIndices)}]");

        if (localPlayerIndices.Count == 0) return;

        var prefabIdx = Mathf.Clamp(localPlayerIndices.Count - 1, 0, _localPlayersConfigPrefabs.Length - 1);
        var localPlayersConfig = Instantiate(_localPlayersConfigPrefabs[prefabIdx], transform);

        for (int i = 0; i < localPlayerIndices.Count; i++)
        {
            var access = localPlayersConfig.GetLocalPlayerAccess(i);
            access.IsMainLocalPlayer = (i == 0);
            _localPlayerAccessByPlayerIndices[localPlayerIndices[i]] = access;

            Debug.Log($"[LPM] Map PlayerRef={localPlayerIndices[i]} -> UI Slot {i}  (Main={access.IsMainLocalPlayer})");
        }

        if (_temporaryCamera) Destroy(_temporaryCamera.gameObject);
    }
}
