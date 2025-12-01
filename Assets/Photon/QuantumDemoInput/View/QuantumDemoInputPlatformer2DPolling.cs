namespace Quantum {
  using Photon.Deterministic;
  using UnityEngine;

  /// <summary>
  /// A Unity script that creates empty input for any Quantum game.
  /// </summary>
  public class QuantumDemoInputPlatformer2DPolling : MonoBehaviour {

    private void OnEnable() {
      QuantumCallback.Subscribe(this, (CallbackPollInput callback) => PollInput(callback));
    }

    /// <summary>
    /// Set an empty input when polled by the simulation.
    /// </summary>
    /// <param name="callback"></param>
    public void PollInput(CallbackPollInput callback) {
      QuantumDemoInputPlatformer2D pInput = default;
      var x = UnityEngine.Input.GetAxis("Horizontal");
      pInput.Left = x < 0;
      pInput.Right = x > 0;
      var y = UnityEngine.Input.GetAxis("Vertical");
      pInput.Down = y < 0;
      pInput.Up = y > 0;

      pInput.Jump = UnityEngine.Input.GetButton("Jump");
      pInput.Dash = UnityEngine.Input.GetButton("Fire2");
      pInput.Fire = UnityEngine.Input.GetButton("Fire1");
      pInput.Use = UnityEngine.Input.GetButton("Fire3");

      // grab this using mouse, etc
      pInput.AimDirection = default;

      // implicitly casts to base input
      callback.SetInput(pInput, DeterministicInputFlags.Repeatable);
    }
  }
}