namespace Quantum
{
    public partial struct PlayerStatus
    {
        public bool IsHoldingBall => HoldingBallEntityRef != default;

        public bool IsRespawning => RespawnTimer.IsRunning;
        public bool IsStunned => StunStatusEffect.DurationTimer.IsRunning;
        public bool IsKnockbacked => KnockbackStatusEffect.DurationTimer.IsRunning;
        public bool IsIncapacitated => IsRespawning || IsStunned || IsKnockbacked;
    }
}
