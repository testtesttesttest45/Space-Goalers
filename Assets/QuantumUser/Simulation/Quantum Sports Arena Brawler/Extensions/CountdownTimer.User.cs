using Photon.Deterministic;

namespace Quantum
{
    public unsafe partial struct CountdownTimer
    {
        public bool IsRunning => TimeLeft > FP._0;
        public bool IsDone => !IsRunning;

        public FP NormalizedTimeLeft
        {
            get
            {
                if (IsDone)
                {
                    return FP._0;
                }

                return TimeLeft / StartTime;
            }
        }

        public FP NormalizedTime => FP._1 - NormalizedTimeLeft;

        public void Start(FP time)
        {
            time = FPMath.Max(time, FP.Epsilon);

            StartTime = time;
            TimeLeft = time;
        }

        public void Tick(FP deltaTime)
        {
            if (IsDone)
            {
                return;
            }

            TimeLeft -= deltaTime;
            if (TimeLeft < FP._0)
            {
                Reset();
            }
        }

        public void Reset()
        {
            TimeLeft = FP._0;
        }
    }
}
