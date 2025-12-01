using UnityEngine;

namespace Quantum
{
    public unsafe class GameSystem : SystemMainThread, ISignalOnGoalScored
    {
        public override void Update(Frame frame)
        {
            switch (frame.Global->GameState)
            {
                case GameState.None: UpdateGameState_None(frame); break;
                case GameState.Initializing: UpdateGameState_Initializing(frame); break;
                case GameState.Starting: UpdateGameState_Starting(frame); break;
                case GameState.Running: UpdateGameState_Running(frame); break;
                case GameState.GoalScored: UpdateGameState_GoalScored(frame); break;
                case GameState.GameOver: UpdateGameState_GameOver(frame); break;
            }
        }

        public void OnGoalScored(Frame frame, EntityRef playerEntityRef, PlayerTeam playerTeam)
        {
            // Despawn active balls for the celebration period
            DespawnBalls(frame);

            // Team total only (stored in Global)
            frame.Global->TeamScore[(int)playerTeam]++;

            // Move into goal celebration state
            ChangeGameState_GoalScored(frame, playerEntityRef, playerTeam);
        }

        private void UpdateGameState_None(Frame frame) { ChangeGameState_Initializing(frame); }

        private void UpdateGameState_Initializing(Frame frame)
        {
            frame.Global->GameStateTimer.Tick(frame.DeltaTime);
            if (frame.Global->GameStateTimer.IsDone)
                ChangeGameState_Starting(frame, true);
        }

        private void UpdateGameState_Starting(Frame frame)
        {
            frame.Global->GameStateTimer.Tick(frame.DeltaTime);
            if (frame.Global->GameStateTimer.IsDone)
            {
                ToggleTeamBaseStaticColliders(frame, false);
                frame.Signals.OnBallSpawned();
                ChangeGameState_Running(frame);
            }
        }

        private void UpdateGameState_Running(Frame frame)
        {
            // Regular time-based end
            frame.Global->MainGameTimer.Tick(frame.DeltaTime);
            if (frame.Global->MainGameTimer.IsDone)
            {
                ChangeGameState_GameOver(frame);
                return;
            }

            // Safety: if someone somehow reached ScoreToWin while "running", end immediately
            var settings = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            if (settings.ScoreToWin > 0)
            {
                if (frame.Global->TeamScore[0] >= settings.ScoreToWin ||
                    frame.Global->TeamScore[1] >= settings.ScoreToWin)
                {
                    ChangeGameState_GameOver(frame);
                    return;
                }
            }
        }

        private void UpdateGameState_GoalScored(Frame frame)
        {
            frame.Global->GameStateTimer.Tick(frame.DeltaTime);
            if (!frame.Global->GameStateTimer.IsDone)
                return;

            // After celebration, decide whether to end or continue
            var settings = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            if (settings.ScoreToWin > 0)
            {
                if (frame.Global->TeamScore[0] >= settings.ScoreToWin ||
                    frame.Global->TeamScore[1] >= settings.ScoreToWin)
                {
                    // Winner already established by score; go to GameOver
                    ChangeGameState_GameOver(frame);
                    return;
                }
            }

            // Otherwise continue the match
            RespawnPlayers(frame);
            ToggleTeamBaseStaticColliders(frame, true);
            ChangeGameState_Starting(frame, false);
        }

        private void UpdateGameState_GameOver(Frame frame) { /* stay */ }

        private void ChangeGameState_Initializing(Frame frame)
        {
            var gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            frame.Global->GameStateTimer.Start(gameSettingsData.InitializationDuration);
            frame.Global->GameState = GameState.Initializing;
            frame.Events.OnGameInitializing();
        }

        private void ChangeGameState_Starting(Frame frame, bool isFirst)
        {
            var gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            frame.Global->GameStateTimer.Start(gameSettingsData.GameStartDuration);
            frame.Global->GameState = GameState.Starting;
            frame.Events.OnGameStarting(isFirst);
        }

        private void ChangeGameState_Running(Frame frame)
        {
            var gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            if (frame.Global->MainGameTimer.IsDone)
                frame.Global->MainGameTimer.Start(gameSettingsData.GameDuration);
            frame.Global->GameState = GameState.Running;
            frame.Events.OnGameRunning();
        }

        private void ChangeGameState_GoalScored(Frame frame, EntityRef playerEntityRef, PlayerTeam playerTeam)
        {
            var gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            frame.Global->GameStateTimer.Start(gameSettingsData.GoalDuration);
            frame.Global->GameState = GameState.GoalScored;
            frame.Events.OnGoalScored(playerEntityRef, playerTeam);
        }

        private void ChangeGameState_GameOver(Frame frame)
        {
            var gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            frame.Global->GameStateTimer.Start(gameSettingsData.GameOverDuration);
            frame.Global->GameState = GameState.GameOver;
            frame.Events.OnGameOver();
        }

        private void DespawnBalls(Frame frame)
        {
            foreach (var (ballEntityRef, _) in frame.Unsafe.GetComponentBlockIterator<BallStatus>())
                frame.Signals.OnBallDespawned(ballEntityRef);
        }

        private void RespawnPlayers(Frame frame)
        {
            foreach (var (playerEntityRef, _) in frame.Unsafe.GetComponentBlockIterator<PlayerStatus>())
                frame.Signals.OnPlayerRespawned(playerEntityRef, true);
        }

        private void ToggleTeamBaseStaticColliders(Frame frame, bool enabled)
        {
            var filtered = frame.Filter<TeamBaseWallStaticColliderTag, StaticColliderLink>();
            while (filtered.Next(out _, out _, out var wallColliderLink))
                frame.Physics3D.SetStaticColliderEnabled(wallColliderLink.StaticColliderIndex, enabled);
        }
    }
}
