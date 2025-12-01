using Photon.Deterministic;

namespace Quantum
{
    public unsafe class MovementSystem : SystemMainThreadFilter<MovementSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef EntityRef;
            public PlayerStatus* PlayerStatus;
            public Transform3D* Transform;
            public CharacterController3D* KCC;
            public AbilityInventory* AbilityInventory;
        }

        public override void Update(Frame frame, ref Filter filter)
        {

            if (filter.PlayerStatus->ExternalSpeedsterActive)
            {
                filter.PlayerStatus->ExternalSpeedster.Tick(frame.DeltaTime);
                if (!filter.PlayerStatus->ExternalSpeedster.IsRunning)
                {
                    filter.PlayerStatus->ExternalSpeedsterActive = false;
                    frame.Events.OnSpeedsterEnded(filter.EntityRef);
                }
            }
            if (frame.Has<SlowedStatusEffect>(filter.EntityRef))
            {
                var slow = frame.Unsafe.GetPointer<SlowedStatusEffect>(filter.EntityRef);
                if (slow->IsActive)
                {
                    slow->Duration.Tick(frame.DeltaTime);
                    if (!slow->Duration.IsRunning)
                        slow->IsActive = false;
                }
            }
            PlayerMovementData movementData = frame.FindAsset<PlayerMovementData>(filter.PlayerStatus->PlayerMovementData.Id);
            movementData.UpdateKCCSettings(frame, filter.EntityRef);

            QuantumDemoInputTopDown input = *frame.GetPlayerInput(filter.PlayerStatus->PlayerRef);

            bool wasGrounded = filter.KCC->Grounded;
            bool hasActiveAbility = filter.AbilityInventory->TryGetActiveAbility(out Ability activeAbility);

            AbilityData activeAbilityData = null;
            if (hasActiveAbility)
            {
                activeAbilityData = frame.FindAsset<AbilityData>(activeAbility.AbilityData.Id);
            }

            bool keepVelocity = hasActiveAbility && (activeAbilityData != null) && activeAbilityData.KeepVelocity;

            if ((!keepVelocity && hasActiveAbility) || filter.PlayerStatus->IsKnockbacked)
            {
                filter.KCC->Velocity = FPVector3.Lerp(
                    filter.KCC->Velocity,
                    FPVector3.Zero,
                    movementData.NoMovementBraking * frame.DeltaTime
                );
            }

            bool blockByAbility = hasActiveAbility && !keepVelocity;


            FPVector3 movementDirection;
            if (filter.PlayerStatus->IsIncapacitated || blockByAbility)
            {
                movementDirection = FPVector3.Zero;
            }
            else
            {
                movementDirection = input.MoveDirection.XOY;
                if (movementDirection.SqrMagnitude > FP._1)
                    movementDirection = movementDirection.Normalized;
            }

            if (!filter.PlayerStatus->IsRespawning)
            {
                filter.KCC->Move(frame, filter.EntityRef, movementDirection);
            }

            FP rotationSpeed;
            FPQuaternion currentRotation = filter.Transform->Rotation;
            FPQuaternion targetRotation = currentRotation;

            if (filter.PlayerStatus->IsKnockbacked)
            {
                rotationSpeed = movementData.QuickRotationSpeed;
                targetRotation = FPQuaternion.LookRotation(-filter.PlayerStatus->KnockbackStatusEffect.KnockbackDirection);
            }
            else if (hasActiveAbility && activeAbilityData != null && activeAbilityData.FaceCastDirection) // <-- guard added
            {
                rotationSpeed = movementData.QuickRotationSpeed;
                targetRotation = FPQuaternion.LookRotation(filter.AbilityInventory->ActiveAbilityInfo.CastDirection);
            }
            else
            {
                rotationSpeed = movementData.DefaultRotationSpeed;

                if (movementData.FaceAimDirection && input.AimDirection != default)
                    targetRotation = FPQuaternion.LookRotation(input.AimDirection.XOY);
                else if (movementDirection != default)
                    targetRotation = FPQuaternion.LookRotation(movementDirection);
            }

            filter.Transform->Rotation = FPQuaternion.Slerp(currentRotation, targetRotation, rotationSpeed * frame.DeltaTime);

            bool activeAllowsConcurrent = hasActiveAbility && (activeAbilityData != null && activeAbilityData.AllowConcurrent);

            if (filter.KCC->Grounded)
            {
                bool jumpOnCooldown = filter.AbilityInventory->GetAbility(AbilityType.Jump).IsOnCooldown;

                if (!jumpOnCooldown && (!hasActiveAbility || activeAllowsConcurrent))
                {
                    filter.PlayerStatus->HasAirJump = true;
                    filter.PlayerStatus->JumpCoyoteTimer.Start(movementData.JumpCoyoteTime);

                    if (!wasGrounded)
                        frame.Events.OnPlayerLanded(filter.EntityRef);
                }
            }
            else
            {
                filter.PlayerStatus->JumpCoyoteTimer.Tick(frame.DeltaTime);
            }
        }
    
    }
}
