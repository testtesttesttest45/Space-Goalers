using Photon.Deterministic;

namespace Quantum
{
    public unsafe class StunStatusEffectSystem : SystemMainThreadFilter<StunStatusEffectSystem.Filter>, ISignalOnStunApplied, ISignalOnStatusEffectsReset
    {
        public struct Filter
        {
            public EntityRef EntityRef;
            public PlayerStatus* PlayerStatus;
        }

        public override void Update(Frame frame, ref Filter filter)
        {
            if (!filter.PlayerStatus->IsStunned)
            {
                return;
            }

            filter.PlayerStatus->StunStatusEffect.DurationTimer.Tick(frame.DeltaTime);
        }

        public void OnStunApplied(Frame frame, EntityRef playerEntityRef, FP duration)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);

            playerStatus->StunStatusEffect.DurationTimer.Start(duration);

            if (playerStatus->IsHoldingBall)
            {
                frame.Signals.OnBallDropped(playerStatus->HoldingBallEntityRef);
            }

            frame.Events.OnPlayerStunned(playerEntityRef);
        }

        public void OnStatusEffectsReset(Frame frame, EntityRef playerEntityRef)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);

            if (!playerStatus->IsStunned)
            {
                return;
            }

            playerStatus->StunStatusEffect.DurationTimer.Reset();
        }
    }
}
