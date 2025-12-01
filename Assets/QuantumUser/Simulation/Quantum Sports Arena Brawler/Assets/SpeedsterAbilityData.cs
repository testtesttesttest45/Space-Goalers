using Photon.Deterministic;
using Quantum.Inspector;
using System;

namespace Quantum
{
    /// Speedster: brief movespeed boost. No physics changes here;
    /// movement system swaps to a faster KCC config while active.
    [Serializable]
    public unsafe partial class SpeedsterAbilityData : AbilityData
    {
        public SpeedsterAbilityData()
        {
            // sensible defaults; tune in the asset
            Duration = FP._1_50;   // 1.5s
            KeepVelocity = true;   // IMPORTANT: don't block movement
            FaceCastDirection = false;
            Delay = FP._0;
            AllowConcurrent = true;
        }

        public override bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            // standard buffered activation
            bool activated = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);
            if (activated)
            {
                frame.Events.OnSpeedsterActivated(entityRef);
            }
            return activated;
        }

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            var state = base.UpdateAbility(frame, entityRef, ref ability);

            if (state.IsActiveEndTick)
            {
                frame.Events.OnSpeedsterEnded(entityRef);
            }

            return state;
        }
    }
}
