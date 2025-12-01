using Photon.Deterministic;

namespace Quantum
{
    public unsafe partial struct QuantumDemoInputTopDown
    {
        public bool GetAbilityInputWasPressed(AbilityType abilityType)
        {
            switch (abilityType)
            {
                case AbilityType.Attack: return false;
                case AbilityType.Block: return false;
                case AbilityType.Bomb: return false;

                case AbilityType.ThrowShort: return false;
                case AbilityType.ThrowLong: return false;

                case AbilityType.Jump: return Jump.WasPressed;
                case AbilityType.Hook: return Hook.WasPressed;

                case AbilityType.Dash:
                case AbilityType.Invisibility:
                case AbilityType.Banana:
                case AbilityType.Speedster: return Use.WasPressed;

                default: return false;
            }
        }
    }
}
