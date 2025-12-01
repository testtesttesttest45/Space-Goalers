namespace Quantum
{
    public partial struct BallStatus
    {
        public bool IsHeldByPlayer => HoldingPlayerEntityRef != default;
    }
}
