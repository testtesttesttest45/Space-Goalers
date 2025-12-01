using System;
using UnityEngine;

namespace Quantum
{
    public unsafe class AbilitySystem : SystemMainThreadFilter<AbilitySystem.Filter>,
      ISignalOnActiveAbilityStopped,
      ISignalOnCooldownsReset,
      ISignalOnComponentAdded<AbilityInventory>
    {
        public struct Filter
        {
            public EntityRef EntityRef;
            public PlayerStatus* PlayerStatus;
            public AbilityInventory* AbilityInventory;
        }

        private static readonly AbilityType[] kUtilitySlots = {
            AbilityType.Dash, AbilityType.Invisibility, AbilityType.Banana, AbilityType.Speedster
        };

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool IsUtility(AbilityType t) =>
          t == AbilityType.Dash || t == AbilityType.Invisibility ||
          t == AbilityType.Banana || t == AbilityType.Speedster;

        internal static unsafe AbilityType FindWiredUtilitySlotStrict(AbilityInventory* inv)
        {
            AbilityType? first = null; int count = 0;
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                ref var a = ref inv->Abilities[i];
                if (!a.AbilityData.IsValid) continue;
                if (IsUtility(a.AbilityType)) { count++; if (first == null) first = a.AbilityType; }
            }
            return first ?? AbilityType.Invisibility;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static unsafe bool ShouldProcessUtilityAbility(AbilityInventory* inv, AbilityType thisEnum)
        {
            if (!IsUtility(thisEnum)) return true;
            return thisEnum == FindWiredUtilitySlotStrict(inv);
        }

        private static string AName(AbilityType t) => t.ToString();
        private static string BtnName(bool fire, bool alt) => $"[Fire:{(fire ? "1" : "0")} Alt:{(alt ? "1" : "0")}]";
        private static bool _dumpedInventory;

        private static void DumpInventory(Frame frame, AbilityInventory* inv)
        {
            if (_dumpedInventory) return;
            _dumpedInventory = true;
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                ref var a = ref inv->Abilities[i];
                if (!a.AbilityData.IsValid) continue;
                var data = frame.FindAsset<AbilityData>(a.AbilityData.Id);
                var mapped = MapType(data);
            }
        }

        private static int FindSlotIndex(AbilityInventory* inv, AbilityType t)
        {
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                if (!inv->Abilities[i].AbilityData.IsValid) continue;
                if (inv->Abilities[i].AbilityType == t) return i;
            }
            return -1;
        }

        private static bool IsAbilityActive(AbilityInventory* inv, AbilityType t)
        {
            int idx = FindSlotIndex(inv, t);
            if (idx < 0) return false;
            if (inv->ActiveAbilityInfo.ActiveAbilityIndex == idx) return true;
            return inv->Abilities[idx].IsDelayedOrActive;
        }

        public override void Update(Frame frame, ref Filter filter)
        {
            if (filter.AbilityInventory->HasActiveAbility)
            {
                int idx = filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex;
                if (idx >= 0 && idx < filter.AbilityInventory->Abilities.Length)
                {
                    Ability cur = filter.AbilityInventory->Abilities[idx];
                    AbilityData curData = frame.FindAsset<AbilityData>(cur.AbilityData.Id);
                    if (curData != null)
                    {
                        if (cur.AbilityType == AbilityType.Block)
                        {
                            filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                        }
                        else if (curData.AllowConcurrent)
                        {
                            filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                        }
                    }
                }
            }

            for (int i = 0; i < filter.AbilityInventory->Abilities.Length; i++)
            {
                ref var a = ref filter.AbilityInventory->Abilities[i];
                if (!a.AbilityData.IsValid) continue;
                var data = frame.FindAsset<AbilityData>(a.AbilityData.Id);
                if (data == null) continue;
                var want = MapType(data);
                if (a.AbilityType != want)
                {
                    a.AbilityType = want;
#if UNITY_EDITOR || DEBUG
                    var id = a.AbilityData.Id.Value.ToString("X16");
                    Quantum.Log.Debug($"[Heal] Fixed slot {i}: enum={want} asset={id}");
#endif
                }
            }

            if (filter.PlayerStatus == null) return;

            Input* baseInput = frame.GetPlayerInput(filter.PlayerStatus->PlayerRef);
            if (baseInput == null) return;

            QuantumDemoInputTopDown input = *baseInput;

            DumpInventory(frame, filter.AbilityInventory);

            if (filter.AbilityInventory->HasActiveAbility)
            {
                int idx = filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex;
                if (idx >= 0 && idx < filter.AbilityInventory->Abilities.Length)
                {
                    Ability cur = filter.AbilityInventory->Abilities[idx];
                    AbilityData curData = frame.FindAsset<AbilityData>(cur.AbilityData.Id);
                    if (curData != null && curData.AllowConcurrent)
                        filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                }
            }

            bool hasBall = filter.PlayerStatus->IsHoldingBall && !filter.AbilityInventory->IsThrowingBall;

            AbilityType main1Owner, main2Owner;
            ResolveSelectedMainOwners(frame, filter.PlayerStatus->PlayerRef, filter.AbilityInventory, out main1Owner, out main2Owner);

            int activeIdx = filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex;
            AbilityType activeType = default;
            bool activeValid = activeIdx >= 0 && activeIdx < filter.AbilityInventory->Abilities.Length;

            if (activeValid)
            {
                ref var act = ref filter.AbilityInventory->Abilities[activeIdx];
                var actData = frame.FindAsset<AbilityData>(act.AbilityData.Id);
                if (actData != null)
                {
                    activeType = act.AbilityType;
                }
            }

            AbilityType? desiredMainThisFrame = null;
            bool firePressedNow = input.Fire.WasPressed;
            bool altPressedNow = input.AltFire.WasPressed;

            if (!hasBall)
            {
                if (firePressedNow && !altPressedNow) desiredMainThisFrame = main1Owner;
                else if (altPressedNow && !firePressedNow) desiredMainThisFrame = main2Owner;
                else if (firePressedNow && altPressedNow)
                {
                    var prefer = main2Owner;
                    if (activeValid && activeType == main2Owner) prefer = main1Owner;
                    desiredMainThisFrame = prefer;
                }
            }

            if (!hasBall && desiredMainThisFrame.HasValue)
            {
                for (int i = 0; i < kMainCandidates.Length; i++)
                {
                    var other = kMainCandidates[i];
                    if (other == desiredMainThisFrame.Value) continue;

                    int oidx = FindSlotIndex(filter.AbilityInventory, other);
                    if (oidx < 0) continue;

                    ref var o = ref filter.AbilityInventory->Abilities[oidx];
                    bool otherIsActive = o.IsDelayedOrActive || filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex == oidx;
                    if (otherIsActive)
                    {

                        o.StopAbility(frame, filter.EntityRef);
                        if (filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex == oidx)
                            filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                    }
                }
            }

            for (int i = 0; i < filter.AbilityInventory->Abilities.Length; i++)
            {
                ref Ability ability = ref filter.AbilityInventory->Abilities[i];
                if (!ability.AbilityData.IsValid) continue;

                AbilityData abilityData = frame.FindAsset<AbilityData>(ability.AbilityData.Id);
                if (abilityData == null) continue;

                abilityData.UpdateAbility(frame, filter.EntityRef, ref ability);

                AbilityType t = ability.AbilityType;

                if (!hasBall && (t == AbilityType.ThrowShort || t == AbilityType.ThrowLong))
                {
                    abilityData.UpdateInput(frame, ref ability, false);
                    continue;
                }

                if (!ShouldProcessUtilityAbility(filter.AbilityInventory, t))
                {
                    abilityData.UpdateInput(frame, ref ability, false);
                    continue;
                }

                bool otherMainSuppressed =
                    !hasBall &&
                    desiredMainThisFrame.HasValue &&
                    (t == main1Owner || t == main2Owner) &&
                    t != desiredMainThisFrame.Value;

                if (otherMainSuppressed)
                {
                    if (ability.IsDelayedOrActive || filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex == i)
                    {
                        ability.StopAbility(frame, filter.EntityRef);
                        if (filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex == i)
                            filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                    }
                    abilityData.UpdateInput(frame, ref ability, false);
                    continue;
                }

                bool pressed = false;

                if (!hasBall)
                {
                    if (t == main1Owner) pressed = input.Fire.WasPressed;
                    else if (t == main2Owner) pressed = input.AltFire.WasPressed;
                    else if (t == AbilityType.Jump || IsUtility(t) || t == AbilityType.Hook)
                        pressed = input.GetAbilityInputWasPressed(t);
                }
                else
                {
                    if (t == AbilityType.ThrowShort) pressed = input.Fire.WasPressed;
                    else if (t == AbilityType.ThrowLong) pressed = input.AltFire.WasPressed;
                    else if (t == AbilityType.Jump || IsUtility(t) || t == AbilityType.Hook)
                        pressed = input.GetAbilityInputWasPressed(t);
                }

                abilityData.UpdateInput(frame, ref ability, pressed);
                abilityData.TryActivateAbility(frame, filter.EntityRef, filter.PlayerStatus, ref ability);

                int activeIdxNow = filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex;

                if (t == AbilityType.Block && activeIdxNow == i)
                {
                    filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                }
                else if (abilityData.AllowConcurrent && activeIdxNow == i)
                {
                    filter.AbilityInventory->ActiveAbilityInfo.ActiveAbilityIndex = -1;
                }
            }
        }

        public void OnActiveAbilityStopped(Frame frame, EntityRef playerEntityRef)
        {
            AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(playerEntityRef);
            if (!inv->HasActiveAbility) return;

            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                Ability ability = inv->Abilities[i];
                if (ability.IsDelayedOrActive)
                {
                    ability.StopAbility(frame, playerEntityRef);
                    break;
                }
            }
        }

        public void OnCooldownsReset(Frame frame, EntityRef playerEntityRef)
        {
            AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(playerEntityRef);
            for (int i = 0; i < inv->Abilities.Length; i++)
                inv->Abilities[i].ResetCooldown();
        }

        private static AbilityType MapType(AbilityData data)
        {
            if (data is AttackAbilityData) return AbilityType.Attack;
            if (data is BlockAbilityData) return AbilityType.Block;
            if (data is DashAbilityData) return AbilityType.Dash;
            if (data is InvisibilityAbilityData) return AbilityType.Invisibility;
            if (data is SpeedsterAbilityData) return AbilityType.Speedster;
            if (data is BananaAbilityData) return AbilityType.Banana;
            if (data is BombAbilityData) return AbilityType.Bomb;
            if (data is HookshotAbilityData) return AbilityType.Hook;
            if (data is JumpAbilityData) return AbilityType.Jump;

            // Short vs Long determined by the asset flag
            if (data is ThrowBallAbilityData tbd)
                return tbd.IsLongThrow ? AbilityType.ThrowLong : AbilityType.ThrowShort;

            return AbilityType.Attack;
        }

        static unsafe bool HasAbility(AbilityInventory* inv, AbilityType t)
        {
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                if (!inv->Abilities[i].AbilityData.IsValid) continue;
                if (inv->Abilities[i].AbilityType == t) return true;
            }
            return false;
        }

        public void OnAdded(Frame frame, EntityRef entity, AbilityInventory* inv)
        {
            inv->ActiveAbilityInfo.ActiveAbilityIndex = -1;

            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                ref Ability a = ref inv->Abilities[i];
                a.AbilityType = (AbilityType)i;
                if (!a.AbilityData.IsValid) continue;

                var data = frame.FindAsset<AbilityData>(a.AbilityData.Id);
                if (data != null) a.AbilityType = MapType(data);
            }
        }

        private static readonly AbilityType[] kMainCandidates = {
            AbilityType.Attack, AbilityType.Block, AbilityType.Bomb, AbilityType.Hook
        };

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool IsMainCandidate(AbilityType t) =>
            t == AbilityType.Attack || t == AbilityType.Block || t == AbilityType.Bomb || t == AbilityType.Hook;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static unsafe void ResolveSelectedMainOwners(
            Frame frame, PlayerRef player, AbilityInventory* inv,
            out AbilityType main1Owner, out AbilityType main2Owner)
        {
            AbilityType? first = null, second = null;
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                ref var a = ref inv->Abilities[i];
                if (!a.AbilityData.IsValid) continue;
                var t = a.AbilityType;
                if (t != AbilityType.Attack && t != AbilityType.Block && t != AbilityType.Bomb && t != AbilityType.Hook) continue;

                if (first == null) first = t;
                else if (t != first.Value) { second = t; break; }
            }

            main1Owner = first ?? AbilityType.Attack;

            if (second != null)
            {
                main2Owner = second.Value;
            }
            else
            {
                bool hasBlock = HasAbility(inv, AbilityType.Block);
                if (hasBlock && main1Owner != AbilityType.Block) main2Owner = AbilityType.Block;
                else
                {
                    AbilityType fallback = main1Owner;
                    for (int i = 0; i < inv->Abilities.Length; i++)
                    {
                        ref var a = ref inv->Abilities[i];
                        if (!a.AbilityData.IsValid) continue;
                        var t = a.AbilityType;
                        if ((t == AbilityType.Attack || t == AbilityType.Block || t == AbilityType.Bomb || t == AbilityType.Hook)
                            && t != main1Owner) { fallback = t; break; }
                    }
                    main2Owner = (fallback == main1Owner) ? AbilityType.Block : fallback;
                }
            }
        }


    }
}
