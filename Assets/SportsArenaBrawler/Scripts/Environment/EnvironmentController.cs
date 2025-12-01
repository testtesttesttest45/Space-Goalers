using Quantum;
using UnityEngine;

public class EnvironmentController : MonoBehaviour
{
    [Header("Indicator Prefabs")]
    [SerializeField] private GameObject _attackGoalIndicatorPrefab;
    [SerializeField] private GameObject _defendGoalIndicatorPrefab;

    [Header("Hierarchy")]
    [SerializeField] private Transform _blueTeamGoalIndicatorParent;
    [SerializeField] private Transform _redTeamGoalIndicatorParent;
    [SerializeField] private GameObject _ballGuideEffect;

    public static EnvironmentController Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        QuantumEvent.Subscribe<EventOnGameStarting>(this, OnGameStarting);
        QuantumEvent.Subscribe<EventOnPlayerCaughtBall>(this, OnPlayerCaughtBall);
    }

    public void InitializeTeamIndicators(PlayerTeam playerTeam, int layer)
    {
        GameObject defendEffect;
        GameObject attackEffect;

        if (playerTeam == PlayerTeam.Blue)
        {
            attackEffect = Instantiate(_attackGoalIndicatorPrefab, _redTeamGoalIndicatorParent);
            defendEffect = Instantiate(_defendGoalIndicatorPrefab, _blueTeamGoalIndicatorParent);
        }
        else
        {
            attackEffect = Instantiate(_attackGoalIndicatorPrefab, _blueTeamGoalIndicatorParent);
            defendEffect = Instantiate(_defendGoalIndicatorPrefab, _redTeamGoalIndicatorParent);
        }

        defendEffect.SetLayerWithChildren(layer);
        attackEffect.SetLayerWithChildren(layer);
    }

    private void OnGameStarting(EventOnGameStarting eventData)
    {
        if (eventData.IsFirst)
        {
            _ballGuideEffect.SetActive(true);
        }
    }

    private void OnPlayerCaughtBall(EventOnPlayerCaughtBall eventData)
    {
        _ballGuideEffect.SetActive(false);
    }
}
