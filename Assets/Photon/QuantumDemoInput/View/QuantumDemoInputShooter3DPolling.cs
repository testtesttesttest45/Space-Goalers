namespace Quantum {
  using Photon.Deterministic;
  using UnityEngine;

  /// <summary>
  /// A Unity script that creates empty input for any Quantum game.
  /// </summary>
  public class QuantumDemoInputShooter3DPolling : MonoBehaviour {

    private void OnEnable() {
      QuantumCallback.Subscribe(this, (CallbackPollInput callback) => PollInput(callback));
    }

    /// <summary>
    /// Set an empty input when polled by the simulation.
    /// </summary>
    /// <param name="callback"></param>
    public void PollInput(CallbackPollInput callback) {
      QuantumDemoInputShooter3D sInput = default;
      var x = UnityEngine.Input.GetAxis("Horizontal");
      var y = UnityEngine.Input.GetAxis("Vertical");

      // no worries with clamping, normalization (all implicit)
      sInput.MoveDirection = new FPVector2(x.ToFP(), y.ToFP());

      sInput.Jump = UnityEngine.Input.GetButton("Jump");
      sInput.Dash = UnityEngine.Input.GetButton("Fire2");
      sInput.Fire = UnityEngine.Input.GetButton("Fire1");
      sInput.Use = UnityEngine.Input.GetButton("Fire3");

      // grab this using local mouse, etc
      sInput.Yaw = default;
      sInput.Pitch = default;

      // implicitly casts to base input
      callback.SetInput(sInput, DeterministicInputFlags.Repeatable);
    }
  }
}