using Photon.Deterministic;

namespace Quantum
{
    public unsafe class BallSpawnSystem : SystemMainThreadFilter<BallSpawnSystem.Filter>, ISignalOnBallSpawned, ISignalOnBallRespawned, ISignalOnBallDespawned
    {
        public struct Filter
        {
            public EntityRef EntityRef;
            public BallStatus* BallStatus;
            public Transform3D* Transform;
        }

        public override void Update(Frame frame, ref Filter filter)
        {
            GameSettingsData gameSettingsData = frame.FindAsset<GameSettingsData>(frame.RuntimeConfig.GameSettingsData.Id);

            if (filter.Transform->Position.Y < gameSettingsData.BallRespawnHeight)
            {
                frame.Signals.OnBallRespawned(filter.EntityRef);
            }
        }

        public void OnBallSpawned(Frame frame)
        {
            EntityPrototype ballPrototype = frame.FindAsset<EntityPrototype>(frame.RuntimeConfig.BallPrototype.Id);
            EntityRef ballEntityRef = frame.Create(ballPrototype);

            frame.Signals.OnBallRespawned(ballEntityRef);
        }

        public void OnBallRespawned(Frame frame, EntityRef ballEntityRef)
        {
            BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(ballEntityRef);
            BallHandlingData ballHandlingData = frame.FindAsset<BallHandlingData>(ballStatus->BallHandlingData.Id);
            Transform3D* transform = frame.Unsafe.GetPointer<Transform3D>(ballEntityRef);
            PhysicsBody3D* physicsBody = frame.Unsafe.GetPointer<PhysicsBody3D>(ballEntityRef);

            if (ballStatus->IsHeldByPlayer)
            {
                frame.Signals.OnBallReleased(ballEntityRef);
            }

            frame.Signals.OnBallPhysicsReset(ballEntityRef);

            transform->Position = GetBallSpawnPosition(frame, transform->Position);
            physicsBody->AddLinearImpulse(ballHandlingData.RespawnImpulse);
        }

        public void OnBallDespawned(Frame frame, EntityRef ballEntityRef)
        {
            BallStatus* ballStatus = frame.Unsafe.GetPointer<BallStatus>(ballEntityRef);

            if (ballStatus->IsHeldByPlayer)
            {
                frame.Signals.OnBallReleased(ballEntityRef);
            }

            frame.Destroy(ballEntityRef);
        }

        private FPVector3 GetBallSpawnPosition(Frame frame, FPVector3 ballPosition)
        {
            FPVector3 spawnPosition = FPVector3.Zero;
            FP closestSpawnerDistnace = FP.UseableMax;

            var filtered = frame.Filter<BallSpawner, Transform3D>();
            while (filtered.NextUnsafe(out var _, out var _, out var spawnerTransform))
            {
                FP spawnerDistance = FPVector3.Distance(ballPosition, spawnerTransform->Position);
                if (closestSpawnerDistnace > spawnerDistance)
                {
                    closestSpawnerDistnace = spawnerDistance;
                    spawnPosition = spawnerTransform->Position;
                }
            }

            return spawnPosition;
        }
    }
}
