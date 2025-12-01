using Photon.Deterministic;
using Quantum.Inspector;

namespace Quantum
{
    public partial class KnockbackStatusEffectData : AssetObject
    {
        public FP KnockbackDistanceXZ = 6;
        public FP KnockbackDistanceY = 1;
        public FPAnimationCurve KnockbackCurveXZ;
        public FPAnimationCurve KnockbackCurveY;
    }
}
