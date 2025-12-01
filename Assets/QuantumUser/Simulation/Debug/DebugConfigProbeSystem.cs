using System.Reflection;

namespace Quantum
{
    // Simple probe that logs once on the first Update in the simulation
    public unsafe class DebugConfigProbeSystem : SystemMainThread
    {
        private bool _logged;

        public override void Update(Frame frame)
        {
            if (_logged) return;
            _logged = true;

            var m = typeof(RuntimeConfig).GetMethod(
              "SerializeUserData",
              BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            );

            Quantum.Log.Info($"[RCUD-Probe] SerializeUserData present in sim? {(m != null ? "YES" : "NO")}  Assembly={typeof(RuntimeConfig).Assembly.FullName}");

            var rc = frame.RuntimeConfig;
            Quantum.Log.Info($"[RCUD-Probe] SelectedBySlot len={(rc.SelectedBySlot == null ? 0 : rc.SelectedBySlot.Length)}");

            if (rc.SelectedBySlot != null && rc.SelectedBySlot.Length > 0)
            {
                var s0 = rc.SelectedBySlot[0];
                Quantum.Log.Info($"[RCUD-Probe] S0={s0.Utility},{s0.Main1},{s0.Main2}, IsSet={s0.IsSet}");
            }
        }
    }
}
