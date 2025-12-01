using Photon.Deterministic;
using Quantum.Inspector;
using UnityEngine;

namespace Quantum
{
    public partial class PlayerMovementData : AssetObject
    {
        public bool FaceAimDirection = false;
        public FP DefaultRotationSpeed = 4;
        public FP QuickRotationSpeed = 15;

        public FP JumpCoyoteTime = FP._0_20;
        public FP NoMovementBraking = 5;
        public FP RespawnDuration = 5;

        public AssetRef<CharacterController3DConfig> DefaultKCCSettings;
        public AssetRef<CharacterController3DConfig> CarryingBallKCCSettings;
        public AssetRef<CharacterController3DConfig> NoMovementKCCSettings;
        [Header("KCC While Dashing")]
        public AssetRef<CharacterController3DConfig> DashingKCCSettings;


        [Header("KCC While Speedster")]
        public AssetRef<CharacterController3DConfig> SpeedsterKCCSettings;


        [Header("KCC While Slowed")]
        public AssetRef<CharacterController3DConfig> SlowedKCCSettings;

        public unsafe void UpdateKCCSettings(Frame frame, EntityRef playerEntityRef)
        {
            var ps = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);
            var inv = frame.Unsafe.GetPointer<AbilityInventory>(playerEntityRef);
            var kcc = frame.Unsafe.GetPointer<CharacterController3D>(playerEntityRef);

            CharacterController3DConfig config = null;

            CharacterController3DConfig GetOrNull(AssetRef<CharacterController3DConfig> r)
              => r.IsValid ? frame.FindAsset<CharacterController3DConfig>(r.Id) : null;

            bool slowedActive = frame.Has<SlowedStatusEffect>(playerEntityRef) &&
                                frame.Unsafe.GetPointer<SlowedStatusEffect>(playerEntityRef)->IsActive;

            bool dashActive = (int)AbilityType.Dash < inv->Abilities.Length &&
                              inv->GetAbility(AbilityType.Dash).IsDelayedOrActive;

            bool speedsterByAbility = (int)AbilityType.Speedster < inv->Abilities.Length &&
                                      inv->GetAbility(AbilityType.Speedster).IsDelayedOrActive;

            // ally boost via banana
            bool externalBoost = ps->ExternalSpeedsterActive && ps->ExternalSpeedster.IsRunning;
            bool speedsterAny = speedsterByAbility || externalBoost;

            bool blockMovement = false;
            if (inv->TryGetActiveAbility(out Ability active))
            {
                var activeData = frame.FindAsset<AbilityData>(active.AbilityData.Id);
                blockMovement = (activeData == null) || !activeData.KeepVelocity;
            }

            if (ps->IsKnockbacked)
            {
                config = GetOrNull(NoMovementKCCSettings);
            }
            else if (blockMovement)
            {
                config = GetOrNull(NoMovementKCCSettings);
            }
            else if (slowedActive)
            {
                config = GetOrNull(SlowedKCCSettings);
            }
            else if (dashActive)
            {
                config = GetOrNull(DashingKCCSettings) ?? GetOrNull(SpeedsterKCCSettings);
            }
            else if (speedsterAny)
            {
                config = GetOrNull(SpeedsterKCCSettings);
            }
            else if (ps->IsHoldingBall)
            {
                config = GetOrNull(CarryingBallKCCSettings);
            }
            else
            {
                config = GetOrNull(DefaultKCCSettings);
            }

            if (config == null)
                config = GetOrNull(DefaultKCCSettings);

            kcc->SetConfig(frame, config);
        }

    }

}
