using Photon.Deterministic;
using Quantum.Inspector;
using Quantum.Physics3D;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
    [Serializable]
    public unsafe partial class AttackAbilityData : AbilityData
    {
        public Shape3DConfig AttackShape;

        public StatusEffectConfig[] HitStatusEffects;

        private static HashSet<EntityRef> _hitEntities = new HashSet<EntityRef>();

        public override Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            Ability.AbilityState abilityState = base.UpdateAbility(frame, entityRef, ref ability);

            if (abilityState.IsActiveStartTick)
            {
                PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(entityRef);
                Transform3D* transform = frame.Unsafe.GetPointer<Transform3D>(entityRef);
                GameSettingsData gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);

                var shape = AttackShape.CreateShape(frame);
                HitCollection3D hits = frame.Physics3D.OverlapShape(*transform, shape, gameSettingsData.PlayerLayerMask, QueryOptions.HitKinematics);

                if (hits.Count > 0)
                {
                    _hitEntities.Add(entityRef);

                    for (int i = 0; i < hits.Count; i++)
                    {
                        Hit3D hit = hits[i];

                        if (_hitEntities.Contains(hit.Entity))
                        {
                            continue;
                        }

                        _hitEntities.Add(hit.Entity);

                        PlayerStatus* hitPlayerStatus = frame.Unsafe.GetPointer<PlayerStatus>(hit.Entity);

                        if (playerStatus->PlayerTeam == hitPlayerStatus->PlayerTeam)
                        {
                            continue;
                        }

                        Transform3D* hitPlayerTransform = frame.Unsafe.GetPointer<Transform3D>(hit.Entity);
                        AbilityInventory* hitPlayerAbilityInventory = frame.Unsafe.GetPointer<AbilityInventory>(hit.Entity);

                        FPVector3 hitLateralDirection = hitPlayerTransform->Position - transform->Position;
                        hitLateralDirection.Y = FP._0;
                        hitLateralDirection = hitLateralDirection.Normalized;

                        if (hitPlayerAbilityInventory->IsBlocking)
                        {
                            frame.Events.OnPlayerBlockHit(hit.Entity, hitLateralDirection);
                        }
                        else
                        {
                            foreach (var statusEffectConfig in HitStatusEffects)
                            {
                                switch (statusEffectConfig.Type)
                                {
                                    case StatusEffectType.Stun:
                                        frame.Signals.OnStunApplied(hit.Entity, statusEffectConfig.Duration);
                                        break;

                                    case StatusEffectType.Knockback:
                                        {
                                            hitPlayerStatus->KnockbackStatusEffect.StatusEffectData = statusEffectConfig.KnockbackData;

                                            frame.Signals.OnKnockbackApplied(
                                                hit.Entity,
                                                statusEffectConfig.Duration,
                                                hitLateralDirection,
                                                statusEffectConfig.KnockbackData
                                            );
                                        }
                                        break;

                                    default:
                                        throw new System.ArgumentException($"Unknown {nameof(StatusEffectType)}: {statusEffectConfig.Type}", nameof(statusEffectConfig.Type));
                                }
                            }

                            frame.Events.OnPlayerHit(hit.Entity);
                        }
                    }

                    _hitEntities.Clear();
                }
            }

            return abilityState;
        }

        public override unsafe bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            bool activated = base.TryActivateAbility(frame, entityRef, playerStatus, ref ability);

            if (activated)
            {
                frame.Events.OnPlayerAttacked(entityRef);
            }

            return activated;
        }
    }
}
