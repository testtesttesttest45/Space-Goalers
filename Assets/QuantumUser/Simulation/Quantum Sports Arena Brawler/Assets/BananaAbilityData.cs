using System;
using Photon.Deterministic;
using Quantum.Inspector;
using UnityEngine;

namespace Quantum
{
    [Serializable]
    public unsafe partial class BananaAbilityData : AbilityData
    {
        [Header("Banana Trap (seconds / meters)")]
        public float TrapLifetimeSec = 8f;
        public float TriggerRadiusM = 0.60f;
        public float SlowDurationSec = 1.50f;
        public float MaxDropRadiusM = 5.0f;

        [Header("Banana Ally Boost")]
        public float AllyBoostDurationSec = 1.10f;

        FP _lastTriggerRadius;
        FP _lastSlowDuration;

        public BananaAbilityData()
        {
            Delay = FP._0;
            KeepVelocity = true;
            FaceCastDirection = false;
            Cooldown = FP.FromFloat_UNSAFE(6f);
            AllowConcurrent = true;
        }

        public override bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* ps, ref Ability ability)
        {
            FP trapLifetime = FP.FromFloat_UNSAFE(TrapLifetimeSec);
            FP triggerRadius = FP.FromFloat_UNSAFE(TriggerRadiusM);
            FP slowDuration = FP.FromFloat_UNSAFE(SlowDurationSec);
            FP maxRadius = FP.FromFloat_UNSAFE(MaxDropRadiusM);

            if (!base.TryActivateAbility(frame, entityRef, ps, ref ability))
                return false;

            ability.DurationTimer.Start(trapLifetime);

            if (!frame.Has<BananaTrapOwner>(entityRef))
                frame.Add<BananaTrapOwner>(entityRef);

            var trap = frame.Unsafe.GetPointer<BananaTrapOwner>(entityRef);
            var tr = frame.Unsafe.GetPointer<Transform3D>(entityRef);

            QuantumDemoInputTopDown inp = *frame.GetPlayerInput(ps->PlayerRef);
            FPVector2 offMeters = new FPVector2(
                inp.AimDirection.X * maxRadius,
                inp.AimDirection.Y * maxRadius
            );

            FP lenSq = offMeters.X * offMeters.X + offMeters.Y * offMeters.Y;
            if (lenSq > maxRadius * maxRadius)
            {
                FP len = FPMath.Sqrt(lenSq);
                if (len > FP._0)
                {
                    FP scale = maxRadius / len;
                    offMeters = new FPVector2(offMeters.X * scale, offMeters.Y * scale);
                }
            }

            if (offMeters.SqrMagnitude < FP.FromFloat_UNSAFE(0.05f))
            {
                FPVector3 backwardXZ = -tr->Forward;
                backwardXZ.Y = FP._0;
                backwardXZ = backwardXZ.Normalized;
                offMeters = new FPVector2(
                    backwardXZ.X * FP.FromFloat_UNSAFE(1.0f),
                    backwardXZ.Z * FP.FromFloat_UNSAFE(1.0f));
            }

            trap->Active = true;
            trap->TriggerRadius = triggerRadius;
            trap->Despawn.Start(trapLifetime);
            trap->FallVelocity = FP._0;

            trap->Armed = false;
            trap->ArmDelay.TimeLeft = FP._0;
            trap->ArmDelay.StartTime = FP._0;

            trap->Pos = tr->Position + new FPVector3(offMeters.X, FP.FromFloat_UNSAFE(3.0f), offMeters.Y);

            _lastTriggerRadius = triggerRadius;
            _lastSlowDuration = slowDuration;

            inp.AimDirection = FPVector2.Zero;
            *frame.GetPlayerInput(ps->PlayerRef) = inp;
            frame.Events.OnBananaActivated(entityRef);
            return true;
        }

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            if (!frame.Has<BananaTrapOwner>(entityRef))
                return base.UpdateAbility(frame, entityRef, ref ability);

            var trap = frame.Unsafe.GetPointer<BananaTrapOwner>(entityRef);
            if (!trap->Active)
                return base.UpdateAbility(frame, entityRef, ref ability);

            FP gravity = FP.FromFloat_UNSAFE(-25.0f);
            FP voidY = FP.FromFloat_UNSAFE(-10.0f);
            FP maxFall = FP.FromFloat_UNSAFE(-60.0f);

            trap->FallVelocity += gravity * frame.DeltaTime;
            if (trap->FallVelocity < maxFall)
                trap->FallVelocity = maxFall;

            FPVector3 nextPos = trap->Pos;
            nextPos.Y += trap->FallVelocity * frame.DeltaTime;

            FPVector3 rayStart = trap->Pos + new FPVector3(FP._0, FP.FromFloat_UNSAFE(0.5f), FP._0);
            FPVector3 rayDir = FPVector3.Down;
            FP rayDist = FP.FromFloat_UNSAFE(6.0f);

            var hit = frame.Physics3D.Raycast(rayStart, rayDir, rayDist);
            bool hasGround = hit.HasValue;
            if (hasGround)
            {
                FP groundY = hit.Value.Point.Y;
                if (hit.Value.Entity == entityRef)
                    hasGround = false;

                if (hasGround && nextPos.Y <= groundY)
                {
                    nextPos.Y = groundY;
                    trap->FallVelocity = FP._0;
                }
            }

            trap->Pos = nextPos;

            if (trap->FallVelocity == FP._0)
            {
                if (!trap->Armed)
                {
                    if (!trap->ArmDelay.IsRunning)
                        trap->ArmDelay.Start(FP.FromFloat_UNSAFE(0.08f));

                    trap->ArmDelay.Tick(frame.DeltaTime);

                    if (!trap->ArmDelay.IsRunning)
                        trap->Armed = true;
                }
            }
            else
            {
                trap->Armed = false;
                trap->ArmDelay.TimeLeft = FP._0;
                trap->ArmDelay.StartTime = FP._0;
            }

            if (trap->Pos.Y < voidY)
            {
                trap->Active = false;
                return base.UpdateAbility(frame, entityRef, ref ability);
            }

            trap->Despawn.Tick(frame.DeltaTime);
            if (!trap->Despawn.IsRunning)
            {
                trap->Active = false;
                return base.UpdateAbility(frame, entityRef, ref ability);
            }

            if (trap->Armed)
            {
                FP r2 = _lastTriggerRadius * _lastTriggerRadius;
                var ownerPs = frame.Get<PlayerStatus>(entityRef);

                var it = frame.Filter<PlayerStatus, Transform3D>();
                while (it.Next(out EntityRef other, out PlayerStatus otherPs, out Transform3D otherTr))
                {
                    FPVector3 d = otherTr.Position - trap->Pos;
                    d.Y = FP._0;

                    if (d.SqrMagnitude > r2) continue;

                    bool isAlly = (otherPs.PlayerTeam == ownerPs.PlayerTeam);
                    if (isAlly)
                    {
                        var psOther = frame.Unsafe.GetPointer<PlayerStatus>(other);
                        psOther->ExternalSpeedsterActive = true;
                        psOther->ExternalSpeedster.Start(FP.FromFloat_UNSAFE(AllyBoostDurationSec));

                        frame.Events.OnBananaConsumed(other, true);

                        trap->Active = false;
                        break;
                    }
                    else
                    {
                        var invOther = frame.Unsafe.GetPointer<AbilityInventory>(other);
                        if (invOther != null && invOther->IsBlocking)
                        {
                            frame.Events.OnPlayerBlockHit(other, d);
                        }
                        else
                        {
                            ApplySlowTo(frame, other, _lastSlowDuration);
                        }

                        frame.Events.OnBananaConsumed(other, false);

                        trap->Active = false;
                        break;
                    }
                }
            }

            return base.UpdateAbility(frame, entityRef, ref ability);
        }

        private static void ApplySlowTo(Frame frame, EntityRef victim, FP dur)
        {
            if (!frame.Has<SlowedStatusEffect>(victim))
                frame.Add<SlowedStatusEffect>(victim);

            var slow = frame.Unsafe.GetPointer<SlowedStatusEffect>(victim);
            slow->IsActive = true;
            slow->Duration.Start(dur);
        }
    }
}