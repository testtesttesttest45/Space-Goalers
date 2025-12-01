namespace Quantum
{
    public partial struct AbilityInventory
    {
        public bool HasActiveAbility => ActiveAbilityInfo.ActiveAbilityIndex >= 0;

        public bool IsBlocking => Blocking;
        public bool IsThrowingBall => GetAbility(AbilityType.ThrowShort).IsActive || GetAbility(AbilityType.ThrowLong).IsActive;

        public ref Ability GetAbility(AbilityType abilityType)
        {
            return ref Abilities[(int)abilityType];
        }

        public bool TryGetActiveAbility(out Ability ability)
        {
            if (!HasActiveAbility)
            {
                ability = default;
                return false;
            }

            ability = Abilities[ActiveAbilityInfo.ActiveAbilityIndex];
            return true;
        }
    }
}
