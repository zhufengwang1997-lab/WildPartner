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

        // ---- riding the caught white deer (试玩反馈 1123b-8) ----
        public float rideWalkSpeed = 3.4f, rideRunSpeed = 8.5f;
        /// <summary>The creature she is sitting on (null on foot).</summary>
        public Transform Mount { get; private set; }
        public bool Riding => Mount != null;
        public bool Swimming { get; private set; }
        public bool Climbing { get; private set; }
        float _seat;
        Transform[] _thighs, _shins;
        Transform _hipsBone;

        /// <summary>Sit on `mount` with her hips `seat` metres higher than standing (null = get off).</summary>
        public void SetMount(Transform mount, float seat)
        {
            Mount = mount;
            _seat = mount ? seat : 0f;
            _jumping = false; _windup = -1f;
            if (visual) visual.localPosition = Vector3.up * _seat;
        }

        /// <summary>Move without a camera cut (getting off the deer).</summary>
        public void Place(Vector3 pos)
        {
            _cc.enabled = false;
            transform.position = pos;
            _cc.enabled = true;
        }

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
        float _lastAnimStep = -10f;
        /// <summary>Called by FootstepRelay on a foot-contact animation event.</summary>
        public void AnimFootstep()
        {
            if (Speed < 0.3f || _jumping) return;
            _lastAnimStep = Time.time;
            Footstep?.Invoke();
        }

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

        /// <summary>Orbit distance factor (0.65..1.9, 1 = default); the cabin interior starts close.</summary>
        public void SetCameraDistance(float k) { if (_orbit) _orbit.RadialAxis.Value = k; }

        /// <summary>Point the orbit camera from a given yaw/pitch (cut, no damping).</summary>
        public void SetCameraOrbit(float yaw, float pitch)
        {
            if (_orbit) { _orbit.HorizontalAxis.Value = yaw; _orbit.VerticalAxis.Value = pitch; }
            if (vcam) vcam.PreviousStateIsValid = false;
        }

        /// <summary>Raised on teleports/cuts so the upscaler can reset its history (IN-08).</summary>
        public static event System.Action CameraCut;

        // ---- world bounds (试玩反馈 1123c-A1) ----
        /// <summary>Hard limit: she never gets closer than this to the terrain edge. The edge hills turn into a
        /// cliff band just outside it (WorldBuilder.EdgeRise), so the stop reads as "too steep", not an air wall.</summary>
        public const float BoundaryInset = 17f;
        /// <summary>Raised after pushing against the edge for a while (the HUD shows a hint).</summary>
        public static event System.Action EdgeReached;
        /// <summary>Raised after she fell out of the world / got wedged and was put back on safe ground.</summary>
        public static event System.Action Recovered;
        Vector3 _safePos; float _safeYaw, _safeTimer = -1f, _edgeTime, _edgeHintAt = -99f, _wedged;
        Vector3 _lastPos;
        public int Recoveries { get; private set; }

        public static Vector3 ClampToWorld(Vector3 p)
        {
            float lo = BoundaryInset, hi = World.WorldLayout.Size - BoundaryInset;
            return new Vector3(Mathf.Clamp(p.x, lo, hi), p.y, Mathf.Clamp(p.z, lo, hi));
        }

        static float GroundAt(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t ? t.SampleHeight(p) + t.transform.position.y : float.NegativeInfinity;
        }

        /// <summary>Runs after every move (walking, jumping, riding, swimming, climbing): clamp to the bounds, keep a
        /// last safe spot, and put her back there if she fell under the terrain or got wedged in the air.</summary>
        void GuardWorld(float dt, bool grounded)
        {
            if (World.EnvController.Indoors) { _safeTimer = -1f; return; }
            var p = transform.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) { Recover(); return; }
            var c = ClampToWorld(p);
            if ((c - p).sqrMagnitude > 1e-8f)
            {
                Place(c); p = c;
                _edgeTime += dt;
                if (_edgeTime > 1.0f && Time.time - _edgeHintAt > 12f) { _edgeHintAt = Time.time; EdgeReached?.Invoke(); }
            }
            else _edgeTime = Mathf.Max(0, _edgeTime - dt);

            float ground = GroundAt(p);
            if (p.y < ground - 1.5f || p.y < -30f) { Recover(); return; }
            // wedged: airborne for seconds without falling (stuck on a rock lip / between colliders)
            bool hung = !grounded && !Riding && Mathf.Abs(p.y - _lastPos.y) < 0.002f && !Swimming && !Climbing;
            _wedged = hung ? _wedged + dt : 0f;
            _lastPos = p;
            if (_wedged > 3f) { Recover(); return; }

            if (_safeTimer < 0f) { _safePos = p; _safeYaw = transform.eulerAngles.y; _safeTimer = 0f; }
            _safeTimer += dt;
            bool dry = !World.WorldLayout.WaterSurface(p.x, p.z, out float wl) || wl < ground + 0.3f;
            if (_safeTimer > 0.5f && grounded && dry && !Riding && Mathf.Abs(p.y - ground) < 0.5f && (ClampToWorld(p) - p).sqrMagnitude < 1e-6f
                && p.x > BoundaryInset + 1 && p.z > BoundaryInset + 1 && p.x < World.WorldLayout.Size - BoundaryInset - 1 && p.z < World.WorldLayout.Size - BoundaryInset - 1)
            { _safeTimer = 0f; _safePos = p; _safeYaw = transform.eulerAngles.y; }
        }

        void Recover()
        {
            Recoveries++;
            var p = _safeTimer >= 0f ? _safePos : new Vector3(World.WorldLayout.PlayerStart.x, 0, World.WorldLayout.PlayerStart.y);
            float g = GroundAt(p);
            if (!float.IsInfinity(g)) p.y = Mathf.Max(p.y, g + 0.1f);
            _vy = -2f; _wedged = 0f; _jumping = false;
            Teleport(p, _safeYaw);
            _lastPos = p;
            Debug.Log($"[Bounds] recovered to {p}");
            Recovered?.Invoke();
        }

        /// <summary>Smoke test hook: drop her under the terrain to check the recovery.</summary>
        public void DebugFallThrough() { Place(transform.position + Vector3.down * 6f); }

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
            float target = Riding ? (Sneaking ? 1.4f : Running ? rideRunSpeed : rideWalkSpeed)
                         : Sneaking ? sneakSpeed : Running ? runSpeed : walkSpeed;

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
            _sinceJumpPressed = (!Frozen && !Riding && GameInput.JumpPressed) ? 0f : _sinceJumpPressed + dt;
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
            GuardWorld(dt, _cc.isGrounded);

            if (_animator)
            {
                // blend tree: Idle 0 / Walk 1.64 / Run 3.2 m/s at 1x; faster ground speed plays the run faster
                _animator.SetFloat(SpeedId, Riding ? 0f : Mathf.Min(Speed, animRunSpeed), 0.08f, Time.deltaTime);
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
                _animator.speed = air || capturing || Riding ? 1f : sneakAnim ? Mathf.Clamp(Speed / animSneakSpeed, 1f, 2f) : Mathf.Max(1f, Speed / animRunSpeed);
                if (capturing)
                {
                    // turn to face the creature while drawing it in
                    Vector3 to = Creatures.CaptureSystem.Target.transform.position - transform.position; to.y = 0;
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
                }
            }

            Noise = Riding ? (Speed < 0.2f ? 0.6f : Running ? 1.4f : 0.9f) : _jumping ? 1.2f : Speed < 0.2f ? (Sneaking ? 0.35f : 0.55f) : Sneaking ? 0.45f : Running ? 1.6f : 1f;

            // Footsteps for audio + procedural animation phase
            float prev = _stepPhase;
            _stepPhase += Speed * Time.deltaTime * (Running ? 1.05f : 1.4f);
            // fallback only for a model without foot-contact events (the rigged witch drives steps from her clips)
            if (Mathf.Floor(prev) != Mathf.Floor(_stepPhase) && Speed > 0.5f && Time.time - _lastAnimStep > 1.5f && !_animator) Footstep?.Invoke();
        }

        /// <summary>Riding: no sit clip, so the legs are posed on top of the Idle clip - thighs forward and apart
        /// round the deer's back, shins hanging down. Runs before SecondaryMotion (order 100).</summary>
        void LateUpdate()
        {
            if (!Riding || !visual) return;
            if (_thighs == null)
            {
                _thighs = new Transform[2]; _shins = new Transform[2];
                foreach (var t in visual.GetComponentsInChildren<Transform>())
                    switch (t.name)
                    {
                        case "Thigh.L": _thighs[0] = t; break; case "Thigh.R": _thighs[1] = t; break;
                        case "Shin.L": _shins[0] = t; break; case "Shin.R": _shins[1] = t; break;
                        case "Hips": _hipsBone = t; break;
                    }
            }
            Vector3 right = transform.right, fwd = transform.forward;
            for (int i = 0; i < 2; i++)
            {
                if (!_thighs[i] || !_shins[i]) continue;
                float outSign = _hipsBone && Vector3.Dot(_thighs[i].position - _hipsBone.position, right) < 0f ? -1f : 1f;
                _thighs[i].rotation = Quaternion.AngleAxis(-70f, right) * Quaternion.AngleAxis(outSign * 24f, fwd) * _thighs[i].rotation;
                _shins[i].rotation = Quaternion.AngleAxis(78f, right) * _shins[i].rotation;
            }
        }
    }
}
