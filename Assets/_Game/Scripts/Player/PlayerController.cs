using UnityEngine;
using Unity.Cinemachine;
using WildPartner.Core;

namespace WildPartner.Player
{
    /// <summary>
    /// Third-person movement (WASD / left stick; Shift run, Ctrl sneak, Space jump) relative to the camera, plus the
    /// Cinemachine orbit driven from <see cref="GameInput"/>. Exposes <see cref="Noise"/> — how far away
    /// creatures can notice the player — which sneaking reduces.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        public float walkSpeed = 1.6f, runSpeed = 4.0f, sneakSpeed = 1.0f;
        public float turnSpeed = 720f;
        public float gravity = -20f;
        [Tooltip("Jump apex height (m). Short coyote time / input buffer make the jump forgiving on slopes.")]
        public float jumpHeight = 0.9f;
        [Tooltip("Anticipation crouch before the feet leave the ground (matches the JumpUp clip)")]
        public float jumpWindup = 0f;
        const float CoyoteTime = 0.12f, JumpBuffer = 0.12f;
        public CinemachineCamera vcam;
        public Transform visual;
        Animator _animator;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int SneakId = Animator.StringToHash("Sneak"), CaptureId = Animator.StringToHash("Capture");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int AirTimeId = Animator.StringToHash("AirTime");
        float _sinceGrounded, _sinceJumpPressed = 99f, _airTime;
        bool _jumping;
        float _windup = -1f;   // >= 0 while crouching before take-off
        /// <summary>True while airborne (jumping or falling).</summary>
        public bool Airborne => _airTime > 0.15f || _jumping;
        public event System.Action Jumped, Landed;
        [Tooltip("Ground speed of the Run clip at 1x (measured in Blender, Rigged/<Name>_speeds.json); faster movement speeds the animator up")]
        public float animRunSpeed = 3.2f;
        /// <summary>Ground speed of the rigged Sneak clip at 1x (0 = no sneak clips).</summary>
        public float animSneakSpeed;

        public float Speed { get; private set; }
        public bool Sneaking { get; private set; }
        public bool Running { get; private set; }
        /// <summary>Detection radius multiplier: 1 walking, ~1.6 running, ~0.45 sneaking, 0.6 standing still.</summary>
        public float Noise { get; private set; } = 1f;
        public bool Frozen;

        CharacterController _cc;
        CinemachineOrbitalFollow _orbit;
        float _vy;
        float _stepPhase;
        public float StepPhase => _stepPhase;
        public event System.Action Footstep;

        void Awake()
        {
            Instance = this;
            _cc = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();
            if (vcam) _orbit = vcam.GetComponent<CinemachineOrbitalFollow>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Teleport(Vector3 pos, float yaw)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            _cc.enabled = true;
            if (_orbit) _orbit.HorizontalAxis.Value = yaw;
            if (vcam) vcam.PreviousStateIsValid = false;   // camera cut: no damping across the jump
            CameraCut?.Invoke();
        }

        /// <summary>Raised on teleports/cuts so the upscaler can reset its history (IN-08).</summary>
        public static event System.Action CameraCut;

        void Update()
        {
            // Camera orbit
            if (_orbit)
            {
                var look = GameInput.Look * SaveSystem.Settings.mouseSensitivity;
                _orbit.HorizontalAxis.Value += look.x;
                float inv = SaveSystem.Settings.invertY ? 1 : -1;
                _orbit.VerticalAxis.Value = Mathf.Clamp(_orbit.VerticalAxis.Value + look.y * inv, -12f, 65f);
                float z = GameInput.Zoom;
                if (Mathf.Abs(z) > 0.01f) _orbit.RadialAxis.Value = Mathf.Clamp(_orbit.RadialAxis.Value - z * 0.05f, 0.65f, 1.9f);
            }

            Vector2 input = Frozen ? Vector2.zero : GameInput.Move;
            Sneaking = GameInput.Sneak;   // holding still while capturing still counts as sneaking
            Running = !Frozen && GameInput.Run && !Sneaking && input.sqrMagnitude > 0.01f;
            float target = Sneaking ? sneakSpeed : Running ? runSpeed : walkSpeed;

            Vector3 fwd = Vector3.forward, right = Vector3.right;
            var cam = Camera.main;
            if (cam)
            {
                fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                right = Vector3.Cross(Vector3.up, fwd);
            }
            Vector3 move = (fwd * input.y + right * input.x);
            float mag = Mathf.Clamp01(move.magnitude);
            Vector3 vel = move.normalized * target * mag;
            Speed = Mathf.MoveTowards(Speed, vel.magnitude, 30f * Time.deltaTime);

            if (mag > 0.05f)
            {
                var rot = Quaternion.LookRotation(move.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, turnSpeed * Time.deltaTime);
            }

            // jump: buffered press + coyote time; vertical speed for the requested apex height
            float dt = Time.deltaTime;
            bool grounded = _cc.isGrounded;
            _sinceGrounded = grounded ? 0f : _sinceGrounded + dt;
            _sinceJumpPressed = (!Frozen && GameInput.JumpPressed) ? 0f : _sinceJumpPressed + dt;
            if (_sinceJumpPressed <= JumpBuffer && _sinceGrounded <= CoyoteTime && !_jumping)
            {
                _jumping = true; _sinceJumpPressed = 99f;
                _windup = grounded ? jumpWindup : 0f;      // coyote jumps (already off the edge) launch at once
                if (_animator) _animator.SetTrigger(JumpId);
            }
            if (_windup >= 0f)
            {
                _windup -= dt;
                if (_windup < 0f)
                {
                    _vy = Mathf.Sqrt(2f * -gravity * jumpHeight);
                    _sinceGrounded = 99f;
                    Jumped?.Invoke();
                }
                else _vy = -2f;
            }
            else if (grounded && _vy <= 0f)
            {
                if (_jumping || _airTime > 0.3f) { Landed?.Invoke(); Footstep?.Invoke(); }
                _jumping = false; _vy = -2f;
            }
            else _vy += gravity * dt;
            _airTime = grounded && _vy <= 0f ? 0f : _airTime + dt;
            _cc.Move((transform.forward * Speed + Vector3.up * _vy) * dt);

            if (_animator)
            {
                // blend tree: Idle 0 / Walk 1.64 / Run 3.2 m/s at 1x; faster ground speed plays the run faster
                _animator.SetFloat(SpeedId, Mathf.Min(Speed, animRunSpeed), 0.08f, Time.deltaTime);
                bool air = _jumping || _airTime > 0.25f;   // short drops (stairs, bumps) stay on Locomotion
                _animator.SetBool(GroundedId, !air);
                _animator.SetFloat(AirTimeId, _airTime);
                bool capturing = Creatures.CaptureSystem.Target != null;
                bool sneakAnim = animSneakSpeed > 0.01f && Sneaking && !air && !capturing;
                if (animSneakSpeed > 0.01f)
                {
                    _animator.SetBool(SneakId, sneakAnim);
                    _animator.SetBool(CaptureId, capturing && !air);
                }
                _animator.speed = air || capturing ? 1f : sneakAnim ? Mathf.Clamp(Speed / animSneakSpeed, 1f, 2f) : Mathf.Max(1f, Speed / animRunSpeed);
                if (capturing)
                {
                    // turn to face the creature while drawing it in
                    Vector3 to = Creatures.CaptureSystem.Target.transform.position - transform.position; to.y = 0;
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
                }
            }

            Noise = _jumping ? 1.2f : Speed < 0.2f ? (Sneaking ? 0.35f : 0.55f) : Sneaking ? 0.45f : Running ? 1.6f : 1f;

            // Footsteps for audio + procedural animation phase
            float prev = _stepPhase;
            _stepPhase += Speed * Time.deltaTime * (Running ? 1.05f : 1.4f);
            if (Mathf.Floor(prev) != Mathf.Floor(_stepPhase) && Speed > 0.5f) Footstep?.Invoke();
        }
    }
}
