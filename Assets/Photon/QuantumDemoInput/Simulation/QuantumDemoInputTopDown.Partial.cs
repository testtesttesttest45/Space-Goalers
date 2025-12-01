namespace Quantum
{
    using Photon.Deterministic;
    unsafe partial struct QuantumDemoInputTopDown
    {

        public static implicit operator Input(QuantumDemoInputTopDown tInput)
        {
            Input input = default;
            input._left = tInput.Left;
            input._right = tInput.Right;
            input._up = tInput.Up;
            input._down = tInput.Down;
            input._a = tInput.Jump;
            input._b = tInput.Dash;
            input._c = tInput.Fire;
            input._d = tInput.AltFire;
            input._r1 = tInput.Use;
            input._l1 = tInput.Hook;   // 👈 NEW: map Hook
            input._l2 = tInput.Speed;
            input._select = tInput.Select;
            input._r2 = tInput.Bomb;

            byte encodedAngle = default;
            var direction = tInput.AimDirection;
            byte encodedMagnitude = default;
            if (direction != default)
            {
                direction = FPVector2.Normalize(direction, out var magnitude);
                encodedMagnitude = (byte)(magnitude * 255).AsInt;
                var angle = FPVector2.RadiansSigned(FPVector2.Up, direction) * FP.Rad2Deg;
                angle = (((angle + 360) % 360) / 2) + 1;
                encodedAngle = (byte)(angle.AsInt);
            }
            input.ThumbSticks.Regular->_rightThumbAngle = encodedAngle;
            input.ThumbSticks.Regular->_rightThumbMagnitude = encodedMagnitude;

            encodedAngle = default;
            if (tInput.MoveDirection != default)
            {
                var angle = FPVector2.RadiansSigned(FPVector2.Up, tInput.MoveDirection.Normalized) * FP.Rad2Deg;
                angle = (((angle + 360) % 360) / 2) + 1;
                encodedAngle = (byte)(angle.AsInt);
            }
            input.ThumbSticks.Regular->_leftThumbAngle = encodedAngle;

            return input;
        }

        public static implicit operator QuantumDemoInputTopDown(Input input)
        {
            QuantumDemoInputTopDown tInput = default;
            tInput.Left = input._left;
            tInput.Right = input._right;
            tInput.Up = input._up;
            tInput.Down = input._down;
            tInput.Jump = input._a;
            tInput.Dash = input._b;
            tInput.Fire = input._c;
            tInput.AltFire = input._d;
            tInput.Use = input._r1;
            tInput.Hook = input._l1;   // 👈 NEW: decode Hook
            tInput.Speed = input._l2;
            tInput.Select = input._select;
            tInput.Bomb = input._r2;

            var encodedAngle = input.ThumbSticks.Regular->_rightThumbAngle;
            var encodedMagnitude = input.ThumbSticks.Regular->_rightThumbMagnitude;
            if (encodedAngle != default)
            {
                int angle = ((int)encodedAngle - 1) * 2;
                var magnitude = ((FP)encodedMagnitude) / 255;
                tInput.AimDirection = FPVector2.Rotate(FPVector2.Up, angle * FP.Deg2Rad) * magnitude;
            }

            var encoded = input.ThumbSticks.Regular->_leftThumbAngle;
            if (encoded != default)
            {
                int angle = ((int)encoded - 1) * 2;
                tInput.MoveDirection = FPVector2.Rotate(FPVector2.Up, angle * FP.Deg2Rad);
            }

            return tInput;
        }

        public FPVector2 DigitalDirection
        {
            get
            {
                FPVector2 value = default;
                if (Left) value.X -= FP._1;
                if (Right) value.X += FP._1;
                if (Down) value.Y -= FP._1;
                if (Up) value.Y += FP._1;
                if (value.SqrMagnitude > FP._1) value = value.Normalized;
                return value;
            }
        }
    }
}
