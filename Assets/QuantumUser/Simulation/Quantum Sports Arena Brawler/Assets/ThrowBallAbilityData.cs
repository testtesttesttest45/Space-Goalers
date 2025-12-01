using Photon.Deterministic;
using Quantum.Inspector;
using System;

namespace Quantum
{
    [Serializable]
    public unsafe partial class ThrowBallAbilityData : AbilityData
    {
        public bool IsLongThrow;

        public FPVector3 ThrowLocalPosition = new FPVector3(0, FP._0_50, 0);

        public FPVector3 ThrowImpulse = new FPVector3(0, 4, 8);
        public FP ThrowImpulseOffsetY = 1;
        public FP ThrowGravityChangeDuration = 1;

        public ThrowBallAbilityData()
        {
            Delay = FP._0;
            Duration = FP._0_10;
            KeepVelocity = true;
            FaceCastDirection = true;
            AllowConcurrent = true;
            Cooldown = FP._0_25;

            CastDirectionType = AbilityCastDirectionType.Aim | AbilityCastDirectionType.FacingDirection;
        }

        public override bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            bool started = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);
            if (!started)
                return false;

            if (playerStatus != null)
            {
                var inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);
                Input* baseInput = frame.GetPlayerInput(playerStatus->PlayerRef);
                FP eps = FP.FromFloat_UNSAFE(0.001f);

                if (IsLongThrow)
                {
                    if (baseInput != null)
                    {
                        QuantumDemoInputTopDown qInput = *baseInput;
                        FPVector2 aim2 = qInput.AimDirection;
                        FP mag = aim2.Magnitude;

                        if (mag > eps)
                        {
                            inv->ActiveAbilityInfo.CastStrength = FPMath.Clamp01(mag);
                        }
                        else
                        {
                            // TAP: no aim, max range in facing direction
                            FPQuaternion castRot = inv->ActiveAbilityInfo.CastRotation;
                            inv->ActiveAbilityInfo.CastDirection = castRot * FPVector3.Forward;
                            inv->ActiveAbilityInfo.CastStrength = FP._1;
                        }
                    }
                }
                else
                {
                    if (baseInput != null)
                    {
                        QuantumDemoInputTopDown qInput = *baseInput;
                        FPVector2 aim2 = qInput.AimDirection;
                        FP mag = aim2.Magnitude;

                        if (mag <= eps)
                        {
                            FPQuaternion castRot = inv->ActiveAbilityInfo.CastRotation;
                            inv->ActiveAbilityInfo.CastDirection = castRot * FPVector3.Forward;
                        }
                    }

                    inv->ActiveAbilityInfo.CastStrength = FP.FromFloat_UNSAFE(0.25f);
                }
            }

            frame.Events.OnPlayerThrewBall(entityRef, IsLongThrow);
            return true;
        }

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            Ability.AbilityState abilityState = base.UpdateAbility(frame, entityRef, ref ability);
            if (!abilityState.IsActiveStartTick)
                return abilityState;

            ThrowBallAbilityData throwBallAbilityData =
                frame.FindAsset<ThrowBallAbilityData>(ability.AbilityData.Id);

            PlayerStatus* playerStats = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
            Transform3D* playerTr = frame.Unsafe.GetPointer<Transform3D>(entityRef);
            AbilityInventory* abilityInventory = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

            if (playerStats == null || !playerStats->HoldingBallEntityRef.IsValid)
                return abilityState;

            EntityRef ballEntity = playerStats->HoldingBallEntityRef;
            BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(ballEntity);
            Transform3D* ballTransform = frame.Unsafe.GetPointer<Transform3D>(ballEntity);
            PhysicsBody3D* ballPhysicsBody = frame.Unsafe.GetPointer<PhysicsBody3D>(ballEntity);

            frame.Signals.OnBallReleased(ballEntity);

            ballTransform->Position =
                playerTr->Position + (abilityInventory->ActiveAbilityInfo.CastRotation * throwBallAbilityData.ThrowLocalPosition);

            if (!IsLongThrow)
            {
                playerTr->Rotation = abilityInventory->ActiveAbilityInfo.CastRotation;

                if (frame.Has<BallTrajectoryState>(ballEntity))
                {
                    var traj = frame.Unsafe.GetPointer<BallTrajectoryState>(ballEntity);
                    traj->Finished = true;
                    traj->PathSpeed = FP._0;
                    traj->PathTotalLen = FP._0;
                    traj->PathCount = 0;
                }

                if (ballPhysicsBody != null)
                {
                    ballPhysicsBody->IsKinematic = false;
                    ballPhysicsBody->Velocity = FPVector3.Zero;
                    ballPhysicsBody->AngularVelocity = FPVector3.Zero;

                    FPVector3 impulseRelativePoint = ballPhysicsBody->CenterOfMass;
                    impulseRelativePoint.Y += throwBallAbilityData.ThrowImpulseOffsetY;

                    FPVector3 impulse =
                        abilityInventory->ActiveAbilityInfo.CastRotation * throwBallAbilityData.ThrowImpulse;

                    ballPhysicsBody->AddLinearImpulse(impulse, impulseRelativePoint);
                }

                if (ballStatus != null && throwBallAbilityData.ThrowGravityChangeDuration > FP._0)
                    ballStatus->GravityChangeTimer.Start(throwBallAbilityData.ThrowGravityChangeDuration);

                return abilityState;
            }

            BallTrajectoryState* trajLong;
            if (!frame.Has<BallTrajectoryState>(ballEntity))
                frame.Add<BallTrajectoryState>(ballEntity);
            trajLong = frame.Unsafe.GetPointer<BallTrajectoryState>(ballEntity);

            FP strength = FPMath.Clamp01(abilityInventory->ActiveAbilityInfo.CastStrength);
            FPVector3 dir = abilityInventory->ActiveAbilityInfo.CastDirection;

            if (dir.SqrMagnitude <= FP._0)
            {
                dir = abilityInventory->ActiveAbilityInfo.CastRotation * FPVector3.Forward;
            }
            dir = dir.Normalized;

            FPVector3[] cached = BombTrajectoryBuffer.PeekOrDefault(
                ballTransform->Position,
                dir,
                strength
            );

            const int MaxPts = 32;
            int srcCount = cached.Length;
            int dstCount = (srcCount <= MaxPts) ? srcCount : MaxPts;

            for (int i = 0; i < dstCount; i++)
            {
                int idx = (i == dstCount - 1)
                    ? (srcCount - 1)
                    : (int)((long)i * (srcCount - 1) / (dstCount - 1));
                trajLong->Path[i] = cached[idx];
            }

            trajLong->PathCount = dstCount;

            FP totalLen = FP._0;
            for (int i = 1; i < dstCount; i++)
                totalLen += (trajLong->Path[i] - trajLong->Path[i - 1]).Magnitude;

            trajLong->PathTotalLen = totalLen;
            trajLong->PathDist = FP._0;

            FP baseSpeed = FP.FromFloat_UNSAFE(15.0f);
            FP speed = baseSpeed * FP.FromFloat_UNSAFE(1.15f);

            trajLong->PathSpeed = (totalLen > FP._0 && speed > FP._0) ? speed : FP._0;
            trajLong->Finished = false;

            if (dstCount >= 1)
                ballTransform->Position = trajLong->Path[0];

            if (ballPhysicsBody != null)
            {
                ballPhysicsBody->Velocity = FPVector3.Zero;
                ballPhysicsBody->AngularVelocity = FPVector3.Zero;
                ballPhysicsBody->IsKinematic = true;
            }

            if (ballStatus != null && throwBallAbilityData.ThrowGravityChangeDuration > FP._0)
                ballStatus->GravityChangeTimer.Start(throwBallAbilityData.ThrowGravityChangeDuration);

            return abilityState;
        }
    }
}
