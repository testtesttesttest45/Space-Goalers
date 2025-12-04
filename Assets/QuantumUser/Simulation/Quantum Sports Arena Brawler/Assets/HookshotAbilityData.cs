using System;
using System.Collections.Generic;
using Photon.Deterministic;
using Quantum.Inspector;
using Quantum.Physics3D;
using UnityEngine;

namespace Quantum
{
    [Serializable]
    public unsafe partial class HookshotAbilityData : AbilityData
    {
        [Header("Shape (thin Capsule/Box)")]
        public Shape3DConfig AttackShape;

        [Header("Range & Sampling")]
        public FP Range = FP._6;
        public FP Step = FP._0_25;
        public FP StartForward = FP._0_75;
        public FP StartUp = FP._0_25;

        [Header("On-Hit Effects")]
        public FP PullYOffset = FP._0;
        public StatusEffectConfig[] HitStatusEffects;

        private static readonly HashSet<EntityRef> _hitEntities = new HashSet<EntityRef>();

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            var state = base.UpdateAbility(frame, entityRef, ref ability);

            if (state.IsActiveEndTick)
            {
                AbilityInventory* invEnd = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);
                if (invEnd != null)
                    invEnd->ActiveAbilityInfo.CastDirection = FPVector3.Zero;
            }

            if (!state.IsActiveStartTick)
                return state;

            Transform3D* attackerTf = frame.Unsafe.GetPointer<Transform3D>(entityRef);
            if (attackerTf == null)
                return state;

            GameSettingsData gameSettings = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            int mask = gameSettings.PlayerLayerMask;

            PlayerStatus* attackerStatus = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
            AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

            FPVector3 fwd = (inv != null && inv->ActiveAbilityInfo.CastDirection.SqrMagnitude > FP._0)
              ? inv->ActiveAbilityInfo.CastDirection
              : GetAimForwardXZ(frame, attackerStatus, attackerTf);

            fwd.Y = FP._0;
            fwd = (fwd.SqrMagnitude > FP._0) ? fwd.Normalized : FPVector3.Forward;

            var shape = AttackShape.CreateShape(frame);

            _hitEntities.Clear();
            _hitEntities.Add(entityRef);

            bool grabbed = false;

            Transform3D probe = *attackerTf;
            probe.Rotation = FPQuaternion.LookRotation(fwd, FPVector3.Up);

            if (Step <= FP._0) Step = FP._0_25;
            FP along = StartForward;
            int guard = 0, guardMax = 256;

            while (along <= Range && guard++ < guardMax)
            {
                probe.Position = attackerTf->Position + fwd * along + FPVector3.Up * StartUp;

                HitCollection3D hits = frame.Physics3D.OverlapShape(probe, shape, mask, QueryOptions.HitKinematics);
                if (hits.Count > 0)
                {
                    for (int i = 0; i < hits.Count; ++i)
                    {
                        Hit3D hit = hits[i];
                        EntityRef victim = hit.Entity;
                        if (victim == EntityRef.None || victim == entityRef) continue;
                        if (_hitEntities.Contains(victim)) continue;

                        _hitEntities.Add(victim);

                        PlayerStatus* victimStatus = frame.Unsafe.GetPointer<PlayerStatus>(victim);
                        if (victimStatus == null) continue;

                        if (attackerStatus != null && attackerStatus->PlayerTeam == victimStatus->PlayerTeam)
                            continue;

                        AbilityInventory* victimInv = frame.Unsafe.GetPointer<AbilityInventory>(victim);
                        if (victimInv != null && victimInv->IsBlocking)
                        {
                            Transform3D* victimTfB = frame.Unsafe.GetPointer<Transform3D>(victim);
                            if (victimTfB != null)
                            {
                                FPVector3 dir = attackerTf->Position - victimTfB->Position;
                                dir.Y = FP._0;
                                dir = dir.SqrMagnitude > FP._0 ? dir.Normalized : FPVector3.Forward;
                                frame.Events.OnPlayerBlockHit(victim, dir);
                            }
                            grabbed = true;
                            break;
                        }

                        if (HitStatusEffects != null)
                        {
                            for (int e = 0; e < HitStatusEffects.Length; ++e)
                            {
                                var effect = HitStatusEffects[e];
                                if (effect.Type == StatusEffectType.Stun)
                                {
                                    frame.Signals.OnStunApplied(victim, effect.Duration);
                                }
                                else if (effect.Type == StatusEffectType.Knockback)
                                {
                                    Transform3D* victimTf = frame.Unsafe.GetPointer<Transform3D>(victim);
                                    if (victimTf != null)
                                    {
                                        FPVector3 kb = (attackerTf->Position - victimTf->Position);
                                        kb.Y = FP._0;
                                        kb = kb.SqrMagnitude > FP._0 ? kb.Normalized : FPVector3.Forward;
                                        victimStatus->KnockbackStatusEffect.StatusEffectData = effect.KnockbackData;

                                        frame.Signals.OnKnockbackApplied(
                                            victim,
                                            effect.Duration,
                                            kb,
                                            effect.KnockbackData
                                        );

                                    }
                                }
                            }
                        }

                        frame.Events.OnPlayerHit(victim);
                        frame.Events.OnPlayerHookHit(entityRef, victim);
                        grabbed = true;
                        break;
                    }

                    if (grabbed) break;
                }

                along += Step;
            }

            _hitEntities.Clear();
            return state;
        }

        public override unsafe bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            bool activated = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);
            if (!activated) return false;

            Transform3D* tf = frame.Unsafe.GetPointer<Transform3D>(entityRef);
            AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(entityRef);

            FPVector3 fwd = (inv != null && inv->ActiveAbilityInfo.CastDirection.SqrMagnitude > FP._0)
              ? inv->ActiveAbilityInfo.CastDirection
              : GetAimForwardXZ(frame, playerStatus, tf);

            if (fwd.SqrMagnitude > FP._0)
            {
                fwd = fwd.Normalized;
                if (tf != null) tf->Rotation = FPQuaternion.LookRotation(fwd, FPVector3.Up);
                if (inv != null) inv->ActiveAbilityInfo.CastDirection = fwd;
            }

            frame.Events.OnPlayerHookshot(entityRef);
            return true;
        }

        private static FPVector3 GetAimForwardXZ(Frame frame, PlayerStatus* status, Transform3D* attackerTf)
        {
            if (status != null)
            {
                QuantumDemoInputTopDown input = *frame.GetPlayerInput(status->PlayerRef);
                FPVector3 fromInput = new FPVector3(input.AimDirection.X, FP._0, input.AimDirection.Y);
                if (fromInput.SqrMagnitude > FP._0)
                    return fromInput.Normalized;
            }

            if (attackerTf != null)
            {
                FPVector3 fwd = attackerTf->Rotation * FPVector3.Forward;
                fwd.Y = FP._0;
                if (fwd.SqrMagnitude > FP._0)
                    return fwd.Normalized;
            }

            return FPVector3.Forward;
        }

    }
}
