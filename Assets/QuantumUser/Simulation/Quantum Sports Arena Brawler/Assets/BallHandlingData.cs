using Photon.Deterministic;

namespace Quantum
{
    public partial class BallHandlingData : AssetObject
    {
        public FP CatchRadius = 1;
        public FP CatchTimeout = 1;

        public FPVector3 RespawnImpulse = new FPVector3(0, 8, 0);
        public FPVector3 DropLocalPosition = new FPVector3(0, FP._0_50, 0);
        public FPVector3 DropMinImpulse = new FPVector3(-3, 16, -3);
        public FPVector3 DropMaxImpulse = new FPVector3(3, 20, 3);
        public FP DropImpulseOffsetY = 1;

        public FPAnimationCurve ThrowGravityChangeCurve;

        public FP LateralBounceFriction = FP._0_05;
        public FP LateralGroundFriction = FP._0_05;
    }
}
