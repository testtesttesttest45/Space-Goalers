namespace Quantum
{
    public unsafe class GoalSystem : SystemSignalsOnly, ISignalOnTrigger3D
    {
        public void OnTrigger3D(Frame frame, TriggerInfo3D info)
        {
            if (!info.IsStatic)
            {
                return;
            }

            if (frame.Global->GameState != GameState.Running)
            {
                return;
            }

            if (frame.TryFindAsset(info.StaticData.Asset, out GoalAreaColliderData goalAreaColliderData))
            {
                PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(info.Entity);

                if (!playerStatus->IsHoldingBall)
                {
                    return;
                }

                if (playerStatus->PlayerTeam == goalAreaColliderData.PlayerTeam)
                {
                    return;
                }

                frame.Signals.OnGoalScored(info.Entity, playerStatus->PlayerTeam);
            }
        }
    }
}
