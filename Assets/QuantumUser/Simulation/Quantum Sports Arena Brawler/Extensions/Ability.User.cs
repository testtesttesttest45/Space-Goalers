using Photon.Deterministic;
using System;

namespace Quantum
{
    public unsafe partial struct Ability
    {
        public struct AbilityState
        {
            public bool IsDelayed;
            public bool IsActive;
            public bool IsActiveStartTick;
            public bool IsActiveEndTick;
            public bool IsOnCooldown;
        }

        public bool HasBufferedInput => InputBufferTimer.IsRunning;
        public bool IsDelayed => DelayTimer.IsRunning;
        public bool IsActive => DurationTimer.IsRunning;
        public bool IsDelayedOrActive => IsDelayed || IsActive;
        public bool IsOnCooldown => CooldownTimer.IsRunning;

        public bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerRef playerRef)
        {
            if (IsOnCooldown)
                return false;

            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
            if (playerStatus->IsIncapacitated)
                return false;

            AbilityInventory* abilityInventory = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);
            AbilityData thisData = frame.FindAsset<AbilityData>(AbilityData.Id);

            if (abilityInventory->HasActiveAbility)
            {
                int otherIdx = abilityInventory->ActiveAbilityInfo.ActiveAbilityIndex;
                if (otherIdx >= 0 && otherIdx < abilityInventory->Abilities.Length)
                {
                    ref Ability other = ref abilityInventory->Abilities[otherIdx];
                    AbilityData otherData = frame.FindAsset<AbilityData>(other.AbilityData.Id);

                    bool bothAllow =
                        (thisData != null && thisData.AllowConcurrent) &&
                        (otherData != null && otherData.AllowConcurrent);

                    if (!bothAllow)
                    {
                        return false;
                    }
                }
            }

            InputBufferTimer.Reset();
            DelayTimer.Start(thisData?.Delay ?? FP._0);
            if (thisData == null || !thisData.StartCooldownAfterDelay)
            {
                CooldownTimer.Start(thisData?.Cooldown ?? FP._0);
            }

            Transform3D* transform = frame.Unsafe.GetPointer<Transform3D>(entityRef);
            CharacterController3D* kcc = frame.Unsafe.GetPointer<CharacterController3D>(entityRef);

            if (thisData == null || !thisData.AllowConcurrent)
            {
                abilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = (int)AbilityType;
                abilityInventory->ActiveAbilityInfo.CastDirection = GetCastDirection(frame, playerRef, thisData, transform);
                abilityInventory->ActiveAbilityInfo.CastRotation = FPQuaternion.LookRotation(abilityInventory->ActiveAbilityInfo.CastDirection);
                abilityInventory->ActiveAbilityInfo.CastVelocity = kcc->Velocity;
            }
            else
            {
                abilityInventory->ActiveAbilityInfo.CastDirection = GetCastDirection(frame, playerRef, thisData, transform);
                abilityInventory->ActiveAbilityInfo.CastRotation = FPQuaternion.LookRotation(abilityInventory->ActiveAbilityInfo.CastDirection);
                abilityInventory->ActiveAbilityInfo.CastVelocity = kcc->Velocity;
            }

            PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(playerStatus->PlayerMovementData.Id);
            playerMovementData.UpdateKCCSettings(frame, entityRef);

            return true;
        }

        private FPVector3 GetCastDirection(Frame frame, PlayerRef playerRef, AbilityData abilityData, Transform3D* transform)
        {
            QuantumDemoInputTopDown input = *frame.GetPlayerInput(playerRef);

            if ((abilityData.CastDirectionType & AbilityCastDirectionType.Aim) == AbilityCastDirectionType.Aim && input.AimDirection != default)
            {
                return input.AimDirection.XOY.Normalized;
            }
            else if ((abilityData.CastDirectionType & AbilityCastDirectionType.Movement) == AbilityCastDirectionType.Movement && input.MoveDirection != default)
            {
                return input.MoveDirection.XOY.Normalized;
            }
            else if ((abilityData.CastDirectionType & AbilityCastDirectionType.FacingDirection) == AbilityCastDirectionType.FacingDirection)
            {
                return transform->Forward;
            }
            else
            {
                throw new ArgumentException($"Unknown {nameof(AbilityCastDirectionType)}: {abilityData.CastDirectionType}", nameof(abilityData.CastDirectionType));
            }
        }

        public AbilityState Update(Frame frame, EntityRef entityRef)
        {
            AbilityState state = new AbilityState();

            InputBufferTimer.Tick(frame.DeltaTime);
            CooldownTimer.Tick(frame.DeltaTime);

            state.IsOnCooldown = IsOnCooldown;

            if (IsDelayedOrActive)
            {
                PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);

                if (playerStatus->IsIncapacitated)
                {
                    StopAbility(frame, entityRef);

                    return state;
                }

                FP delayTimeLeft = DelayTimer.TimeLeft;

                if (IsDelayed)
                {
                    DelayTimer.Tick(frame.DeltaTime);

                    if (DelayTimer.IsRunning)
                    {
                        state.IsDelayed = true;
                    }
                    else
                    {
                        state.IsActiveStartTick = true;

                        AbilityData abilityData = frame.FindAsset<AbilityData>(AbilityData.Id);

                        DurationTimer.Start(abilityData.Duration);
                        if (abilityData.StartCooldownAfterDelay)
                        {
                            CooldownTimer.Start(abilityData.Cooldown);
                        }
                    }
                }

                if (IsActive)
                {
                    state.IsActive = true;

                    DurationTimer.Tick(frame.DeltaTime - delayTimeLeft);

                    if (DurationTimer.IsDone)
                    {
                        state.IsActiveEndTick = true;

                        StopAbility(frame, entityRef);
                    }
                }
            }

            return state;
        }

        public void BufferInput(Frame frame)
        {
            AbilityData abilityData = frame.FindAsset<AbilityData>(AbilityData.Id);

            InputBufferTimer.Start(abilityData.InputBuffer);
        }

        public void StopAbility(Frame frame, EntityRef entityRef)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
            AbilityInventory* abilityInventory = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);
            PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(playerStatus->PlayerMovementData.Id);

            if (abilityInventory->ActiveAbilityInfo.ActiveAbilityIndex == (int)AbilityType)
            {
                abilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
            }

            DelayTimer.Reset();
            DurationTimer.Reset();

            playerMovementData.UpdateKCCSettings(frame, entityRef);
        }

        public void ResetCooldown()
        {
            CooldownTimer.Reset();
        }
    }
}
