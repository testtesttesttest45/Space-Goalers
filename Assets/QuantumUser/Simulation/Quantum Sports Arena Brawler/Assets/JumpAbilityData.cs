using Photon.Deterministic;
using Quantum.Inspector;
using System;

namespace Quantum
{
    [Serializable]
    public unsafe partial class JumpAbilityData : AbilityData
    {
        public FP JumpImpulse = 15;
        public FP JumpWithBallImpulse = 13;

        public JumpAbilityData()
        {
            AllowConcurrent = true;

            KeepVelocity = true;
            Delay = FP._0;
        }

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            var state = base.UpdateAbility(frame, entityRef, ref ability);

            if (state.IsActiveStartTick)
            {
                var ps = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
                var kcc = frame.Unsafe.GetPointer<CharacterController3D>(entityRef);
                kcc->Jump(frame, true, ps->IsHoldingBall ? JumpWithBallImpulse : JumpImpulse);
            }

            return state;
        }

        public override unsafe bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            bool activated = false;

            if (playerStatus->JumpCoyoteTimer.IsRunning)
            {
                activated = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);
                if (activated)
                {
                    playerStatus->JumpCoyoteTimer.Reset();
                    frame.Events.OnPlayerJumped(entityRef);
                }
            }
            else if (playerStatus->HasAirJump)
            {
                activated = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);
                if (activated)
                {
                    playerStatus->HasAirJump = false;
                    frame.Events.OnPlayerAirJumped(entityRef);
                }
            }

            return activated;
        }
    }
}
