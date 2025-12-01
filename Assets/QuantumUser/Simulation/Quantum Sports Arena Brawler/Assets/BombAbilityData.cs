using System;
using Photon.Deterministic;
using Quantum.Physics3D;

namespace Quantum
{
    [Serializable]
    public unsafe partial class BombAbilityData : AbilityData
    {
        public AssetRef<EntityPrototype> BombPrototype;
        public FPVector3 LocalSpawnOffset = new FPVector3(FP._0, FP._0_50, FP._0);

        public FP MinTravelTime = FP.FromFloat_UNSAFE(0.60f);
        public FP MaxTravelTime = FP.FromFloat_UNSAFE(1.20f);
        public FP TravelTimeScale = FP.FromFloat_UNSAFE(1f);

        public FP DefaultHandoffDistance = FP.FromFloat_UNSAFE(0.75f);
        public FP DefaultDownwardBias = FP.FromFloat_UNSAFE(2.0f);

        public FP ExplosionRadiusMeters = FP.FromFloat_UNSAFE(2.5f);
        public FP GroundFuseSeconds = FP.FromFloat_UNSAFE(2.0f);
        public FP LifeTimeSeconds = FP.FromFloat_UNSAFE(5.0f);
        public FP ContactTriggerMeters = FP.FromFloat_UNSAFE(0.4f);
        public FP KnockbackDuration = FP.FromFloat_UNSAFE(0.4f);

        public BombAbilityData()
        {
            Delay = FP._0;
            Duration = FP._0_10;
            KeepVelocity = true;
            FaceCastDirection = true;
            AllowConcurrent = true;
            Cooldown = FP._1;
            CastDirectionType = AbilityCastDirectionType.Aim | AbilityCastDirectionType.FacingDirection;
        }

        public override bool TryActivateAbility(
    Frame frame,
    EntityRef entityRef,
    PlayerStatus* playerStatus,
    ref Ability ability)
        {
            bool started = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);

            if (!started)
                return false;

            if (playerStatus != null)
            {
                var inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

                Input* baseInput = frame.GetPlayerInput(playerStatus->PlayerRef);
                if (baseInput != null)
                {
                    QuantumDemoInputTopDown qInput = *baseInput;

                    FPVector2 aim2 = qInput.AimDirection;
                    FP mag = aim2.Magnitude;

                    inv->ActiveAbilityInfo.CastStrength = FPMath.Clamp01(mag);

#if DEBUG || UNITY_EDITOR
                    Quantum.Log.Info(
                        $"[BombAbility] TryActivate: Aim2=({aim2.X.AsFloat:F2},{aim2.Y.AsFloat:F2}) " +
                        $"mag={mag.AsFloat:F2} -> CastStrength={inv->ActiveAbilityInfo.CastStrength.AsFloat:F2}"
                    );
#endif
                }
            }
            frame.Events.OnBombCast(entityRef);
            return true;
        }

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            var st = base.UpdateAbility(frame, entityRef, ref ability);
            if (!st.IsActiveStartTick)
                return st;

            if (!BombPrototype.IsValid)
            {
                Quantum.Log.Warn("[Bomb] Missing BombPrototype on ability asset.");
                return st;
            }

            var inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);
            var playerTr = frame.Unsafe.GetPointer<Transform3D>(entityRef);

            playerTr->Rotation = inv->ActiveAbilityInfo.CastRotation;

            var bomb = frame.Create(BombPrototype);
            var bTr = frame.Unsafe.GetPointer<Transform3D>(bomb);
            var bombState = frame.Unsafe.GetPointer<BombState>(bomb);

            bTr->Position = playerTr->Position + (inv->ActiveAbilityInfo.CastRotation * LocalSpawnOffset);
            bTr->Rotation = inv->ActiveAbilityInfo.CastRotation;

            if (frame.Has<PlayerStatus>(entityRef))
            {
                var ownerStatus = frame.Get<PlayerStatus>(entityRef);
                bombState->Owner = entityRef;
                bombState->OwnerTeam = ownerStatus.PlayerTeam;
            }
            else
            {
                bombState->Owner = entityRef;
                bombState->OwnerTeam = default;
            }

            if (frame.Has<PhysicsBody3D>(bomb))
            {
                var body = frame.Unsafe.GetPointer<PhysicsBody3D>(bomb);
                body->Velocity = FPVector3.Zero;
                body->AngularVelocity = FPVector3.Zero;
                body->IsKinematic = true;
            }
            if (frame.Has<PhysicsCollider3D>(bomb))
            {
                var col = frame.Unsafe.GetPointer<PhysicsCollider3D>(bomb);
                col->Enabled = false;
            }

            FP strength = FPMath.Clamp01(inv->ActiveAbilityInfo.CastStrength);
            FPVector3 dir = inv->ActiveAbilityInfo.CastDirection;
            if (dir.SqrMagnitude <= FP._0)
                dir = FPVector3.Forward;
            else
                dir = dir.Normalized;

            Quantum.Log.Info($"[BombAbility] AimDir={inv->ActiveAbilityInfo.CastDirection} " +
                             $"Strength={inv->ActiveAbilityInfo.CastStrength.AsFloat:F2}");

            FPVector3[] cached = BombTrajectoryBuffer.PeekOrDefault(
                playerTr->Position + FPVector3.Up * FP._0_25,
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
                bombState->Path[i] = cached[idx];
            }

            bombState->PathCount = dstCount;

            FP totalLen = FP._0;
            for (int i = 1; i < dstCount; i++)
                totalLen += (bombState->Path[i] - bombState->Path[i - 1]).Magnitude;

            bombState->PathTotalLen = totalLen;
            bombState->PathDist = FP._0;

           
            FP baseSpeed = FP.FromFloat_UNSAFE(15.0f);
            FP speed = baseSpeed * TravelTimeScale;

            if (totalLen > FP._0 && speed > FP._0)
            {
                bombState->PathSpeed = speed;

#if DEBUG || UNITY_EDITOR
                FP travelTime = totalLen / speed;
                Quantum.Log.Info(
                    $"[BombAbility] len={totalLen.AsFloat:F2}, speed={speed.AsFloat:F2}, " +
                    $"time={travelTime.AsFloat:F2}"
                );
#endif
            }
            else
            {
                bombState->PathSpeed = FP._0;
            }

            // ensure not finished at start
            bombState->Finished = false;

            bombState->HandoffDistance = FPMath.Max(
                DefaultHandoffDistance,
                totalLen * FP.FromFloat_UNSAFE(0.15f)
            );

            bombState->DownwardBias = DefaultDownwardBias;
            bombState->ExplosionRadius = ExplosionRadiusMeters;
            bombState->LifeTimeLeft = LifeTimeSeconds;
            bombState->GroundFuseLeft = GroundFuseSeconds;
            bombState->ContactTriggerRadius = ContactTriggerMeters;
            bombState->GroundFuseArmed = false;
            bombState->Exploded = false;

            if (dstCount >= 1)
                bTr->Position = bombState->Path[0];

            Quantum.Log.Info(
                $"[BombAbility] pts={bombState->PathCount}, " +
                $"len={bombState->PathTotalLen.AsFloat:F2}, " +
                $"speed={bombState->PathSpeed.AsFloat:F2}"
            );

            return st;
        }

    }
}
