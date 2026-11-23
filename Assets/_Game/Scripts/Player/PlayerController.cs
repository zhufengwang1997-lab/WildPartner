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
        static readonly int SwimId = Animator.StringToHash("Swim"), ClimbId = Animator.StringToHash("Climb"), ClimbOverId = Animator.StringToHash("ClimbOver");
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
            // 1123c-B4 leftover: keep the camera out of tree crowns while climbing / high up
            if (vcam && !vcam.GetComponent<CanopyCameraAvoid>()) vcam.gameObject.AddComponent<CanopyCameraAvoid>().player = this;
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

        public float CameraRadial => _orbit ? _orbit.RadialAxis.Value : 1f;
        /// <summary>Ease the orbit camera towards a yaw / pitch / distance (fishing: behind her right shoulder).</summary>
        public void BlendCameraOrbit(float yaw, float pitch, float radial, float k)
        {
            if (!_orbit) return;
            _orbit.HorizontalAxis.Value = Mathf.LerpAngle(_orbit.HorizontalAxis.Value, yaw, k);
            _orbit.VerticalAxis.Value = Mathf.Lerp(_orbit.VerticalAxis.Value, pitch, k);
            _orbit.RadialAxis.Value = Mathf.Lerp(_orbit.RadialAxis.Value, radial, k);
        }

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
            bool dry = (!World.WorldLayout.WaterSurface(p.x, p.z, out float wl) || wl < ground + 0.3f) && !WaterWalking;
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

        // ---- 1123c-B4: climbing ----
        public float climbSpeed = 0.9f, climbFastSpeed = 1.4f;
        public const float StaminaMax = 100f;
        /// <summary>Climbing stamina (0..100): drains on the wall, refills on the ground; empty -> she lets go.</summary>
        public float Stamina { get; private set; } = StaminaMax;
        public event System.Action ClimbStarted, ClimbedOver, ClimbFell;
        /// <summary>The top of the watchtower was reached (first time per session raises TowerTopReached).</summary>
        public static event System.Action TowerTopReached;
        Vector3 _wallNormal;
        float _lookIdle = 10f;
        float _gapExtra, _blockedUp, _grabAt, _pushWall, _climbCooldown, _mantleT = -1f, _mantleH;
        Vector3 _mantleFrom, _mantleTo;
        bool _towerTopDone;
        public bool Mantling => _mantleT >= 0f;
        const float ChestH = 1.25f, WallGap = 0.42f;

        /// <summary>Rock walls, cliffs, the watchtower and steep terrain can be climbed (not trees, houses, props).</summary>
        static bool Climbable(RaycastHit h)
        {
            if (Mathf.Abs(h.normal.y) > 0.55f) return false;                  // not a wall
            if (h.collider is TerrainCollider) return true;
            for (var t = h.collider.transform; t; t = t.parent)
            {
                string n = t.name;
                if (n.StartsWith("Rock") || n.StartsWith("Cliff") || n.StartsWith("Watchtower")) return true;
            }
            return false;
        }

        // rays start inside her own capsule, which they therefore never hit
        static int ClimbMask => Physics.DefaultRaycastLayers;

        bool WallAhead(Vector3 dir, float up, out RaycastHit hit, float reach = 0.9f)
        {
            var o = transform.position + Vector3.up * up;
            return Physics.Raycast(o, dir, out hit, reach, ClimbMask, QueryTriggerInteraction.Ignore) && Climbable(hit);
        }

        /// <summary>Grab a wall: pushing into one (0.25 s) or Space while facing one. Not while riding / swimming / on the koi's water.</summary>
        void TryStartClimb(Vector2 input, float dt)
        {
            _climbCooldown -= dt;
            if (Climbing || Riding || Swimming || World.EnvController.Indoors || _climbCooldown > 0f || Frozen || GameInput.Sneak) { _pushWall = 0f; return; }
            if (Stamina < 15f) return;
            var fwd = transform.forward;
            { var ahead = transform.position + fwd * 1.2f; if ((ClampToWorld(ahead) - ahead).sqrMagnitude > 1e-4f) { _pushWall = 0f; return; } }   // the edge cliffs: no climbing out
            bool wall = WallAhead(fwd, ChestH, out var hit, 0.75f) && WallAhead(fwd, 0.5f, out _, 0.95f);
            ClimbDebug(fwd, input);
            if (!wall) { _pushWall = 0f; return; }
            // only walls taller than she can just step / jump up: the wall must go on above her head
            bool tall = WallAhead(-hit.normal, 2.2f, out _, 1.4f) || WallAhead(fwd, 2.0f, out _, 1.2f);
            if (!tall) { _pushWall = 0f; return; }
            // walking into it: the camera-relative move points at the wall
            Vector3 cf = Vector3.forward, cr = Vector3.right;
            var cam = Camera.main;
            if (cam) { cf = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized; cr = Vector3.Cross(Vector3.up, cf); }
            Vector3 mv = cf * input.y + cr * input.x;
            bool pushing = mv.magnitude > 0.5f && Vector3.Dot(mv.normalized, -hit.normal) > 0.6f && Vector3.Dot(fwd, -hit.normal) > 0.5f;
            _pushWall = pushing ? _pushWall + dt : 0f;
            if (_pushWall > 0.25f || GameInput.JumpPressed) StartClimb(hit);
        }

        float _climbDbgAt;
        void ClimbDebug(Vector3 fwd, Vector2 input)
        {
            if (input.sqrMagnitude < 0.25f || Time.time - _climbDbgAt < 1f) return;
            var o = transform.position + Vector3.up * ChestH;
            if (!Physics.Raycast(o, fwd, out var h, 1.2f, ClimbMask, QueryTriggerInteraction.Ignore)) return;
            _climbDbgAt = Time.time;
            Debug.Log($"[Climb] probe: {h.collider.name} d {h.distance:0.00} n {h.normal} climbable {Climbable(h)} fwd {fwd}");
        }

        void StartClimb(RaycastHit hit)
        {
            if (_waterFloor) _waterFloor.gameObject.SetActive(false);
            Climbing = true; _gapExtra = 0f; _grabAt = Time.time; _pushWall = 0f; _jumping = false; _windup = -1f; _vy = 0f; _airTime = 0f;
            _wallNormal = hit.normal; _wallNormal.y = 0; _wallNormal.Normalize();
            transform.rotation = Quaternion.LookRotation(-_wallNormal);
            if (_animator) _animator.SetBool(ClimbId, true);
            Debug.Log($"[Climb] grabbed {hit.collider.name} at {transform.position}");
            ClimbStarted?.Invoke();
        }

        void StopClimb(bool fell)
        {
            Climbing = false; _climbCooldown = 0.6f; _vy = fell ? 0f : -2f;
            if (_animator) _animator.SetBool(ClimbId, false);
            if (fell) { ClimbFell?.Invoke(); Debug.Log($"[Climb] let go at {transform.position} (stamina {Stamina:0})"); }
        }

        /// <summary>One climbing frame: W/S up/down, A/D sideways along the wall, Ctrl lets go, the top -> mantle.</summary>
        void ClimbUpdate(Vector2 input, float dt)
        {
            if (GameInput.Sneak && !Frozen) { StopClimb(true); return; }   // Ctrl: let go
            bool fast = GameInput.Run;
            float sp = fast ? climbFastSpeed : climbSpeed;
            Stamina -= (input.sqrMagnitude > 0.01f ? (fast ? 9f : 5f) : 2.5f) * dt;
            if (Stamina <= 0f) { Stamina = 0f; StopClimb(true); return; }

            var fwd = -_wallNormal;
            // keep to the wall: re-sample the surface at chest height (follows curved rock)
            if (WallAhead(fwd, ChestH, out var hit, 1.2f) || WallAhead(fwd, 0.6f, out hit, 1.2f))
            {
                var n = hit.normal; n.y = 0;
                if (n.sqrMagnitude > 0.01f) _wallNormal = Vector3.Slerp(_wallNormal, n.normalized, 10f * dt).normalized;
                fwd = -_wallNormal;
                transform.rotation = Quaternion.LookRotation(fwd);
            }
            var right = Vector3.Cross(Vector3.up, fwd);
            // top of the wall: a walkable floor in reach above and ahead -> climb over
            if (input.y > 0.1f && FindLedge(fwd, out var top)) { StartMantle(top); return; }
            // no wall at chest and none at the knees: nothing to hold on to
            bool chest = WallAhead(fwd, ChestH, out _, 1.0f), low = WallAhead(fwd, 0.5f, out _, 1.0f);
            if (!chest && !low) { StopClimb(true); return; }
            float vUp = input.y * sp, vSide = input.x * sp * 0.8f;
            // sideways only while there is wall there
            if (Mathf.Abs(vSide) > 0.01f && !WallAhead(fwd, ChestH, out _, 1.2f, Mathf.Sign(vSide) * 0.45f)) vSide = 0f;
            // down onto the ground: let go
            if (vUp < 0f && _cc.isGrounded && Time.time - _grabAt > 0.4f) { StopClimb(false); return; }
            // hold the gap to the wall
            float dist = chest ? hit.distance : WallGap + 0.1f;
            // a sill / ledge sticking out above her head: lean out from the wall to get round it
            _gapExtra = _blockedUp > 0.05f ? Mathf.Min(0.7f, _gapExtra + 1.2f * dt) : Mathf.Max(0f, _gapExtra - 0.4f * dt);
            Vector3 toWall = fwd * Mathf.Clamp((dist - WallGap - _gapExtra) * 6f, -1f, 1f);
            Speed = new Vector2(vUp, vSide).magnitude;
            var flags = _cc.Move((Vector3.up * vUp + right * vSide + toWall) * dt);
            // head against an overhang: look for the floor above it (out over her back too)
            _blockedUp = vUp > 0.05f && (flags & CollisionFlags.Above) != 0 ? _blockedUp + dt : 0f;
            if (_blockedUp > 0.9f)
            {
                _blockedUp = 0f;
                if (FindLedge(fwd, out var top2, true)) { StartMantle(top2); return; }
                string what = Physics.Raycast(transform.position + Vector3.up * 1.2f, Vector3.up, out var bh, 1.2f, ClimbMask, QueryTriggerInteraction.Ignore) ? $"{bh.collider.name} {bh.distance:0.00} m up" : "?";
                Debug.Log($"[Climb] blocked above at {transform.position} by {what}, leaning out {_gapExtra:0.00}");
            }
            else if (_blockedUp > 0.25f && FindLedge(fwd, out var top3, true)) { _blockedUp = 0f; StartMantle(top3); return; }
            if (_animator) _animator.speed = Mathf.Clamp(Speed / climbSpeed, 0f, 1.6f);
        }

        bool WallAhead(Vector3 dir, float up, out RaycastHit hit, float reach, float sideways)
        {
            var o = transform.position + Vector3.up * up + Vector3.Cross(Vector3.up, dir) * sideways;
            return Physics.Raycast(o, dir, out hit, reach, ClimbMask, QueryTriggerInteraction.Ignore) && Climbable(hit);
        }

        /// <summary>A flat floor above her head that her capsule fits on: just past the wall face, or - when her head
        /// is against an overhang (the watchtower balcony) - out over her back. Rays go through roofs and railings
        /// and take the first walkable surface below them.</summary>
        bool FindLedge(Vector3 fwd, out Vector3 top, bool overhang = false)
        {
            top = default;
            var p = transform.position;
            var aheads = overhang ? new[] { 0.7f, 1.0f, 1.35f, -0.2f, -0.6f, -1.0f } : new[] { 0.7f, 1.0f, 1.35f };
            foreach (float ahead in aheads)
            {
                var o = p + fwd * ahead + Vector3.up * 3.4f;
                var hits = Physics.RaycastAll(o, Vector3.down, 3.2f, ClimbMask, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                foreach (var h in hits)
                {
                    if (h.normal.y < 0.7f) continue;
                    float rise = h.point.y - p.y;
                    if (rise < 0.6f || rise > 2.6f) continue;
                    var a = h.point + Vector3.up * (_cc.radius + 0.06f);
                    var b = h.point + Vector3.up * (_cc.height - _cc.radius);
                    if (Physics.CheckCapsule(a, b, _cc.radius * 0.9f, ClimbMask, QueryTriggerInteraction.Ignore)) continue;
                    top = h.point + Vector3.up * 0.02f;
                    return true;
                }
            }
            return false;
        }

        void StartMantle(Vector3 top)
        {
            _mantleFrom = transform.position; _mantleTo = top; _mantleT = 0f; _mantleH = top.y - _mantleFrom.y;
            Climbing = false;
            if (_animator) { _animator.SetBool(ClimbId, false); _animator.SetTrigger(ClimbOverId); _animator.speed = 1f; }
            // the root goes onto the ledge at once; the ClimbOver clip starts with the body 1 m lower and 0.45 m back,
            // the visual offset covers the rest of the real height and eases out over the clip
            Place(top);
            Debug.Log($"[Climb] climbing over onto {top} (rise {_mantleH:0.00} m)");
        }

        void MantleUpdate(float dt)
        {
            const float Dur = 0.9f;
            _mantleT += dt;
            float t = Mathf.Clamp01(_mantleT / Dur);
            if (visual)
            {
                float extra = (_mantleH - 1.0f) * (1f - Mathf.SmoothStep(0, 1, t));
                visual.localPosition = Vector3.up * (_seat - extra);
            }
            Speed = 0f; _vy = -2f;
            if (t >= 1f)
            {
                _mantleT = -1f;
                if (visual) visual.localPosition = Vector3.up * _seat;
                ClimbedOver?.Invoke();
                CheckTowerTop();
            }
        }

        void CheckTowerTop()
        {
            var p = transform.position;
            var tw = World.WorldLayout.Watchtower;
            if (_towerTopDone || Vector2.Distance(new Vector2(p.x, p.z), tw) > 5f || p.y < GroundAt(p) + 4f) return;
            _towerTopDone = true;
            Debug.Log($"[Climb] top of the watchtower at {p}");
            TowerTopReached?.Invoke();
        }

        // ---- 1123c-B3: swimming ----
        public float swimSpeed = 1.3f, swimFastSpeed = 2.0f, swimSlowSpeed = 0.8f;
        /// <summary>Water deeper than this (above the waist) -> she swims; shallower than SwimLeaveDepth -> wades out.</summary>
        public const float SwimEnterDepth = 1.05f, SwimLeaveDepth = 0.9f;
        /// <summary>Root (feet) height under the water line while swimming: the prone body floats at the surface.</summary>
        public const float SwimSink = 0.95f;
        /// <summary>The lake-island undertow: within this many metres of an island's shore the current pushes a
        /// swimmer back out (faster than she swims). Only walking on the water with the koi gets her there.</summary>
        public const float UndertowBand = 12f, UndertowSpeed = 2.8f;
        public static event System.Action UndertowHit;
        public event System.Action EnteredWater, SwimStroke;
        float _swimLevel, _strokeTimer, _undertowHintAt = -99f;
        ParticleSystem _ripples;
        /// <summary>Metres per second the undertow is pushing her this frame (0 = none).</summary>
        public float UndertowPush { get; private set; }

        void UpdateSwimState()
        {
            var p = transform.position;
            bool can = !World.EnvController.Indoors && !Riding && !Climbing;
            float level = 0f, g = GroundAt(p);
            bool water = can && World.WorldLayout.WaterSurface(p.x, p.z, out level);
            float depth = water ? level - g : 0f;
            if (Swimming && Creatures.PetSwitcher.Is("ink_koi") && water && depth > 0.3f)
            {
                // the koi became the current pet while she swims: up onto the water surface
                Place(new Vector3(p.x, level + 0.02f, p.z)); Swimming = false; _vy = 0f;
                if (_animator) _animator.SetBool(SwimId, false);
                return;
            }
            if (!Swimming)
            {
                if (water && !WaterWalking && depth > SwimEnterDepth && p.y < level - 0.7f)
                {
                    Swimming = true; _swimLevel = level; _jumping = false; _windup = -1f;
                    Debug.Log($"[Swim] in the water at {p} (depth {depth:0.0} m)");
                    EnteredWater?.Invoke();
                }
            }
            else if (!water || depth < SwimLeaveDepth) { Swimming = false; _vy = -2f; Debug.Log($"[Swim] out of the water at {p}"); }
            if (Swimming) _swimLevel = level;
        }

        /// <summary>Current of the river where she is (试玩反馈 1123d-2), downstream at up to `scale` x the local
        /// flow factor m/s; the lake and the pond are still.</summary>
        public Vector3 RiverPush { get; private set; }
        Vector3 RiverCurrent(Vector3 p, float scale)
        {
            RiverPush = World.WorldLayout.RiverFlow(p.x, p.z, out var d, out float sp) ? new Vector3(d.x, 0, d.y) * sp * scale : Vector3.zero;
            return RiverPush;
        }

        /// <summary>Extra horizontal velocity from the current round the lake islands.</summary>
        Vector3 Undertow(Vector3 p)
        {
            UndertowPush = 0f;
            if (!Swimming) return Vector3.zero;
            foreach (var isl in World.WorldLayout.Islands)
            {
                var d = new Vector3(p.x - isl.x, 0, p.z - isl.y);
                float dist = d.magnitude, shore = isl.z;
                if (dist > shore + UndertowBand || dist < 0.01f) continue;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shore + UndertowBand, shore + UndertowBand * 0.5f, dist));
                UndertowPush = UndertowSpeed * k;
                if (k > 0.3f && Time.time - _undertowHintAt > 10f) { _undertowHintAt = Time.time; UndertowHit?.Invoke(); }
                return d / dist * UndertowPush;
            }
            return Vector3.zero;
        }

        // 1123c-C2: interactive ripples drawn by the water shader (_WP_Ripples: xyz centre, w start time)
        static readonly Vector4[] _rippleBuf = new Vector4[8];
        static int _rippleNext; static bool _rippleInit;
        static readonly int RipplesId = Shader.PropertyToID("_WP_Ripples");
        float _rippleAt;
        public static void AddRipple(Vector3 p)
        {
            if (!_rippleInit) { _rippleInit = true; for (int i = 0; i < 8; i++) _rippleBuf[i] = new Vector4(0, 0, 0, -100f); }
            _rippleBuf[_rippleNext] = new Vector4(p.x, p.y, p.z, Time.timeSinceLevelLoad);
            _rippleNext = (_rippleNext + 1) % 8;
            Shader.SetGlobalVectorArray(RipplesId, _rippleBuf);
        }

        /// <summary>Ripple rings round her while swimming / walking on the water, plus a few drops thrown up by the strokes.</summary>
        void UpdateRipples()
        {
            bool on = Swimming || WaterWalking;
            var p0 = transform.position;
            bool inWater = !World.EnvController.Indoors && World.WorldLayout.WaterSurface(p0.x, p0.z, out float wl0) && wl0 > p0.y - 0.05f;
            if (!on && !inWater && !_ripples) return;
            if (!_ripples)
            {
                var go = new GameObject("SwimRipples", typeof(ParticleSystem));
                _ripples = go.GetComponent<ParticleSystem>();
                var main = _ripples.main;
                main.startLifetime = 1.6f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startColor = new Color(0.92f, 0.97f, 1f, 0.55f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 60;
                var sh = _ripples.shape; sh.enabled = false;
                var sz = _ripples.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 3.2f));
                var col = _ripples.colorOverLifetime; col.enabled = true;
                var gr = new Gradient();
                gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                           new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0, 1) });
                col.color = gr;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; r.sharedMaterial = World.FxMat.Ring;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var p = transform.position;
            float level = Swimming ? _swimLevel : p.y;
            _ripples.transform.position = new Vector3(p.x, level + 0.03f, p.z) + transform.forward * 0.35f;
            var em = _ripples.emission;
            em.rateOverTime = !on ? 0f : Swimming ? 1.2f + Speed * 1.6f : (Speed > 0.2f ? 2.5f : 0.6f);
            // rings in the water surface itself: swimming, walking on it, or wading through the shallows
            float wl = p.y;
            bool wading = !on && !World.EnvController.Indoors && World.WorldLayout.WaterSurface(p.x, p.z, out wl) && wl > p.y - 0.05f && wl < p.y + 1.2f;
            if ((on || wading) && Time.time - _rippleAt > (Speed > 0.2f ? 0.35f : 1.2f))
            {
                _rippleAt = Time.time;
                AddRipple(new Vector3(p.x, Swimming ? _swimLevel : (on ? p.y : wl), p.z));
            }
        }

        // ---- 1123c-B2: walking on water with the ink koi as current pet ----
        /// <summary>Standing / walking on the river or lake surface (koi ability).</summary>
        public bool WaterWalking { get; private set; }
        BoxCollider _waterFloor;
        ParticleSystem _splash;

        /// <summary>An invisible floor follows her at the water level while the koi is the current pet and she is
        /// over open water, so the character controller simply walks (and jumps) on it.</summary>
        void UpdateWaterFloor()
        {
            var p = transform.position;
            float level = 0f;
            bool over = Creatures.PetSwitcher.Is("ink_koi") && !World.EnvController.Indoors && !Riding &&
                        World.WorldLayout.WaterSurface(p.x, p.z, out level) && level > GroundAt(p) + 0.05f && p.y > level - 0.35f;
            if (!_waterFloor)
            {
                var go = new GameObject("KoiWaterFloor");
                _waterFloor = go.AddComponent<BoxCollider>();
                _waterFloor.size = new Vector3(3f, 0.2f, 3f);
            }
            _waterFloor.gameObject.SetActive(over);
            if (over) _waterFloor.transform.position = new Vector3(p.x, level - 0.1f, p.z);
            WaterWalking = over && p.y < level + 0.35f;
        }

        void UpdateWaterSplash()
        {
            if (!WaterWalking && !Swimming && !_splash) return;
            if (!_splash)
            {
                var go = new GameObject("WaterSteps", typeof(ParticleSystem));
                go.transform.SetParent(transform, false);
                _splash = go.GetComponent<ParticleSystem>();
                var main = _splash.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f); main.gravityModifier = 1.2f;
                main.startColor = new Color(0.9f, 0.97f, 1f, 0.85f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 200;
                var sh = _splash.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = 0.25f;
                go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = WildPartner.World.FxMat.Alpha;
            }
            _splash.transform.localPosition = Vector3.up * (Swimming ? SwimSink - 0.05f : 0f) + Vector3.forward * (Swimming ? 0.5f : 0f);
            var em = _splash.emission;
            em.rateOverTime = WaterWalking ? 6f + Speed * 18f : Swimming && Speed > 0.2f ? 3f + Speed * 5f : 0f;
        }

        /// <summary>Put her on the nearest dry bank (switching away from the koi while on the water).</summary>
        public void StepAshore()
        {
            var p = transform.position;
            for (float r = 1f; r <= 30f; r += 1f)
                for (int k = 0; k < 24; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f;
                    var q = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    q = ClampToWorld(q);
                    float g = GroundAt(q);
                    if (World.WorldLayout.WaterSurface(q.x, q.z, out float wl) && wl > g - 0.05f) continue;
                    if (Terrain.activeTerrain && Terrain.activeTerrain.terrainData.GetSteepness(q.x / World.WorldLayout.Size, q.z / World.WorldLayout.Size) > 35f) continue;
                    q.y = g + 0.1f;
                    if (_waterFloor) _waterFloor.gameObject.SetActive(false);
                    Place(q); _vy = -2f; WaterWalking = false;
                    Debug.Log($"[Pet] stepped ashore at {q}");
                    return;
                }
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
                // 1123c-B4 leftover: on a wall the camera swings round behind her to face the wall (unless the
                // player is turning it), so it is not squeezed against the wall or left in the trees beside it
                if (look.sqrMagnitude > 1e-4f) _lookIdle = 0f; else _lookIdle += Time.deltaTime;
                if ((Climbing || Mantling) && _lookIdle > 1.2f && _wallNormal.sqrMagnitude > 0.5f)
                {
                    float k = 1f - Mathf.Exp(-Time.deltaTime / 0.7f);
                    _orbit.HorizontalAxis.Value = Mathf.LerpAngle(_orbit.HorizontalAxis.Value, Quaternion.LookRotation(-_wallNormal).eulerAngles.y, k);
                    _orbit.VerticalAxis.Value = Mathf.Lerp(_orbit.VerticalAxis.Value, 16f, k);
                }
            }

            Vector2 input = Frozen ? Vector2.zero : GameInput.Move;
            Sneaking = GameInput.Sneak;   // holding still while capturing still counts as sneaking
            Running = !Frozen && GameInput.Run && !Sneaking && input.sqrMagnitude > 0.01f;
            float dt0 = Time.deltaTime;
            // 1123c-B4: climbing over a ledge / on a wall takes over the whole move
            if (_mantleT >= 0f) { MantleUpdate(dt0); GuardWorld(dt0, true); Noise = 0.8f; return; }
            if (!Climbing) Stamina = Mathf.Min(StaminaMax, Stamina + (_cc.isGrounded || Swimming ? 25f : 0f) * dt0);
            TryStartClimb(input, dt0);
            if (Climbing) { ClimbUpdate(input, dt0); GuardWorld(dt0, false); Noise = 0.8f; return; }
            UpdateSwimState();
            float target = Swimming ? (Sneaking ? swimSlowSpeed : Running ? swimFastSpeed : swimSpeed)
                         : Riding ? (Sneaking ? 1.4f : Running ? rideRunSpeed : rideWalkSpeed)
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
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, (Swimming ? 240f : turnSpeed) * Time.deltaTime);
            }

            // jump: buffered press + coyote time; vertical speed for the requested apex height
            float dt = Time.deltaTime;
            bool grounded = _cc.isGrounded;
            _sinceGrounded = grounded ? 0f : _sinceGrounded + dt;
            _sinceJumpPressed = (!Frozen && !Riding && !Swimming && GameInput.JumpPressed) ? 0f : _sinceJumpPressed + dt;
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
                    // 1123c-B2: the moon rabbit as current pet -> jumps 1.8x as high
                    _vy = Mathf.Sqrt(2f * -gravity * jumpHeight * (Creatures.PetSwitcher.Is("moon_rabbit") ? 1.8f : 1f));
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
            Vector3 drift = Vector3.zero;
            if (Swimming)
            {
                // float at the surface with a slow bob; no gravity, no jumping
                float targetY = Mathf.Max(_swimLevel - SwimSink + Mathf.Sin(Time.time * 2.6f) * 0.03f, GroundAt(transform.position) + 0.02f);
                _vy = Mathf.Clamp((targetY - transform.position.y) * 4f, -3f, 1.5f);
                _jumping = false; _windup = -1f; _airTime = 0f;
                drift = Undertow(transform.position) + RiverCurrent(transform.position, 0.5f);
                _strokeTimer += dt * Mathf.Max(0.35f, Speed / swimSpeed);
                if (_strokeTimer > 1.2f) { _strokeTimer -= 1.2f; if (Speed > 0.2f) SwimStroke?.Invoke(); }
            }
            else if (WaterWalking) drift = RiverCurrent(transform.position, 0.2f);   // the koi's footing slides a little downstream
            else RiverPush = Vector3.zero;
            UpdateWaterFloor();
            _cc.Move((transform.forward * Speed + Vector3.up * _vy + drift) * dt);
            GuardWorld(dt, _cc.isGrounded);
            UpdateWaterSplash();
            UpdateRipples();

            if (_animator)
            {
                // blend tree: Idle 0 / Walk 1.64 / Run 3.2 m/s at 1x; faster ground speed plays the run faster
                _animator.SetFloat(SpeedId, Riding || Swimming ? 0f : Mathf.Min(Speed, animRunSpeed), 0.08f, Time.deltaTime);
                _animator.SetBool(SwimId, Swimming);
                bool air = !Swimming && (_jumping || _airTime > 0.25f);   // short drops (stairs, bumps) stay on Locomotion
                _animator.SetBool(GroundedId, !air);
                _animator.SetFloat(AirTimeId, _airTime);
                bool capturing = Creatures.CaptureSystem.Target != null;
                bool sneakAnim = animSneakSpeed > 0.01f && Sneaking && !air && !capturing;
                if (animSneakSpeed > 0.01f)
                {
                    _animator.SetBool(SneakId, sneakAnim);
                    _animator.SetBool(CaptureId, capturing && !air);
                }
                _animator.speed = Swimming ? Mathf.Clamp(Speed / swimSpeed, 0.45f, 1.6f) : air || capturing || Riding ? 1f : sneakAnim ? Mathf.Clamp(Speed / animSneakSpeed, 1f, 2f) : Mathf.Max(1f, Speed / animRunSpeed);
                if (capturing)
                {
                    // turn to face the creature while drawing it in
                    Vector3 to = Creatures.CaptureSystem.Target.transform.position - transform.position; to.y = 0;
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
                }
            }

            Noise = Swimming ? (Speed < 0.2f ? 0.5f : Running ? 1.3f : 0.9f) : Riding ? (Speed < 0.2f ? 0.6f : Running ? 1.4f : 0.9f) : _jumping ? 1.2f : Speed < 0.2f ? (Sneaking ? 0.35f : 0.55f) : Sneaking ? 0.45f : Running ? 1.6f : 1f;

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
