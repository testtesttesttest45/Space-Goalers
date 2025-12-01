using System;
using System.Collections.Generic;
using Photon.Deterministic;
using UnityEngine;

namespace Quantum
{
    public unsafe class PlayerSpawnSystem
      : SystemMainThreadFilter<PlayerSpawnSystem.Filter>,
        ISignalOnPlayerAdded,
        ISignalOnPlayerRespawned,
        ISignalOnPlayerRespawnTimerReset
    {
        public FP SpawnSeparation = FP.FromFloat_UNSAFE(1.0f);

        public struct Filter
        {
            public EntityRef EntityRef;
            public PlayerStatus* PlayerStatus;
            public Transform3D* Transform;
            public PhysicsCollider3D* Collider;
        }

        private static int GetSameTeamOrder(Frame frame, PlayerTeam team, PlayerRef me)
        {
            int order = 0;
            var everyone = frame.Filter<PlayerStatus>();
            while (everyone.NextUnsafe(out var eRef, out var ps))
            {
                if (ps->PlayerTeam != team) continue;
                if ((int)ps->PlayerRef < (int)me) order++;
            }
            return order;
        }

        private static FPVector3 ComputeSpawnOffset(int sameTeamOrder, FP spacingMeters)
        {
            if (sameTeamOrder <= 0)
                return FPVector3.Zero;

            int ring = 1 + (sameTeamOrder - 1) / 6;
            int idx = (sameTeamOrder - 1) % 6;

            FP twoPi = FP.FromFloat_UNSAFE(6.283185307179586f);
            FP angle = twoPi * (FP)idx / (FP)6;
            FP r = spacingMeters * (FP)ring;

            FP x = FPMath.Cos(angle) * r;
            FP z = FPMath.Sin(angle) * r;

            return new FPVector3(x, FP._0, z);
        }

        private static EntityRef ChooseSpawner(Frame frame, List<EntityRef> spawners, PlayerTeam team, PlayerRef playerRef)
        {
            if (spawners.Count == 0)
                return EntityRef.None;

            int sameTeamOrder = GetSameTeamOrder(frame, team, playerRef);
            int index = (spawners.Count == 1) ? 0 : (sameTeamOrder % spawners.Count);
            return spawners[index];
        }

        public void OnPlayerAdded(Frame frame, PlayerRef player, bool firstTime)
        {
            var cfg = frame.RuntimeConfig;
            if (cfg.SelectedBySlot != null && (int)player < cfg.SelectedBySlot.Length)
            {
                var s0 = cfg.SelectedBySlot[(int)player];
            }

            RuntimePlayer runtimePlayerData = frame.GetPlayerData(player);
            Debug.Log($"[SpawnCheck] PlayerRef={player} Nick={runtimePlayerData.PlayerNickname}");
            EntityPrototype playerPrototype = frame.FindAsset<EntityPrototype>(runtimePlayerData.PlayerAvatar.Id);
            EntityRef playerEntityRef = frame.Create(playerPrototype);

            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);
            Transform3D* playerTransform = frame.Unsafe.GetPointer<Transform3D>(playerEntityRef);
            CharacterController3D* kcc = frame.Unsafe.GetPointer<CharacterController3D>(playerEntityRef);
            playerStatus->PlayerRef = player;

            {
                SelectedAbilities selFromNick = TryParseAbilitiesFromNickname(runtimePlayerData.PlayerNickname);

                SelectedAbilities selFromCfg = default;
                int slot = (int)player;
                if (cfg.SelectedBySlot != null && slot >= 0 && slot < cfg.SelectedBySlot.Length)
                    selFromCfg = cfg.SelectedBySlot[slot];

                var sel = selFromNick.IsSet ? selFromNick : selFromCfg;

                AbilityInventory* inv = frame.Unsafe.GetPointer<AbilityInventory>(playerEntityRef);
                inv->ActiveAbilityInfo.ActiveAbilityIndex = -1;

                if (!sel.IsSet)
                {
                    Quantum.Log.Info($"[Spawn] Slot {slot} IsSet=false → no abilities injected (default prefab).");
                }
                else
                {
                    AssetRef<AbilityData> GetProtoSlot(AbilityType t) => inv->Abilities[(int)t].AbilityData;

                    var srcMain1 = GetProtoSlot(sel.Main1);
                    var srcMain2 = GetProtoSlot(sel.Main2);
                    var srcUtility = GetProtoSlot(sel.Utility);
                    var srcJump = GetProtoSlot(AbilityType.Jump);
                    var srcShort = GetProtoSlot(AbilityType.ThrowShort);
                    var srcLong = GetProtoSlot(AbilityType.ThrowLong);

                    ClearAllAbilities(inv);

                    if (srcMain1.IsValid)
                    {
                        ref var m1 = ref inv->Abilities[(int)AbilityType.Attack];
                        m1.AbilityData = srcMain1;
                        ResetAbilityRuntime(ref m1, AbilityType.Attack);
                    }
                    if (srcMain2.IsValid)
                    {
                        ref var m2 = ref inv->Abilities[(int)AbilityType.Block];
                        m2.AbilityData = srcMain2;
                        ResetAbilityRuntime(ref m2, AbilityType.Block);
                    }
                    if (srcUtility.IsValid)
                    {
                        ref var util = ref inv->Abilities[(int)sel.Utility];
                        util.AbilityData = srcUtility;
                        ResetAbilityRuntime(ref util, sel.Utility);
                    }
                    if (srcJump.IsValid)
                    {
                        ref var j = ref inv->Abilities[(int)AbilityType.Jump];
                        j.AbilityData = srcJump;
                        ResetAbilityRuntime(ref j, AbilityType.Jump);
                    }
                    if (srcShort.IsValid)
                    {
                        ref var ts = ref inv->Abilities[(int)AbilityType.ThrowShort];
                        ts.AbilityData = srcShort;
                        ResetAbilityRuntime(ref ts, AbilityType.ThrowShort);
                    }
                    if (srcLong.IsValid)
                    {
                        ref var tl = ref inv->Abilities[(int)AbilityType.ThrowLong];
                        tl.AbilityData = srcLong;
                        ResetAbilityRuntime(ref tl, AbilityType.ThrowLong);
                    }

                    Quantum.Log.Info($"[Spawn] Applied from nickname → U={sel.Utility}, M1={sel.Main1}, M2={sel.Main2}");
                }
            }

            bool gameStarted =
              frame.Global->GameState == GameState.Running ||
              frame.Global->GameState == GameState.GoalScored ||
              frame.Global->GameState == GameState.GameOver ||
              frame.Global->GameState == GameState.Starting;

            int teamInt;
            if (gameStarted)
            {
                int blueCount = 0, redCount = 0;
                var everyone = frame.Filter<PlayerStatus>();
                while (everyone.NextUnsafe(out var eRef, out var ps))
                {
                    if (ps->PlayerRef == player) continue;
                    if (ps->PlayerTeam == PlayerTeam.Blue) blueCount++;
                    else if (ps->PlayerTeam == PlayerTeam.Red) redCount++;
                }
                teamInt = (blueCount > redCount) ? 1 :
                          (redCount > blueCount) ? 0 :
                          (((int)player % 2) == 0 ? 0 : 1);
            }
            else
            {
                int slot0 = (int)player;
                int teamBySlot = 0;
                var init = frame.RuntimeConfig.InitialTeamBySlot;
                if (init != null && slot0 >= 0 && slot0 < init.Length)
                    teamBySlot = init[slot0];
                else
                    teamBySlot = ((slot0 + 1) % 2 == 1) ? 0 : 1;

                teamInt = (teamBySlot == 1) ? 1 : 0;
            }

            playerStatus->PlayerTeam = (teamInt == 1) ? PlayerTeam.Red : PlayerTeam.Blue;
            Debug.Log($"[Spawn] OnPlayerAdded: PlayerRef={player} → Team={playerStatus->PlayerTeam} ({(gameStarted ? "late-join balance" : "from InitialTeamBySlot")})");

            List<EntityRef> spawners = new List<EntityRef>();
            var spawnerFilter = frame.Filter<PlayerSpawner, Transform3D>();
            while (spawnerFilter.NextUnsafe(out var spawnerEntityRef, out var spawner, out var spawnerTransform))
            {
                if (spawner->PlayerTeam == playerStatus->PlayerTeam)
                    spawners.Add(spawnerEntityRef);
            }

            if (spawners.Count > 0)
            {
                int sameTeamOrder = GetSameTeamOrder(frame, playerStatus->PlayerTeam, player);
                EntityRef chosenRef = ChooseSpawner(frame, spawners, playerStatus->PlayerTeam, player);
                Transform3D* chosenTransform = frame.Unsafe.GetPointer<Transform3D>(chosenRef);

                FPVector3 offset = ComputeSpawnOffset(sameTeamOrder, SpawnSeparation);

                playerStatus->SpawnerEntityRef = chosenRef;
                playerTransform->Position = chosenTransform->Position + offset;
                playerTransform->Rotation = chosenTransform->Rotation;

                kcc->Velocity = new FPVector3(FP._0, kcc->Velocity.Y, FP._0);

                Debug.Log($"[Spawn] PlayerRef={player} Team={playerStatus->PlayerTeam} Order={sameTeamOrder} SpawnAt={playerTransform->Position}");
            }
            else
            {
                Debug.LogWarning($"[Spawn] No spawners found for team {playerStatus->PlayerTeam}");
            }
        }

        private static unsafe void ClearAllAbilities(AbilityInventory* inv)
        {
            for (int i = 0; i < inv->Abilities.Length; i++)
            {
                ref var a = ref inv->Abilities[i];
                a.AbilityData = default;
                a.InputBufferTimer = default; a.DelayTimer = default; a.DurationTimer = default; a.CooldownTimer = default;
                a.AbilityType = (AbilityType)i;
            }
        }

        private static void ResetAbilityRuntime(ref Ability a, AbilityType t)
        {
            a.InputBufferTimer = default; a.DelayTimer = default; a.DurationTimer = default; a.CooldownTimer = default;
            a.AbilityType = t;
        }

        private static SelectedAbilities TryParseAbilitiesFromNickname(string nick)
        {
            var result = new SelectedAbilities();
            if (string.IsNullOrEmpty(nick))
                return result;

            int lb = nick.IndexOf('[');
            int rb = nick.IndexOf(']');
            if (lb < 0 || rb <= lb) return result;

            var payload = nick.Substring(lb + 1, rb - lb - 1); // U:Dash;M:Attack,Block
            AbilityType util = default, m1 = default, m2 = default;
            bool haveU = false, haveM1 = false, haveM2 = false;

            foreach (var part in payload.Split(';'))
            {
                var p = part.Trim();
                if (p.StartsWith("U:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = p.Substring(2).Trim();
                    if (Enum.TryParse(v, true, out util)) haveU = true;
                }
                else if (p.StartsWith("M:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = p.Substring(2).Trim();
                    var ms = v.Split(',');
                    if (ms.Length > 0 && Enum.TryParse(ms[0].Trim(), true, out m1)) haveM1 = true;
                    if (ms.Length > 1 && Enum.TryParse(ms[1].Trim(), true, out m2)) haveM2 = true;
                }
            }

            if (haveU && haveM1 && haveM2)
            {
                result.Utility = util;
                result.Main1 = m1;
                result.Main2 = m2;
                result.IsSet = true;
            }
            return result;
        }

        public override void Update(Frame frame, ref Filter filter)
        {
            if (!frame.Global->PlayerLastConnectionState.IsSet(filter.PlayerStatus->PlayerRef))
            {
                if (filter.PlayerStatus->IsHoldingBall)
                    frame.Signals.OnBallDropped(filter.PlayerStatus->HoldingBallEntityRef);

                frame.Destroy(filter.EntityRef);
                return;
            }

            GameSettingsData gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);

            if (filter.PlayerStatus->IsRespawning)
            {
                filter.PlayerStatus->RespawnTimer.Tick(frame.DeltaTime);
                if (filter.PlayerStatus->RespawnTimer.IsDone)
                    frame.Signals.OnPlayerRespawnTimerReset(filter.EntityRef);
            }

            if (filter.Transform->Position.Y < gameSettingsData.PlayerRespawnHeight)
            {
                PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(filter.PlayerStatus->PlayerMovementData.Id);
                filter.Collider->Enabled = false;
                filter.PlayerStatus->RespawnTimer.Start(playerMovementData.RespawnDuration);

                frame.Signals.OnPlayerRespawned(filter.EntityRef, false);
                frame.Events.OnPlayerEnteredVoid(filter.EntityRef);
            }
        }

        public void OnPlayerRespawned(Frame frame, EntityRef playerEntityRef, QBoolean fullReset)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);
            Transform3D* transform = frame.Unsafe.GetPointer<Transform3D>(playerEntityRef);
            CharacterController3D* kcc = frame.Unsafe.GetPointer<CharacterController3D>(playerEntityRef);

            frame.Signals.OnActiveAbilityStopped(playerEntityRef);
            frame.Signals.OnStatusEffectsReset(playerEntityRef);

            if (playerStatus->IsHoldingBall)
                frame.Signals.OnBallRespawned(playerStatus->HoldingBallEntityRef);

            if (fullReset)
            {
                frame.Signals.OnPlayerRespawnTimerReset(playerEntityRef);
                frame.Signals.OnCooldownsReset(playerEntityRef);
            }

            List<EntityRef> spawners = new List<EntityRef>();
            var filtered = frame.Filter<PlayerSpawner, Transform3D>();
            while (filtered.NextUnsafe(out var spawnerEntityRef, out var spawner, out var spawnerTransform))
            {
                if (spawner->PlayerTeam == playerStatus->PlayerTeam)
                    spawners.Add(spawnerEntityRef);
            }

            if (spawners.Count > 0)
            {
                int sameTeamOrder = GetSameTeamOrder(frame, playerStatus->PlayerTeam, playerStatus->PlayerRef);
                EntityRef chosenRef = ChooseSpawner(frame, spawners, playerStatus->PlayerTeam, playerStatus->PlayerRef);
                Transform3D* chosenTransform = frame.Unsafe.GetPointer<Transform3D>(chosenRef);

                FPVector3 offset = ComputeSpawnOffset(sameTeamOrder, SpawnSeparation);

                playerStatus->SpawnerEntityRef = chosenRef;
                transform->Position = chosenTransform->Position + offset;
                transform->Rotation = chosenTransform->Rotation;

                kcc->Velocity = new FPVector3(FP._0, kcc->Velocity.Y, FP._0);
            }
            else
            {
                Debug.LogWarning($"[RespawnSystem] No spawners for team {playerStatus->PlayerTeam}");
            }
        }

        public void OnPlayerRespawnTimerReset(Frame frame, EntityRef playerEntityRef)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);
            PhysicsCollider3D* collider = frame.Unsafe.GetPointer<PhysicsCollider3D>(playerEntityRef);

            collider->Enabled = true;
            playerStatus->RespawnTimer.Reset();
        }
    }
}
