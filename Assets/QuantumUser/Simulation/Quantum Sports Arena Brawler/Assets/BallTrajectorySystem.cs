using System;
using Photon.Deterministic;
using Quantum.Physics3D;

namespace Quantum
{
    public unsafe class BallTrajectorySystem : SystemMainThread, ISignalOnComponentAdded<BallTrajectoryState>
    {
        public override void Update(Frame frame)
        {
            var it = frame.Filter<BallTrajectoryState, Transform3D, PhysicsBody3D>();
            while (it.Next(out EntityRef ball, out BallTrajectoryState _, out Transform3D _, out PhysicsBody3D _))
            {
                var st = frame.Unsafe.GetPointer<BallTrajectoryState>(ball);
                var tr = frame.Unsafe.GetPointer<Transform3D>(ball);
                var body = frame.Unsafe.GetPointer<PhysicsBody3D>(ball);

                if (st->Finished || st->PathCount < 2 || st->PathTotalLen <= FP._0 || st->PathSpeed <= FP._0)
                    continue;

                st->PathDist += st->PathSpeed * frame.DeltaTime;

                if (st->PathDist >= st->PathTotalLen)
                {
                    tr->Position = st->Path[st->PathCount - 1];

                    FPVector3 finalDir = st->Path[st->PathCount - 1] - st->Path[st->PathCount - 2];
                    if (finalDir.SqrMagnitude > FP._0)
                    {
                        finalDir = finalDir.Normalized;
                        tr->Rotation = FPQuaternion.LookRotation(finalDir, FPVector3.Up);

                        body->IsKinematic = false;
                        body->AngularVelocity = FPVector3.Zero;
                        body->Velocity = finalDir * st->PathSpeed;
                    }
                    else
                    {
                        body->IsKinematic = false;
                        body->AngularVelocity = FPVector3.Zero;
                        body->Velocity = FPVector3.Zero;
                    }

                    st->Finished = true;
                    continue;
                }

                FP remainingDist = st->PathDist;
                int segIdx = 0;
                for (int i = 1; i < st->PathCount; i++)
                {
                    FP segLenAccum = (st->Path[i] - st->Path[i - 1]).Magnitude;
                    if (remainingDist <= segLenAccum)
                    {
                        segIdx = i - 1;
                        break;
                    }
                    remainingDist -= segLenAccum;
                }

                if (segIdx >= st->PathCount - 1)
                    segIdx = st->PathCount - 2;

                FPVector3 p0 = st->Path[segIdx];
                FPVector3 p1 = st->Path[segIdx + 1];
                FPVector3 segVec = p1 - p0;
                FP segLenCurrent = segVec.Magnitude;

                if (segLenCurrent > FP._0)
                {
                    FP t = FPMath.Clamp(remainingDist / segLenCurrent, FP._0, FP._1);
                    tr->Position = p0 + segVec * t;

                    if (segVec.SqrMagnitude > FP._0)
                    {
                        tr->Rotation = FPQuaternion.LookRotation(segVec.Normalized, FPVector3.Up);
                    }
                }
            }
        }

        public void OnAdded(Frame frame, EntityRef entity, BallTrajectoryState* comp)
        {
            comp->PathCount = 0;
            comp->PathTotalLen = FP._0;
            comp->PathDist = FP._0;
            comp->PathSpeed = FP._0;
            comp->Finished = false;
        }
    }
}
