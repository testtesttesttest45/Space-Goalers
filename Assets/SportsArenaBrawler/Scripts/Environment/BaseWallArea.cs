using Quantum;
using UnityEngine;

public unsafe class BaseWallArea : MonoBehaviour
{
    private static readonly int IS_VISIBLE_ANIM_HASH = Animator.StringToHash("Is Visible");

    [SerializeField] private Animator _animator;

    private void Start()
    {
        QuantumEvent.Subscribe<EventOnGameInitializing>(this, OnGameInitializing);
        QuantumEvent.Subscribe<EventOnGameStarting>(this, OnGameStarting);
        QuantumEvent.Subscribe<EventOnGameRunning>(this, OnGameRunning);

        Frame frame = QuantumRunner.Default?.Game?.Frames.Predicted;
        if (frame != null)
        {
            bool isVisible = frame.Global->GameState == GameState.Initializing || frame.Global->GameState == GameState.Starting;
            _animator.SetBool(IS_VISIBLE_ANIM_HASH, isVisible);
        }
    }

    private void OnGameInitializing(EventOnGameInitializing eventData)
    {
        _animator.SetBool(IS_VISIBLE_ANIM_HASH, true);
    }

    private void OnGameStarting(EventOnGameStarting eventData)
    {
        _animator.SetBool(IS_VISIBLE_ANIM_HASH, true);
    }

    private void OnGameRunning(EventOnGameRunning eventData)
    {
        _animator.SetBool(IS_VISIBLE_ANIM_HASH, false);
    }
}
