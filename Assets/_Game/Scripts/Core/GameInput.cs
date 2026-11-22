using UnityEngine;
using UnityEngine.InputSystem;

namespace WildPartner.Core
{
    /// <summary>Keyboard/mouse + gamepad in one place (Input System, polled). Values are zero while blocked.</summary>
    public static class GameInput
    {
        public static bool Blocked;
        /// <summary>Automation hooks (smoke test): behave as if the capture / sneak keys were held.</summary>
        public static bool SimCapture, SimSneak, SimJump, SimInteract, SimRun;
        public static Vector2 SimMove;

        static Keyboard K => Keyboard.current;
        static Mouse M => Mouse.current;
        static Gamepad G => Gamepad.current;

        public static Vector2 Move
        {
            get
            {
                if (Blocked) return Vector2.zero;
                Vector2 v = SimMove;
                if (K != null)
                {
                    if (K.wKey.isPressed || K.upArrowKey.isPressed) v.y += 1;
                    if (K.sKey.isPressed || K.downArrowKey.isPressed) v.y -= 1;
                    if (K.dKey.isPressed || K.rightArrowKey.isPressed) v.x += 1;
                    if (K.aKey.isPressed || K.leftArrowKey.isPressed) v.x -= 1;
                }
                if (G != null) v += G.leftStick.ReadValue();
                return Vector2.ClampMagnitude(v, 1);
            }
        }

        /// <summary>Camera look delta in degrees this frame.</summary>
        public static Vector2 Look
        {
            get
            {
                if (Blocked) return Vector2.zero;
                Vector2 v = Vector2.zero;
                if (M != null && Cursor.lockState == CursorLockMode.Locked) v += M.delta.ReadValue() * 0.12f;
                if (G != null) v += G.rightStick.ReadValue() * 160f * Time.unscaledDeltaTime;
                return v;
            }
        }

        public static float Zoom => Blocked || M == null ? 0 : M.scroll.ReadValue().y;

        public static bool Run => !Blocked && (SimRun || (K != null && K.leftShiftKey.isPressed) || (G != null && G.leftStickButton.isPressed));
        public static bool Sneak => !Blocked && (SimSneak || (K != null && (K.leftCtrlKey.isPressed || K.cKey.isPressed)) || (G != null && G.rightShoulder.isPressed));
        public static bool CaptureHeld => !Blocked && (SimCapture || (K != null && K.eKey.isPressed) || (G != null && G.buttonWest.isPressed));
        public static bool JumpPressed => !Blocked && (SimJump || (K != null && K.spaceKey.wasPressedThisFrame) || (G != null && G.buttonSouth.wasPressedThisFrame));
        /// <summary>E tapped (doors, bed, book in the cabin). Holding E near an animal is the capture instead.</summary>
        public static bool InteractPressed => !Blocked && (SimInteract || (K != null && K.eKey.wasPressedThisFrame) || (G != null && G.buttonWest.wasPressedThisFrame));
        public static bool JournalPressed => (K != null && K.tabKey.wasPressedThisFrame) || (G != null && G.selectButton.wasPressedThisFrame);
        public static bool MapPressed => (K != null && K.mKey.wasPressedThisFrame) || (G != null && G.dpad.up.wasPressedThisFrame);
        public static bool PausePressed => (K != null && K.escapeKey.wasPressedThisFrame) || (G != null && G.startButton.wasPressedThisFrame);
        public static bool DebugPressed => K != null && K.f3Key.wasPressedThisFrame;
        public static bool AnyKeyDown => (K != null && K.anyKey.wasPressedThisFrame) || (G != null && G.buttonSouth.wasPressedThisFrame);
        public static bool UsingGamepad => G != null && (G.leftStick.ReadValue().sqrMagnitude > 0.01f || G.rightStick.ReadValue().sqrMagnitude > 0.01f);
    }
}
