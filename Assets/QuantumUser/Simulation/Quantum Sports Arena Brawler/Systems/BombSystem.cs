using System;
using Photon.Deterministic;
using Quantum;

public unsafe class BombSystem : SystemMainThread, ISignalOnComponentAdded<BombState>
{
    public override void Update(Frame frame)
    {
        var it = frame.Filter<BombState, Transform3D>();
        while (it.Next(out EntityRef bomb, out BombState _, out Transform3D _))
        {
            var st = frame.Unsafe.GetPointer<BombState>(bomb);
            var tr = frame.Unsafe.GetPointer<Transform3D>(bomb);

            st->LifeTimeLeft -= frame.DeltaTime;
            if (st->LifeTimeLeft <= FP._0)
            {
                frame.Destroy(bomb);
                continue;
            }

            if (!(st->Finished || st->PathCount < 2 || st->PathTotalLen <= FP._0 || st->PathSpeed <= FP._0))
            {
                st->PathDist += st->PathSpeed * frame.DeltaTime;

                FP remaining = st->PathTotalLen - st->PathDist;
                if (remaining <= st->HandoffDistance)
                {
                    ApplyPhysicsAndFinish(frame, bomb, st, tr);
                    continue;
                }

                if (st->PathDist >= st->PathTotalLen)
                {
                    tr->Position = st->Path[st->PathCount - 1];
                    var finalDir = st->Path[st->PathCount - 1] - st->Path[st->PathCount - 2];
                    if (finalDir.SqrMagnitude > FP._0)
                        tr->Rotation = FPQuaternion.LookRotation(finalDir.Normalized, FPVector3.Up);
                    EnablePhysics(frame, bomb, st, tr, FPVector3.Zero);
                    st->Finished = true;
                    continue;
                }

                FP distLeft = st->PathDist;
                int segIdx = 0;
                for (int i = 1; i < st->PathCount; i++)
                {
                    FP segLen = (st->Path[i] - st->Path[i - 1]).Magnitude;
                    if (distLeft <= segLen) { segIdx = i - 1; break; }
                    distLeft -= segLen;
                }
                if (segIdx >= st->PathCount - 1) segIdx = st->PathCount - 2;

                FPVector3 p0 = st->Path[segIdx];
                FPVector3 p1 = st->Path[segIdx + 1];
                FPVector3 segVec = (p1 - p0);
                FP segLenFollow = segVec.Magnitude;

                if (segLenFollow > FP._0)
                {
                    FP tt = FPMath.Clamp(distLeft / segLenFollow, FP._0, FP._1);
                    tr->Position = p0 + segVec * tt;
                    if (segVec.SqrMagnitude > FP._0)
                        tr->Rotation = FPQuaternion.LookRotation(segVec.Normalized, FPVector3.Up);
                }
            }

            HandleCollisionsAndFuse(frame, bomb, st, tr);
        }
    }

    private static void ApplyPhysicsAndFinish(Frame frame, EntityRef bomb, BombState* st, Transform3D* tr)
    {
        int segIdx = Math.Max(0, st->PathCount - 2);

        FPVector3 delta = st->Path[segIdx + 1] - st->Path[segIdx];
        if (delta.SqrMagnitude <= FP._0)
            delta = FPVector3.Forward;

        FPVector3 dir = delta.Normalized;

        FP slopeY = FPMath.Clamp(
            delta.Y / (delta.Magnitude + FP.FromFloat_UNSAFE(0.001f)),
            FP.FromFloat_UNSAFE(-0.5f),
            FP.FromFloat_UNSAFE(0.5f)
        );

        FPVector3 launchVel = dir * st->PathSpeed;
        launchVel.Y += FPMath.Max(FP._0, slopeY * st->PathSpeed)
                       + (st->PathSpeed * FP.FromFloat_UNSAFE(0.25f));

        EnablePhysics(frame, bomb, st, tr, launchVel);
        st->Finished = true;
    }

    private static void EnablePhysics(Frame frame, EntityRef bomb, BombState* st, Transform3D* tr, FPVector3 vel)
    {
        if (frame.Has<PhysicsBody3D>(bomb))
        {
            var body = frame.Unsafe.GetPointer<PhysicsBody3D>(bomb);
            body->IsKinematic = false;
            body->AngularVelocity = FPVector3.Zero;
            body->Velocity = vel;
        }
        if (frame.Has<PhysicsCollider3D>(bomb))
        {
            var col = frame.Unsafe.GetPointer<PhysicsCollider3D>(bomb);
            col->Enabled = true;
        }
    }

    private static void HandleCollisionsAndFuse(Frame frame, EntityRef bomb, BombState* st, Transform3D* tr)
    {
        if (!st->Exploded && st->ContactTriggerRadius > FP._0)
        {
            GameSettingsData gs = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
            var touchSphere = Shape3D.CreateSphere(st->ContactTriggerRadius);
            var touch = frame.Physics3D.OverlapShape(*tr, touchSphere, gs.PlayerLayerMask, QueryOptions.HitKinematics);

            for (int i = 0; i < touch.Count; i++)
            {
                var tgt = touch[i].Entity;
                if (!frame.Has<PlayerStatus>(tgt)) continue;
                var tps = frame.Unsafe.GetPointer<PlayerStatus>(tgt);
                if (tps->PlayerTeam.Equals(st->OwnerTeam)) continue;
                ExplodeAndDestroy(frame, bomb, st, tr);
                return;
            }
        }

        if (frame.Has<PhysicsBody3D>(bomb))
        {
            var body = frame.Unsafe.GetPointer<PhysicsBody3D>(bomb);
            if (!body->IsKinematic)
            {
                FP sqrSpeed = body->Velocity.SqrMagnitude;
                st->GroundFuseArmed = sqrSpeed <= FP.FromFloat_UNSAFE(0.0025f);
                if (st->GroundFuseArmed)
                {
                    st->GroundFuseLeft -= frame.DeltaTime;
                    if (st->GroundFuseLeft <= FP._0)
                        ExplodeAndDestroy(frame, bomb, st, tr);
                }
            }
        }
    }

    private static void ExplodeAndDestroy(Frame frame, EntityRef bomb, BombState* st, Transform3D* tr)
    {
        if (st->Exploded)
            return;

        st->Exploded = true;

        GameSettingsData gs = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);
        FP r = st->ExplosionRadius;
        var aoeSphere = Shape3D.CreateSphere(r);
        var hits = frame.Physics3D.OverlapShape(*tr, aoeSphere, gs.PlayerLayerMask, QueryOptions.HitKinematics);

        for (int i = 0; i < hits.Count; i++)
        {
            var target = hits[i].Entity;
            if (!frame.Has<PlayerStatus>(target))
                continue;

            var tps = frame.Unsafe.GetPointer<PlayerStatus>(target);
            if (tps->PlayerTeam.Equals(st->OwnerTeam))
                continue;

            var tTr = frame.Unsafe.GetPointer<Transform3D>(target);
            var inv = frame.Unsafe.GetPointer<AbilityInventory>(target);

            FPVector3 dir = tTr->Position - tr->Position;
            dir.Y = FP._0;
            if (dir.SqrMagnitude > FP._0)
                dir = dir.Normalized;
            else
                dir = FPVector3.Forward;

            if (inv->IsBlocking)
                frame.Events.OnPlayerBlockHit(target, dir);
            else
            {
                frame.Signals.OnKnockbackApplied(target, FP.FromFloat_UNSAFE(0.25f), dir);
                frame.Events.OnPlayerHit(target);
            }
        }

        frame.Destroy(bomb);
    }

    public void OnAdded(Frame frame, EntityRef entity, BombState* comp)
    {
        comp->Finished = false;
        comp->PathCount = 0;
        comp->PathTotalLen = FP._0;
        comp->PathDist = FP._0;
        comp->PathSpeed = FP._0;
        comp->HandoffDistance = FP.FromFloat_UNSAFE(0.75f);
        comp->DownwardBias = FP.FromFloat_UNSAFE(2.0f);
        comp->ExplosionRadius = FP.FromFloat_UNSAFE(2.5f);
        comp->LifeTimeLeft = FP.FromFloat_UNSAFE(5.0f);
        comp->GroundFuseLeft = FP.FromFloat_UNSAFE(2.0f);
        comp->ContactTriggerRadius = FP.FromFloat_UNSAFE(0.4f);
        comp->GroundFuseArmed = false;
        comp->Exploded = false;
    }
}
