using Photon.Deterministic;
using System;

namespace Quantum
{
    [Serializable]
    public unsafe partial class DashAbilityData : AbilityData
    {
        public FP DashDistance = FP.FromFloat_UNSAFE(30f);
        public FPAnimationCurve DashMovementCurve;

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            FP lastNormTime = ability.IsActive ? ability.DurationTimer.NormalizedTime : FP._0;

            var state = base.UpdateAbility(frame, entityRef, ref ability);

            var kcc = frame.Unsafe.GetPointer<CharacterController3D>(entityRef);
            var ps = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
            var inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

            var move = frame.FindAsset<PlayerMovementData>(ps->PlayerMovementData.Id);
            var defCfg = frame.FindAsset<CharacterController3DConfig>(move.DefaultKCCSettings.Id);

            if (!state.IsActive)
            {
                kcc->MaxSpeed = defCfg.MaxSpeed;
                return state;
            }

            FP t0 = FPMath.Clamp01(lastNormTime);
            FP t1 = FPMath.Clamp01(ability.DurationTimer.NormalizedTime);

            FP pStart = DashMovementCurve.Evaluate(FP._0);
            FP pEnd = DashMovementCurve.Evaluate(FP._1);
            FP span = pEnd - pStart;

            FP p0, p1;
            if (FPMath.Abs(span) < FP.FromFloat_UNSAFE(1e-3f))
            {
                p0 = t0; p1 = t1; span = FP._1;
            }
            else
            {
                p0 = DashMovementCurve.Evaluate(t0);
                p1 = DashMovementCurve.Evaluate(t1);
            }

            FP seg01 = (p1 - p0) / span;

            
            FPVector3 dir = inv->ActiveAbilityInfo.CastDirection;
            FP len = FPMath.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            if (len > FP._0)
            {
                dir = new FPVector3(dir.X / len, FP._0, dir.Z / len);
            }
            else
            {
                var xform = frame.Unsafe.GetPointer<Transform3D>(entityRef);
                FPVector3 fwd = xform->Rotation * FPVector3.Forward;
                FP fl = FPMath.Sqrt(fwd.X * fwd.X + fwd.Z * fwd.Z);
                dir = (fl > FP._0) ? new FPVector3(fwd.X / fl, FP._0, fwd.Z / fl) : new FPVector3(FP._0, FP._0, FP._1);
            }

            FPVector3 seg = dir * DashDistance * seg01;
            FP dt = frame.DeltaTime;
            FPVector3 velXZ = (dt > FP._0)
                ? new FPVector3(seg.X / dt, FP._0, seg.Z / dt)
                : FPVector3.Zero;

            FP needed = FPMath.Max(velXZ.Magnitude + FP.FromFloat_UNSAFE(0.5f), defCfg.MaxSpeed);
            if (kcc->MaxSpeed < needed)
                kcc->MaxSpeed = needed;

            kcc->Velocity = new FPVector3(velXZ.X, kcc->Velocity.Y, velXZ.Z);

            if (state.IsActiveEndTick)
            {
                kcc->MaxSpeed = defCfg.MaxSpeed;
                kcc->Velocity = dir * defCfg.MaxSpeed + new FPVector3(FP._0, kcc->Velocity.Y, FP._0);
            }

            return state;
        }

        public override bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            bool ok = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);
            if (!ok) return false;

            var inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);
            var xform = frame.Unsafe.GetPointer<Transform3D>(entityRef);
            var kcc = frame.Unsafe.GetPointer<CharacterController3D>(entityRef);

            FPVector3 dir = inv->ActiveAbilityInfo.CastDirection;
            dir = new FPVector3(dir.X, FP._0, dir.Z);
            FP len = FPMath.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            if (len <= FP._0)
            {
                FPVector3 fwd = xform->Rotation * FPVector3.Forward;
                FP fl = FPMath.Sqrt(fwd.X * fwd.X + fwd.Z * fwd.Z);
                dir = (fl > FP._0) ? new FPVector3(fwd.X / fl, FP._0, fwd.Z / fl) : new FPVector3(FP._0, FP._0, FP._1);
            }
            else
            {
                dir = new FPVector3(dir.X / len, FP._0, dir.Z / len);
            }
            inv->ActiveAbilityInfo.CastDirection = dir; // lock during dash

            kcc->Velocity = new FPVector3(FP._0, kcc->Velocity.Y, FP._0);

            frame.Events.OnPlayerDashed(entityRef);
            return true;
        }
    }
}
