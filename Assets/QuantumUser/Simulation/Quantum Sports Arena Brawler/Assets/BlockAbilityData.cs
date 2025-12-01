using System;

namespace Quantum
{
    [Serializable]
    public unsafe partial class BlockAbilityData : AbilityData
    {

        public override bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {

            bool activated = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);

            if (activated)
            {
                AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

                inv->Blocking = true;

                frame.Events.OnPlayerBlocked(entityRef);
            }

            return activated;
        }

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            var state = base.UpdateAbility(frame, entityRef, ref ability);

            bool endingNow = !ability.IsDelayedOrActive;

            if (endingNow)
            {
                AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

                inv->Blocking = false;
            }

            return state;
        }
    }
}
