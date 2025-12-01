using UnityEngine;

public class LocalPlayersConfig : MonoBehaviour
{
    [SerializeField] private LocalPlayerAccess[] _localPlayerAccess;

    public LocalPlayerAccess GetLocalPlayerAccess(int playerIndex)
    {
        return _localPlayerAccess[playerIndex];
    }
}
