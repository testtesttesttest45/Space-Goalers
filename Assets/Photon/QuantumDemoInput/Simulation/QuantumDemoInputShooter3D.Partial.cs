namespace Quantum {
  using Photon.Deterministic;
  unsafe partial struct QuantumDemoInputShooter3D {
    // limited to -180 +180
    private const int YAW_MULT = 45;
    // limited to -90 +90
    private const int PITCH_MULT = 90;

    public static implicit operator Input(QuantumDemoInputShooter3D sInput) {
      Input input = default;
      input._a = sInput.Jump;
      input._b = sInput.Dash;
      input._c = sInput.Fire;
      input._d = sInput.AltFire;
      input._r1 = sInput.Use;

      // move direction conversion/compression
      // uses magnitude for "analog" style movement
      byte encodedAngle = default;
      var direction = sInput.MoveDirection;
      byte encodedMagnitude = default;
      if (direction != default) {
        direction = FPVector2.Normalize(direction, out var magnitude);
        encodedMagnitude = (byte)(magnitude * 255).AsInt;
        var angle = FPVector2.RadiansSigned(FPVector2.Up, direction) * FP.Rad2Deg;
        angle = (((angle + 360) % 360) / 2) + 1;
        encodedAngle = (byte)(angle.AsInt);
      }
      input.ThumbSticks.HighRes->_leftThumbAngle = encodedAngle;
      input.ThumbSticks.HighRes->_leftThumbMagnitude = encodedMagnitude;

      // higher res right thumbstick (pitch yaw deltas)
      var clampedYaw = sInput.Yaw % 180;
      input.ThumbSticks.HighRes->_rightThumbX = (short)(clampedYaw * YAW_MULT).AsInt;
      var clampedPitch = FPMath.Clamp(sInput.Pitch, -90, 90);
      input.ThumbSticks.HighRes->_rightThumbY = (short)(clampedPitch * PITCH_MULT).AsInt;

      return input;
    }

    public static implicit operator QuantumDemoInputShooter3D(Input input) {
      QuantumDemoInputShooter3D sInput = default;
      sInput.Jump = input._a;
      sInput.Dash = input._b;
      sInput.Fire = input._c;
      sInput.AltFire = input._d;
      sInput.Use = input._r1;

      var encodedAngle = input.ThumbSticks.Regular->_leftThumbAngle;
      var encodedMagnitude = input.ThumbSticks.Regular->_leftThumbMagnitude;
      if (encodedAngle != default) {
        int angle = ((int)encodedAngle - 1) * 2;
        var magnitude = ((FP)encodedMagnitude) / 255;
        sInput.MoveDirection = FPVector2.Rotate(FPVector2.Up, angle * FP.Deg2Rad) * magnitude;
      }

      sInput.Yaw = ((FP)input.ThumbSticks.HighRes->_rightThumbX) / YAW_MULT;
      sInput.Pitch = ((FP)input.ThumbSticks.HighRes->_rightThumbY) / PITCH_MULT;
      return sInput;
    }

    // use this as projectile orientation upon firing
    public FPQuaternion LookRotation => FPQuaternion.Euler(Yaw, FP._0, Pitch);
  }
}