using Photon.Deterministic;

namespace Quantum
{
    /// Push/carry players that are in front of the dasher while the dash is active.
    /// - Works with your AbilityInventory array (no GetAbility helper needed).
    /// - Uses deterministic math (FPMath).
    /// - Keeps things stable: minimum forward speed, small positional latch, ahead-only check.
    public unsafe class DashCarrySystem
      : SystemMainThreadFilter<DashCarrySystem.Filter>
    {
        // ===== Tunables =====
        // Extra radius added to the dasher's capsule to make the "nose" a bit wider.
        public FP ExtraRadius = FP.FromFloat_UNSAFE(0.35f);

        // Small separation so colliders don't start overlapped.
        public FP Padding = FP.FromFloat_UNSAFE(0.10f);

        // If target is farther than contact + StickMargin, we skip latching.
        public FP StickMargin = FP.FromFloat_UNSAFE(0.25f);

        // Only affect actors generally "ahead" of the dash.
        public FP AheadDotMin = FP.FromFloat_UNSAFE(0.0f);  // 0 = in front hemisphere

        // Blend factor to copy the dasher's horizontal velocity onto the target (0..1).
        public FP Blend = FP.FromFloat_UNSAFE(1.00f);

        // Extra forward boost applied to targets so they don't drift behind.
        public FP ExtraFwdBoost = FP.FromFloat_UNSAFE(4.0f);

        // Minimum carry speed: if the dasher is barely moving, don't try to carry.
        public FP MinCarrySpeed = FP.FromFloat_UNSAFE(2.0f);

        // Positional latch gain: how tightly we try to place the target just ahead of the dasher’s nose.
        public FP PosLatchGain = FP.FromFloat_UNSAFE(1.0f);

        // Strength of a spring-like forward pull to close any small remaining gap.
        public FP PullGain = FP.FromFloat_UNSAFE(60.0f);

        // Team filtering
        public bool OnlyEnemies = true;

        // ===== Filter for "dasher" A =====
        public struct Filter
        {
            public EntityRef Entity;
            public PlayerStatus* PS;
            public Transform3D* TR;
            public CharacterController3D* KCC;
            public AbilityInventory* Inv;
        }

        public override void Update(Frame f, ref Filter a)
        {
            // Is dash ability currently delayed/active?
            ref Ability dash = ref a.Inv->Abilities[(int)AbilityType.Dash];
            bool dashActive = dash.DelayTimer.IsRunning || dash.DurationTimer.IsRunning;
            if (!dashActive)
                return;

            // Dasher forward (XZ) based on frozen CastDirection from your dash ability
            FPVector3 cast = a.Inv->ActiveAbilityInfo.CastDirection;
            FPVector3 fwd = new FPVector3(cast.X, FP._0, cast.Z);
            FP flen = fwd.Magnitude;
            fwd = (flen > FP._0) ? (fwd / flen) : new FPVector3(FP._0, FP._0, FP._1);

            // Dasher horizontal speed
            FPVector3 aVelXZ = new FPVector3(a.KCC->Velocity.X, FP._0, a.KCC->Velocity.Z);
            if (aVelXZ.Magnitude < MinCarrySpeed)
                return;

            // Effective "nose" radius for A
            FP aR = GetKccRadiusForEntity(f, a.PS) + ExtraRadius;

            // Iterate potential targets B
            var itB = f.Filter<PlayerStatus, Transform3D, CharacterController3D>();
            while (itB.NextUnsafe(out var bRef, out var bPS, out var bTR, out var bKCC))
            {
                if (bRef == a.Entity) continue;
                if (OnlyEnemies && bPS->PlayerTeam == a.PS->PlayerTeam) continue;

                // Vector from A to B in XZ
                FPVector3 toB = new FPVector3(bTR->Position.X - a.TR->Position.X, FP._0, bTR->Position.Z - a.TR->Position.Z);
                FP dist = toB.Magnitude;
                FPVector3 dir = (dist > FP._0) ? (toB / dist) : fwd;

                // Ahead-only
                FP ahead = FPVector3.Dot(fwd, dir);
                if (ahead < AheadDotMin)
                    continue;

                // Target radius
                FP bR = GetKccRadiusForEntity(f, bPS);
                FP contactR = aR + bR + Padding;

                // If too far from the nose, skip
                if (dist > (contactR + StickMargin))
                    continue;

                // Desired target velocity: match dasher + a small forward boost
                FPVector3 bVelXZ = new FPVector3(bKCC->Velocity.X, FP._0, bKCC->Velocity.Z);
                FPVector3 wanted = aVelXZ + fwd * ExtraFwdBoost;
                FPVector3 deltaV = (wanted - bVelXZ) * Blend;

                // Apply horizontal velocity change
                bKCC->Velocity = new FPVector3(
                    bKCC->Velocity.X + deltaV.X,
                    bKCC->Velocity.Y,
                    bKCC->Velocity.Z + deltaV.Z
                );

                // Forward pull to close remaining gap to the nose
                FP targetDist = contactR;     // sit just ahead of the dasher’s capsule
                FP gap = targetDist - dist;
                if (gap > FP._0)
                {
                    // spring-like pull forward; proportional to remaining gap and dt
                    FPVector3 pull = fwd * (gap * PullGain * f.DeltaTime);
                    bKCC->Velocity += pull;
                }

                // Positional latch: gently place B at the nose point in front of A
                FP desX = a.TR->Position.X + fwd.X * targetDist;
                FP desZ = a.TR->Position.Z + fwd.Z * targetDist;
                FP corrX = desX - bTR->Position.X;
                FP corrZ = desZ - bTR->Position.Z;

                FP invDt = (f.DeltaTime > FP._0) ? (FP._1 / f.DeltaTime) : FP.FromFloat_UNSAFE(1000f);
                FPVector3 corrV = new FPVector3(corrX * invDt * PosLatchGain, FP._0, corrZ * invDt * PosLatchGain);
                bKCC->Velocity += corrV;
            }
        }

        // === Helpers ===
        private static FP GetKccRadiusForEntity(Frame f, PlayerStatus* ps)
        {
            var move = f.FindAsset<PlayerMovementData>(ps->PlayerMovementData.Id);
            if (move == null || !move.DefaultKCCSettings.IsValid)
                return FP.FromFloat_UNSAFE(0.5f);

            var cfg = f.FindAsset<CharacterController3DConfig>(move.DefaultKCCSettings.Id);
            return (cfg != null) ? cfg.Radius : FP.FromFloat_UNSAFE(0.5f);
        }
    }
}
