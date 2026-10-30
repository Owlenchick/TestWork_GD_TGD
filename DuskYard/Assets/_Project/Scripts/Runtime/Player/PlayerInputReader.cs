using UnityEngine;
using UnityEngine.InputSystem;

namespace DuskYard.Player
{
    /// <summary>
    /// Reads keyboard/mouse/gamepad directly from the Input System devices and exposes a small, frame-stable state.
    /// When <see cref="Scripted"/> is true the devices are ignored and an external driver (benchmark autopilot)
    /// feeds the values through <see cref="SetScripted"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] float mouseSensitivity = 0.11f;     // degrees per mouse count
        [SerializeField] float gamepadLookSpeed = 170f;      // degrees per second at full stick

        public Vector2 Move { get; private set; }
        /// <summary>Camera rotation delta for this frame, degrees (x = yaw, y = pitch).</summary>
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool LockOnPressed { get; private set; }
        public bool Scripted { get; set; }

        void Update()
        {
            if (Scripted) return;

            Vector2 move = Vector2.zero, look = Vector2.zero;
            bool sprint = false, attack = false, lockOn = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                sprint |= kb.leftShiftKey.isPressed;
                attack |= kb.spaceKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame;
                lockOn |= kb.qKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    look += mouse.delta.ReadValue() * mouseSensitivity;
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (Cursor.lockState != CursorLockMode.Locked && Application.isFocused) SetCursorLocked(true);
                    else attack = true;
                }
                lockOn |= mouse.middleButton.wasPressedThisFrame;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                move += pad.leftStick.ReadValue();
                look += pad.rightStick.ReadValue() * (gamepadLookSpeed * Time.unscaledDeltaTime);
                sprint |= pad.leftStickButton.isPressed || pad.leftTrigger.isPressed;
                attack |= pad.buttonWest.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame;
                lockOn |= pad.rightStickButton.wasPressedThisFrame;
            }

            Move = Vector2.ClampMagnitude(move, 1f);
            Look = look;
            Sprint = sprint;
            AttackPressed = attack;
            LockOnPressed = lockOn;
        }

        public void SetScripted(Vector2 move, Vector2 look, bool sprint, bool attack, bool lockOn = false)
        {
            Move = Vector2.ClampMagnitude(move, 1f);
            Look = look;
            Sprint = sprint;
            AttackPressed = attack;
            LockOnPressed = lockOn;
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
