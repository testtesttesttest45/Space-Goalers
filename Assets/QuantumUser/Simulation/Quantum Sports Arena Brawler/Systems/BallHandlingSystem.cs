using Photon.Deterministic;
using Quantum.Physics3D;

namespace Quantum
{
    public unsafe class BallHandlingSystem : SystemMainThreadFilter<BallHandlingSystem.Filter>, ISignalOnBallReleased, ISignalOnBallDropped, ISignalOnBallPhysicsReset, ISignalOnCollisionEnter3D, ISignalOnCollision3D
    {
        public struct Filter
        {
            public EntityRef EntityRef;
            public BallStatus* BallStatus;
            public Transform3D* Transform;
            public PhysicsBody3D* PhysicsBody;
            public PhysicsCollider3D* Collider;
        }

        public override void Update(Frame frame, ref Filter filter)
        {
            BallHandlingData ballHandlingData = frame.FindAsset<BallHandlingData>(filter.BallStatus->BallHandlingData.Id);

            if (filter.BallStatus->IsHeldByPlayer)
            {
                CarryBall(frame, ref filter, ballHandlingData);
            }
            else
            {
                AttemptCatchBall(frame, ref filter, ballHandlingData);
            }

            UpdateBallGravityScale(frame, ref filter, ballHandlingData);
            HandleBallCollisions(frame, ref filter, ballHandlingData);

            filter.BallStatus->CatchTimeoutTimer.Tick(frame.DeltaTime);
        }

        private void AttemptCatchBall(Frame frame, ref Filter filter, BallHandlingData ballHandlingData)
        {
            GameSettingsData gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);

            Shape3D sphereShape = Shape3D.CreateSphere(ballHandlingData.CatchRadius);
            HitCollection3D hitCollection = frame.Physics3D.OverlapShape(filter.Transform->Position, FPQuaternion.Identity, sphereShape, gameSettingsData.PlayerLayerMask);

            hitCollection.SortCastDistance();
            for (int i = 0; i < hitCollection.Count; i++)
            {
                Hit3D hit = hitCollection[i];

                if (!CanCatchBall(frame, ref filter, hit.Entity))
                {
                    continue;
                }

                CatchBall(frame, ref filter, hit.Entity, ballHandlingData);
                break;
            }
        }

        private bool CanCatchBall(Frame frame, ref Filter filter, EntityRef playerEntityRef)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);

            if (playerStatus->IsIncapacitated)
            {
                return false;
            }

            if (playerStatus->IsHoldingBall)
            {
                return false;
            }

            if (filter.BallStatus->CatchTimeoutTimer.IsRunning)
            {
                return filter.BallStatus->CatchTimeoutPlayerRef != playerStatus->PlayerRef;
            }

            return true;
        }

        private void CatchBall(Frame frame, ref Filter filter, EntityRef playerEntityRef, BallHandlingData ballHandlingData)
        {
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);
            PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(playerStatus->PlayerMovementData.Id);

            playerStatus->HoldingBallEntityRef = filter.EntityRef;
            filter.BallStatus->HoldingPlayerEntityRef = playerEntityRef;

            filter.Collider->Enabled = false;
            filter.PhysicsBody->IsKinematic = true;
            frame.Signals.OnBallPhysicsReset(filter.EntityRef);

            playerMovementData.UpdateKCCSettings(frame, playerEntityRef);

            CarryBall(frame, ref filter, ballHandlingData);

            frame.Events.OnPlayerCaughtBall(playerEntityRef, filter.EntityRef);
        }

        private void CarryBall(Frame frame, ref Filter filter, BallHandlingData ballHandlingData)
        {
            Transform3D* playerTransform = frame.Unsafe.GetPointer<Transform3D>(filter.BallStatus->HoldingPlayerEntityRef);

            filter.Transform->Position = playerTransform->Position + (playerTransform->Rotation * ballHandlingData.DropLocalPosition);
        }

        public void OnBallReleased(Frame frame, EntityRef ballEntityRef)
        {
            BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(ballEntityRef);
            BallHandlingData ballHandlingData = frame.FindAsset<BallHandlingData>(ballStatus->BallHandlingData.Id);
            PhysicsBody3D* ballPhysicsBody = frame.Unsafe.GetPointer<PhysicsBody3D>(ballEntityRef);
            PhysicsCollider3D* ballCollider = frame.Unsafe.GetPointer<PhysicsCollider3D>(ballEntityRef);

            EntityRef playerEntityRef = ballStatus->HoldingPlayerEntityRef;
            PlayerStatus* playerStatus = frame.Unsafe.GetPointer<PlayerStatus>(playerEntityRef);
            PlayerMovementData playerMovementData = frame.FindAsset<PlayerMovementData>(playerStatus->PlayerMovementData.Id);

            ballStatus->HoldingPlayerEntityRef = default;
            ballStatus->CatchTimeoutTimer.Start(ballHandlingData.CatchTimeout);

            ballCollider->Enabled = true;
            ballPhysicsBody->IsKinematic = false;

            playerStatus->HoldingBallEntityRef = default;
            ballStatus->CatchTimeoutPlayerRef = playerStatus->PlayerRef;

            playerMovementData.UpdateKCCSettings(frame, playerEntityRef);
        }

        public void OnBallDropped(Frame frame, EntityRef ballEntityRef)
        {
            BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(ballEntityRef);
            BallHandlingData ballHandlingData = frame.FindAsset<BallHandlingData>(ballStatus->BallHandlingData.Id);
            Transform3D* ballTransform = frame.Unsafe.GetPointer<Transform3D>(ballEntityRef);
            PhysicsBody3D* ballPhysicsBody = frame.Unsafe.GetPointer<PhysicsBody3D>(ballEntityRef);

            Transform3D* playerTransform = frame.Unsafe.GetPointer<Transform3D>(ballStatus->HoldingPlayerEntityRef);

            frame.Signals.OnBallReleased(ballEntityRef);

            ballTransform->Position = playerTransform->Position + (playerTransform->Rotation * ballHandlingData.DropLocalPosition);

            FPVector3 dropImpulse = new FPVector3(
                frame.RNG->NextInclusive(ballHandlingData.DropMinImpulse.X, ballHandlingData.DropMaxImpulse.X),
                frame.RNG->NextInclusive(ballHandlingData.DropMinImpulse.Y, ballHandlingData.DropMaxImpulse.Y),
                frame.RNG->NextInclusive(ballHandlingData.DropMinImpulse.Z, ballHandlingData.DropMaxImpulse.Z));

            FPVector3 impulseRelativePoint = ballPhysicsBody->CenterOfMass;
            impulseRelativePoint.Y += ballHandlingData.DropImpulseOffsetY;

            ballPhysicsBody->AddLinearImpulse(playerTransform->Rotation * dropImpulse, impulseRelativePoint);
        }

        public void OnBallPhysicsReset(Frame frame, EntityRef ballEntityRef)
        {
            PhysicsBody3D* physicsBody = frame.Unsafe.GetPointer<PhysicsBody3D>(ballEntityRef);

            physicsBody->Velocity = FPVector3.Zero;
            physicsBody->AngularVelocity = FPVector3.Zero;

            ResetBallGravity(frame, ballEntityRef);
        }

        private void UpdateBallGravityScale(Frame frame, ref Filter filter, BallHandlingData ballHandlingData)
        {
            if (filter.BallStatus->GravityChangeTimer.IsRunning)
            {
                FP gravityScale = ballHandlingData.ThrowGravityChangeCurve.Evaluate(filter.BallStatus->GravityChangeTimer.NormalizedTime);
                filter.PhysicsBody->GravityScale = gravityScale;

                filter.BallStatus->GravityChangeTimer.Tick(frame.DeltaTime);
                if (filter.BallStatus->GravityChangeTimer.IsDone)
                {
                    ResetBallGravity(frame, filter.EntityRef);
                }
            }
        }

        private void ResetBallGravity(Frame frame, EntityRef ballEntityRef)
        {
            BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(ballEntityRef);
            PhysicsBody3D* physicsBody = frame.Unsafe.GetPointer<PhysicsBody3D>(ballEntityRef);

            ballStatus->GravityChangeTimer.Reset();
            physicsBody->GravityScale = FP._1;
        }

        public void OnCollisionEnter3D(Frame frame, CollisionInfo3D info)
        {
            if (frame.Unsafe.TryGetPointer(info.Entity, out BallStatus* ballStatus))
            {
                ballStatus->HasCollisionEnter = true;
            }
        }

        public void OnCollision3D(Frame frame, CollisionInfo3D info)
        {
            if (frame.Unsafe.TryGetPointer(info.Entity, out BallStatus* ballStatus))
            {
                ballStatus->HasCollision = true;
            }
        }

        private void HandleBallCollisions(Frame frame, ref Filter filter, BallHandlingData ballHandlingData)
        {
            if (!filter.PhysicsBody->IsKinematic)
            {
                if (filter.BallStatus->HasCollisionEnter)
                {
                    filter.PhysicsBody->Velocity.X *= ballHandlingData.LateralBounceFriction;
                    filter.PhysicsBody->Velocity.Z *= ballHandlingData.LateralBounceFriction;

                    frame.Events.OnBallBounced(filter.EntityRef);
                }

                if (filter.BallStatus->HasCollision)
                {
                    filter.PhysicsBody->Velocity.X *= ballHandlingData.LateralGroundFriction;
                    filter.PhysicsBody->Velocity.Z *= ballHandlingData.LateralGroundFriction;
                }
            }

            filter.BallStatus->HasCollisionEnter = false;
            filter.BallStatus->HasCollision = false;
        }
    }
}
