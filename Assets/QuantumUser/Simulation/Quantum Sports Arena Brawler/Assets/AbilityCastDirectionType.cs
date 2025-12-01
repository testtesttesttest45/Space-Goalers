using System;

namespace Quantum
{
    [Flags]
    public enum AbilityCastDirectionType
    {
        Aim = 1 << 0,
        Movement = 1 << 1,
        FacingDirection = 1 << 2,
    }
}
