using Photon.Deterministic;

namespace Quantum
{
    public partial class GameSettingsData : AssetObject
    {
        public FP InitializationDuration = 3;
        public FP GameStartDuration = 3;
        public FP GameDuration = 300;
        public FP GoalDuration = 3;
        public FP GameOverDuration = 3;

        public FP PlayerRespawnHeight = -20;
        public FP BallRespawnHeight = -20;

        public LayerMask PlayerLayerMask;

        // NEW: 0 = disabled. If > 0, first team to reach this score wins.
        public int ScoreToWin = 0;
    }
}
