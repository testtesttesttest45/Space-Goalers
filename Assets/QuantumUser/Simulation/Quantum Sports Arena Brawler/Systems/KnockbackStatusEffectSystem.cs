using Photon.Deterministic;

namespace Quantum
{
    public unsafe class KnockbackStatusEffectSystem : SystemMainThreadFilter<KnockbackStatusEffectSystem.Filter>, ISignalOnKnockbackApplied, ISignalOnStatusEffectsReset
    {
        public struct Filter
        {
            public EntityRef EntityRef;
            public PlayerStatus* PlayerStatus;
            public Transform3D* Transform;
            public CharacterController3D* KCC;
        }

        public override void Update(Frame frame, ref Filter filter)
        {
            if (!filter.PlayerStatus->IsKnockbacked)
            {
                return;
            }

            PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(filter.PlayerStatus->PlayerMovementData.Id);

            FPVector3 lastRelativePosition = GetKnockbackRelativePosition(frame, filter.PlayerStatus);
            filter.PlayerStatus->KnockbackStatusEffect.DurationTimer.Tick(frame.DeltaTime);
            FPVector3 newRelativePosition = GetKnockbackRelativePosition(frame, filter.PlayerStatus);

            FPVector3 movement = newRelativePosition - lastRelativePosition;
            filter.Transform->Position += movement;

            if (filter.PlayerStatus->KnockbackStatusEffect.DurationTimer.IsRunning)
            {
                filter.PlayerStatus->KnockbackStatusEffect.KnockbackVelocity = (newRelativePosition - lastRelativePosition) / frame.DeltaTime;
            }
            else
            {
                filter.KCC->Velocity = filter.PlayerStatus->KnockbackStatusEffect.KnockbackVelocity;

                playerMovementData.UpdateKCCSettings(frame, filter.EntityRef);
            }
        }

        private FPVector3 GetKnockbackRelativePosition(Frame frame, PlayerStatus* PlayerStatus)
        {
            KnockbackStatusEffectData statusEffectData = frame.FindAsset<KnockbackStatusEffectData>(PlayerStatus->KnockbackStatusEffect.StatusEffectData.Id);

            FP normalizedTime = PlayerStatus->KnockbackStatusEffect.DurationTimer.NormalizedTime;
            FP normalizedPositionXZ = statusEffectData.KnockbackCurveXZ.Evaluate(normalizedTime);
            FP normalizedPositionY = statusEffectData.KnockbackCurveY.Evaluate(normalizedTime);
            FPVector3 relativePosition = (PlayerStatus->KnockbackStatusEffect.KnockbackDirection * statusEffectData.KnockbackDistanceXZ * normalizedPositionXZ) +
                (FPVector3.Up * statusEffectData.KnockbackDistanceY * normalizedPositionY);

            return relativePosition;
        }

        public void OnKnockbackApplied(Frame frame,
    EntityRef playerEntityRef,
    FP duration,
    FPVector3 direction,
    AssetRef<KnockbackStatusEffectData> data)
        {
            PlayerStatus* status = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);

            status->KnockbackStatusEffect.DurationTimer.Start(duration);
            status->KnockbackStatusEffect.KnockbackDirection = direction;
            status->KnockbackStatusEffect.StatusEffectData = data;

            if (status->IsHoldingBall)
                frame.Signals.OnBallDropped(status->HoldingBallEntityRef);
        }

        public void OnStatusEffectsReset(Frame frame, EntityRef playerEntityRef)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);

            if (!playerStatus->IsKnockbacked)
            {
                return;
            }

            PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(playerStatus->PlayerMovementData.Id);

            playerStatus->KnockbackStatusEffect.DurationTimer.Reset();
            playerMovementData.UpdateKCCSettings(frame, playerEntityRef);
        }
    }
}
