using System;
using Photon.Deterministic;
using UnityEngine;

namespace Quantum
{
    public static class BombTrajectoryBuffer
    {
        public static void StoreUnity(Vector3[] src)
        {
            int count = src != null ? src.Length : 0;
        }

        public static FPVector3[] PeekOrDefault(FPVector3 start, FPVector3 dir, FP strength)
        {
            const int SEGMENTS = 12;

            FP minDist = FP.FromFloat_UNSAFE(5f);
            FP maxDist = FP.FromFloat_UNSAFE(26f);
            FP minArcH = FP.FromFloat_UNSAFE(5f);
            FP maxArcH = FP.FromFloat_UNSAFE(10f);
            FP PI = FP.FromFloat_UNSAFE(3.1415926f);

            strength = FPMath.Clamp01(strength);

            FP totalDist = FPMath.Lerp(minDist, maxDist, strength);
            FP arcHeight = FPMath.Lerp(maxArcH, minArcH, strength);

            if (dir.SqrMagnitude > FP._0)
                dir = dir.Normalized;
            else
                dir = FPVector3.Forward;

            var pts = new FPVector3[SEGMENTS];

            for (int i = 0; i < SEGMENTS; i++)
            {
                FP t = FP.FromFloat_UNSAFE(i / (float)(SEGMENTS - 1));
                FP y = arcHeight * FPMath.Sin(PI * t);

                FP along = totalDist * t;
                FPVector3 p = start + dir * along + FPVector3.Up * y;
                pts[i] = p;
            }

            return pts;
        }
    }

}
