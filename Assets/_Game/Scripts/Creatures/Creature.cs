using UnityEngine;
using UnityEngine.AI;
using WildPartner.Core;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Creatures
{
    /// <summary>
    /// One wild creature. State machine: Hidden -> Wander -> Alert -> Flee (-> Wander) ; Captured -> Companion.
    /// Alert is a meter (0..1) that rises while the player is inside alertRadius * player.Noise and falls
    /// otherwise; reaching 1 makes the creature flee and interrupts any capture in progress (FR-G04).
    /// Logic lives on the root; the model is the "Visual" child so models can be swapped with zero code
    /// changes (PRD 4.2).
    /// </summary>
    public class Creature : MonoBehaviour
    {
        public enum State { Hidden, Wander, Alert, Flee, Companion }

        public string defId;
        public CreatureDef Def { get; private set; }
        public State Current { get; private set; } = State.Hidden;
        public float AlertLevel { get; private set; }
        public int CompanionSlot { get; set; }
        /// <summary>Ground speed of the rigged Run clip at 1x (from the Blender export); 0 = not rigged.</summary>
        public float animRunSpeed;
        /// <summary>Ground speed of the rigged Walk clip at 1x; 0 = not rigged.</summary>
        public float animWalkSpeed;
        /// <summary>0 none, 1 sitting + licking, 2 lying belly-up, 3 rubbing against the witch (cat only).</summary>
        public int Mood { get; private set; }

        /// <summary>The cat meowed (leading the witch to a creature) at this position.</summary>
        public static event System.Action<Vector3> Meowed;
        static readonly System.Collections.Generic.List<Creature> s_all = new System.Collections.Generic.List<Creature>();
        float _leadCooldown = 25f, _leadTime, _nextMeow;
        Vector3 _leadSpot;
        Creature _leadTarget;
        bool _leadArrived;

        Animator _animator;
        float _stillTime, _rubTime, _pauseUntil;
        bool _hasMood, _paused;
        static readonly int SpeedId = Animator.StringToHash("Speed"), MoodId = Animator.StringToHash("Mood");

        NavMeshAgent _agent;
        ProceduralAnimator _anim;
        Transform _visual;
        Vector3 _target;
        float _stateTime, _nextWander;
        int _anchor;
        Vector3 _velocity;
        float _appear;            // 0..1 grow-in scale
        Vector3 _visualScale;

        public bool CanBeCaptured => Current == State.Wander || Current == State.Alert;

        void Awake()
        {
            Def = CreatureDefs.Get(defId);
            _agent = GetComponent<NavMeshAgent>();
            _anim = GetComponent<ProceduralAnimator>();
            _visual = transform.Find("Visual");
            s_all.Add(this);
            _animator = GetComponentInChildren<Animator>();
            if (_animator && _animator.runtimeAnimatorController)
                foreach (var prm in _animator.parameters) if (prm.nameHash == MoodId) _hasMood = true;
            if (_visual) _visualScale = _visual.localScale;
        }

        public void Init(int anchor, bool startHidden)
        {
            _anchor = anchor;
            if (_agent) _agent.enabled = false;
            Place(AnchorPos(anchor));
            SetState(startHidden ? State.Hidden : State.Wander);
        }

        Vector3 AnchorPos(int i)
        {
            var a = Def.anchors[Mathf.Clamp(i, 0, Def.anchors.Length - 1)];
            return GroundPoint(a.x, a.y);
        }

        public static float GroundHeight(float x, float z)
        {
            var t = Terrain.activeTerrain;
            return t ? t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y : 0;
        }

        Vector3 GroundPoint(float x, float z)
        {
            float y;
            if (Def.movement == Movement.Water)
            {
                WorldLayout.RiverDistance(x, z, out float wl);
                y = wl + 0.05f;
            }
            else y = GroundHeight(x, z) + Def.hover;
            return new Vector3(x, y, z);
        }

        void Place(Vector3 p)
        {
            if (_agent && Def.movement == Movement.Ground)
            {
                if (NavMesh.SamplePosition(p, out var hit, 20f, NavMesh.AllAreas)) p = hit.position;
                transform.position = p;
                _agent.enabled = true;
                _agent.Warp(p);
            }
            else transform.position = p;
        }

        void SetState(State s)
        {
            Current = s;
            _stateTime = 0;
            _pauseUntil = 0; _paused = false;
            bool visible = s != State.Hidden;
            if (_visual) _visual.gameObject.SetActive(visible);
            foreach (var c in GetComponents<Collider>()) c.enabled = visible;
            if (_agent && _agent.enabled)
            {
                _agent.isStopped = !visible;
                _agent.speed = s == State.Flee ? Def.fleeSpeed : s == State.Companion ? 6f : Def.wanderSpeed;
                _agent.acceleration = s == State.Flee ? 40f : 12f;
            }
            if (s == State.Wander) _nextWander = 0;
            if (s == State.Hidden) _appear = 0;
        }

        /// <summary>Called by the spawner once per second.</summary>
        public void UpdateAvailability(float hour, bool raining, Vector3 player)
        {
            if (Current == State.Companion) return;
            bool want = Def.ConditionMet(hour, raining);
            if (Current == State.Hidden && want)
            {
                // Appear at the anchor farthest from the player that is still in the habitat.
                int best = 0; float bestD = -1;
                for (int i = 0; i < Def.anchors.Length; i++)
                {
                    float d = Vector2.Distance(Def.anchors[i], new Vector2(player.x, player.z));
                    if (d > 25f && (bestD < 0 || d < bestD)) { best = i; bestD = d; }
                }
                _anchor = best;
                Place(AnchorPos(best));
                SetState(State.Wander);
                Debug.Log($"[Creature] {Def.id} appears at anchor {best} ({Def.ConditionText})");
            }
            else if (Current != State.Hidden && !want && Vector3.Distance(transform.position, player) > 25f)
            {
                SetState(State.Hidden);
                Debug.Log($"[Creature] {Def.id} leaves ({Def.ConditionText} over)");
            }
        }

        public void BecomeCompanion(int slot)
        {
            CompanionSlot = slot;
            AlertLevel = 0;
            if (_agent && !_agent.enabled && Def.movement == Movement.Ground) Place(transform.position);
            SetState(State.Companion);
            if (_agent && _agent.enabled) { _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance; _agent.avoidancePriority = 40 + slot; _agent.stoppingDistance = 0.8f; }
            if (_agent && Def.id == "cat") _agent.angularSpeed = 260f;   // gets up and turns after her, never on the spot
        }

        /// <summary>Companion follows the witch through a cut (cabin door): appear next to `p` at once.</summary>
        public void WarpNear(Vector3 p, float yaw)
        {
            if (Def.movement == Movement.Ground && _agent)
            {
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
                if (_agent.enabled) _agent.Warp(p); else transform.position = p;
                if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
            }
            else transform.position = p + Vector3.up * (Def.id == "ink_koi" ? 1.3f : 1.8f);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            _velocity = Vector3.zero;
        }

        /// <summary>Spawn directly as a companion next to the player (loading a save).</summary>
        public void RestoreAsCompanion(int slot, Vector3 near)
        {
            SetState(State.Wander);
            Place(near + new Vector3(Mathf.Cos(slot * 1.3f), 0, Mathf.Sin(slot * 1.3f)) * 2.5f);
            if (Def.movement != Movement.Ground) transform.position = near + Vector3.up * 1.4f + new Vector3(slot - 2, 0, -1.5f);
            BecomeCompanion(slot);
            _appear = 1;
        }

        void Update()
        {
            if (Current == State.Hidden) return;
            _stateTime += Time.deltaTime;
            _appear = Mathf.MoveTowards(_appear, 1, Time.deltaTime * 1.5f);
            if (_visual && Current != State.Companion) _visual.localScale = _visualScale * Mathf.SmoothStep(0.01f, 1, _appear);

            var player = PlayerController.Instance;
            if (!player) return;
            Vector3 pp = player.transform.position;
            Vector3 toPlayer = pp - transform.position; toPlayer.y = 0;
            float dist = toPlayer.magnitude;

            if (Current == State.Companion) { UpdateCompanion(player); DriveAnimator(player); return; }

            // Senses
            float radius = Def.alertRadius * player.Noise;
            bool sensed = dist < radius;
            // Sneaking makes the meter rise much slower; holding capture while sneaking calms the creature
            // (it "accepts" you), holding it while not sneaking only halves the rise -> the hint "sneak up".
            float rate = sensed ? Def.alertRate * Mathf.Lerp(2.2f, 1f, dist / Mathf.Max(radius, 0.01f)) * (player.Sneaking ? 0.35f : 1f) : -0.25f;
            if (CaptureSystem.Target == this) rate = player.Sneaking ? -0.15f : (sensed ? rate * 0.5f : -0.1f);
            AlertLevel = Mathf.Clamp01(AlertLevel + rate * Time.deltaTime);
            if (_anim) _anim.alert = Current == State.Alert ? AlertLevel : 0;

            switch (Current)
            {
                case State.Wander:
                    if (AlertLevel > 0.25f) { SetState(State.Alert); break; }
                    // held by the capture spell: it stops and looks at you instead of strolling out of range
                    if (CaptureSystem.Target == this) { if (_agent && _agent.enabled) _agent.isStopped = true; FaceTowards(toPlayer); break; }
                    Wander();
                    break;
                case State.Alert:
                    if (_agent && _agent.enabled) _agent.isStopped = true;
                    FaceTowards(toPlayer);
                    if (AlertLevel >= 1f) Flee(pp);
                    else if (AlertLevel < 0.1f) SetState(State.Wander);
                    break;
                case State.Flee:
                    MoveTo(_target, Def.fleeSpeed);
                    if (_stateTime > 6f || Arrived(_target))
                    {
                        AlertLevel = 0.3f;
                        SetState(State.Wander);
                    }
                    break;
            }
        }

        void Wander()
        {
            if (Def.wanderSpeed <= 0.01f) return; // perched (owl) stays put until disturbed
            // rigged grazers stop at the end of a stroll and graze for a while (deer: GrazeDown -> Graze)
            if (_pauseUntil > _stateTime)
            {
                if (_agent && _agent.enabled) _agent.isStopped = true;
                return;
            }
            if (_hasMood && !_paused && Def.id != "cat" && Arrived(_target) && Random.value < 0.75f)
            {
                _paused = true;
                _pauseUntil = _stateTime + Random.Range(5f, 12f);
                return;
            }
            if (_stateTime > _nextWander || Arrived(_target))
            {
                _paused = false;
                var a = Def.anchors[_anchor];
                var r = Random.insideUnitCircle * Def.wanderRadius;
                _target = GroundPoint(a.x + r.x, a.y + r.y);
                _nextWander = _stateTime + Random.Range(4f, 9f);
            }
            MoveTo(_target, Def.wanderSpeed);
        }

        void Flee(Vector3 playerPos)
        {
            CaptureSystem.NotifyFled(this);
            SetState(State.Flee);
            if (Def.movement == Movement.Flying && Def.wanderSpeed <= 0f)   // perched flyers hop to another perch
            {
                // fly to a different perch
                _anchor = (_anchor + 1 + Random.Range(0, Def.anchors.Length - 1)) % Def.anchors.Length;
                _target = AnchorPos(_anchor);
            }
            else
            {
                Vector3 away = transform.position - playerPos; away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away = away.normalized * Def.alertRadius * 1.8f;
                Vector3 p = transform.position + away;
                // stay inside the habitat
                var a = Def.anchors[_anchor];
                var off = new Vector2(p.x - a.x, p.z - a.y);
                if (off.magnitude > Def.wanderRadius * 2f) off = off.normalized * Def.wanderRadius * 2f;
                _target = GroundPoint(a.x + off.x, a.y + off.y);
            }
            Debug.Log($"[Creature] {Def.id} flees");
        }

        bool Arrived(Vector3 t)
        {
            var d = t - transform.position; d.y = 0;
            return d.magnitude < 1.2f;
        }

        void MoveTo(Vector3 target, float speed)
        {
            if (Def.movement == Movement.Ground && _agent && _agent.enabled)
            {
                _agent.isStopped = false;
                _agent.speed = speed;
                if ((_agent.destination - target).sqrMagnitude > 1f) _agent.SetDestination(target);
                return;
            }
            // Flying / swimming: steer directly, keep height above ground / on the water.
            Vector3 to = target - transform.position;
            Vector3 desired = to.magnitude > 0.1f ? to.normalized * speed : Vector3.zero;
            _velocity = Vector3.Lerp(_velocity, desired, 1 - Mathf.Exp(-3f * Time.deltaTime));
            Vector3 p = transform.position + _velocity * Time.deltaTime;
            if (Def.movement == Movement.Water)
            {
                float d = WorldLayout.RiverDistance(p.x, p.z, out float wl);
                if (d > WorldLayout.RiverHalfWidth - 1f) { p = transform.position; _velocity = -_velocity * 0.5f; _target = AnchorPos(_anchor); }
                p.y = wl + 0.05f;
            }
            else
            {
                float g = GroundHeight(p.x, p.z) + (Def.hover > 0 ? Def.hover : (Current == State.Flee ? 4f : 0f));
                p.y = Mathf.Lerp(p.y, g, 1 - Mathf.Exp(-4f * Time.deltaTime));
            }
            transform.position = p;
            FaceTowards(_velocity);
        }

        void LateUpdate()
        {
            if (_animator && Current != State.Companion && Current != State.Hidden) DriveAnimator(PlayerController.Instance);
        }

        /// <summary>Rigged models: blend-tree speed from the actual ground speed (playback sped up above the Run
        /// clip's own speed so the paws keep up), and the cat's moods while it keeps the witch company:
        /// she stands still a moment -> sit and lick a paw; ~20 s -> lie down belly-up; she walks up to it -> rub.</summary>
        void DriveAnimator(PlayerController player)
        {
            float dt = Time.deltaTime;
            Vector3 v = _agent && _agent.enabled ? _agent.velocity : _velocity;
            float speed = Vector3.ProjectOnPlane(v, Vector3.up).magnitude;
            int mood = Current == State.Wander && _pauseUntil > _stateTime ? 1 : 0;
            if (Def.id == "cat" && Current == State.Companion && player)
            {
                Vector3 toCat = transform.position - player.transform.position; toCat.y = 0;
                bool approaching = !_leadArrived && player.Speed > 0.4f && toCat.magnitude < 1.6f && Vector3.Dot(player.transform.forward, toCat.normalized) > 0.6f;
                if (approaching && _rubTime <= 0f && Mood != 3) _rubTime = 2.6f;
                _rubTime -= dt;
                _stillTime = player.Speed < 0.1f && speed < 0.25f ? _stillTime + dt : 0f;
                mood = _leadArrived ? 4 : _rubTime > 0f ? 3 : _stillTime > 20f ? 2 : _stillTime > 2.5f ? 1 : 0;
                if (mood == 3)
                {
                    // walk up to her legs and lean in
                    Vector3 to = -toCat;
                    FaceTowards(to);
                    if (_agent && _agent.enabled)
                    {
                        _agent.isStopped = to.magnitude < 0.55f;
                        if (to.magnitude >= 0.55f) { _agent.speed = 1.2f; _agent.SetDestination(player.transform.position - to.normalized * 0.45f); }
                    }
                }
            }
            Mood = mood;
            if (!_animator) return;
            float run = animRunSpeed > 0.01f ? animRunSpeed : 1f;
            float walk = animWalkSpeed > 0.01f ? animWalkSpeed : run * 0.3f;
            // up to 1.5x the walk clip's speed: keep walking, just faster; beyond that blend towards the run
            // and speed the run up once past its own speed — the hooves / paws keep up with the ground
            bool walking = speed <= walk * 1.5f;
            float param = walking ? Mathf.Min(speed, walk) : Mathf.Min(speed, run);
            float play = walking ? Mathf.Max(1f, speed / walk) : Mathf.Clamp(speed / run, 1f, 2.6f);
            _animator.SetFloat(SpeedId, param, 0.1f, dt);
            if (_hasMood) _animator.SetInteger(MoodId, mood);
            _animator.speed = mood == 0 ? play : 1f;
        }

        void FaceTowards(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0004f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-6f * Time.deltaTime));
        }

        void OnDestroy() => s_all.Remove(this);
        /// <summary>Cat is standing ahead of the witch, pointing at a wild creature (smoke test / UI).</summary>
        public bool IsLeading => _leadArrived;
        public void LeadSoon() => _leadCooldown = 0f;

        /// <summary>Cat only (玩法设计 tier 2 "带路"): every so often while the witch is on the move, if a wild
        /// creature is out within 50 m, the cat trots a few metres ahead towards it, stops, raises a paw and
        /// meows at it (Mood 4). Returns true while leading (the formation follow is skipped).</summary>
        bool UpdateLead(PlayerController player)
        {
            float dt = Time.deltaTime;
            _leadCooldown -= dt;
            if (_leadTime <= 0f && _leadCooldown <= 0f && player.Speed > 0.5f
                && Vector3.Distance(transform.position, player.transform.position) < 12f)
            {
                _leadCooldown = 40f;
                Creature best = null; float bd = 50f;
                foreach (var c in s_all)
                {
                    if (c == this || (c.Current != State.Wander && c.Current != State.Alert)) continue;
                    float d = Vector3.Distance(c.transform.position, player.transform.position);
                    if (d < bd) { bd = d; best = c; }
                }
                if (best && bd > 8f)
                {
                    Vector3 dir = best.transform.position - player.transform.position; dir.y = 0; dir.Normalize();
                    Vector3 spot = player.transform.position + dir * 4.5f;
                    if (NavMesh.SamplePosition(spot, out var hit, 3f, NavMesh.AllAreas)) spot = hit.position;
                    _leadTarget = best; _leadSpot = spot; _leadTime = 7f; _leadArrived = false;
                    Debug.Log($"[Cat] leading towards {best.Def.id} ({bd:0} m)");
                }
            }
            if (_leadTime <= 0f) { _leadArrived = false; return false; }
            _leadTime -= dt;
            if (!_leadTarget || (_leadTarget.Current != State.Wander && _leadTarget.Current != State.Alert)) { _leadTime = 0f; return false; }
            Vector3 to = _leadSpot - transform.position; to.y = 0;
            if (!_leadArrived && to.magnitude < 0.6f)
            {
                _leadArrived = true; _nextMeow = 0.3f;
                Debug.Log("[Cat] pointing + meow");
                if (GameFlow.Instance) GameFlow.Instance.ShowToast("喵——小猫好像发现了什么，朝它看的方向找找吧。");
            }
            if (_agent && _agent.enabled)
            {
                _agent.isStopped = _leadArrived;
                if (!_leadArrived) { _agent.speed = player.runSpeed; _agent.SetDestination(_leadSpot); }
            }
            if (_leadArrived)
            {
                Vector3 look = _leadTarget.transform.position - transform.position; look.y = 0;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 300f * dt);
                _nextMeow -= dt;
                if (_nextMeow <= 0f) { Meowed?.Invoke(transform.position); _nextMeow = 1.5f; }   // two meows per Point loop (3 s)
            }
            return true;
        }

        // ---- 试玩反馈 1123b-5: when the witch stops, the cat settles ~0.5 m on her right -------------------------
        float _playerStill;
        bool _hasSpot;
        Vector3 _spot;
        /// <summary>Which side the cat chose last time (0 right, 1 left, 2 behind; -1 none) - smoke test / debug.</summary>
        public int SideSpotKind { get; private set; } = -1;
        public Vector3 SideSpot => _spot;
        public bool AtSideSpot { get; private set; }

        /// <summary>She has stood still for a moment: walk (not run) to the spot beside her right leg, turn to face
        /// roughly where she faces, and stay there; the sit / lick / belly-up moods then start from that spot
        /// (DriveAnimator). Returns false while she moves, so the normal follow-behind takes over again; the
        /// NavMeshAgent turns at its own limited angular speed, so getting up and following is never a snap turn.</summary>
        bool UpdateSideSpot(PlayerController player)
        {
            float dt = Time.deltaTime;
            _playerStill = player.Speed < 0.1f && !player.Airborne ? _playerStill + dt : 0f;
            if (_playerStill < 0.35f || _rubTime > 0f || !_agent || !_agent.enabled)
            {
                if (_hasSpot && _agent && _agent.enabled) _agent.stoppingDistance = 0.8f;
                _hasSpot = false; AtSideSpot = false;
                return false;
            }
            if (!_hasSpot)
            {
                _spot = PickSideSpot(player, out int kind);
                SideSpotKind = kind;
                _hasSpot = true;
            }
            Vector3 to = _spot - transform.position; to.y = 0;
            if (to.magnitude > 0.12f && !AtSideSpot)
            {
                _agent.isStopped = false;
                _agent.stoppingDistance = 0.05f;
                _agent.speed = Mathf.Lerp(1.1f, 2.4f, Mathf.InverseLerp(1f, 4f, to.magnitude));   // walk over, trot if far
                _agent.SetDestination(_spot);
            }
            else
            {
                AtSideSpot = true;
                _agent.isStopped = true;
                FaceTowards(player.transform.forward);
            }
            return true;
        }

        static readonly Collider[] s_hits = new Collider[16];

        /// <summary>0.5 m to her right (by her facing); if a wall, tree, rock or the edge of the walkable area is
        /// there, the left side; then behind her; as a last resort the nearest walkable point behind.</summary>
        Vector3 PickSideSpot(PlayerController player, out int kind)
        {
            var pt = player.transform;
            Vector3 p = pt.position, r = pt.right, f = pt.forward;
            Vector3[] cands = { p + r * 0.5f, p - r * 0.5f, p - f * 0.65f };
            for (int i = 0; i < cands.Length; i++)
                if (SpotFree(p, cands[i], out var ok)) { kind = i; return ok; }
            kind = 3;
            return NavMesh.SamplePosition(p - f * 0.8f, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : transform.position;
        }

        bool SpotFree(Vector3 from, Vector3 c, out Vector3 spot)
        {
            spot = c;
            if (!NavMesh.SamplePosition(c, out var hit, 0.3f, NavMesh.AllAreas)) return false;
            if (Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(c.x, c.z)) > 0.15f) return false;
            if (NavMesh.Raycast(from, hit.position, out _, NavMesh.AllAreas)) return false;   // a wall / edge between
            int n = Physics.OverlapSphereNonAlloc(hit.position + Vector3.up * 0.25f, 0.13f, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_hits[i];
                if (col is TerrainCollider || col.GetComponentInParent<Creature>() || col.GetComponentInParent<PlayerController>()) continue;
                return false;   // trunk, rock, furniture, wall
            }
            spot = hit.position;
            return true;
        }

        void UpdateCompanion(PlayerController player)
        {
            if (Def.id == "cat" && Def.movement == Movement.Ground && UpdateLead(player)) return;
            if (Def.id == "cat" && Def.movement == Movement.Ground && UpdateSideSpot(player)) return;
            // Formation slots behind the player so companions never stack on each other.
            float ang = 140f + CompanionSlot * 22f;
            float side = CompanionSlot % 2 == 0 ? 1 : -1;
            Vector3 offset = Quaternion.Euler(0, side * (ang - 140f) + 180f, 0) * Vector3.forward * (2.2f + CompanionSlot * 0.6f);
            Vector3 slot = player.transform.position + player.transform.rotation * offset;
            if (Def.movement == Movement.Ground && _agent && _agent.enabled)
            {
                float d = Vector3.Distance(transform.position, slot);
                _agent.isStopped = false;
                _agent.speed = Mathf.Lerp(3f, player.runSpeed + 1.5f, Mathf.InverseLerp(2f, 10f, d));
                if (d > 1f) _agent.SetDestination(slot);
                if (d > 40f) { _agent.Warp(slot); }
            }
            else
            {
                // flyers and the koi (floating in a water bubble) hover at shoulder height
                Vector3 hover = slot + Vector3.up * (Def.id == "ink_koi" ? 1.3f : 1.8f);
                transform.position = Vector3.SmoothDamp(transform.position, hover, ref _velocity, 0.5f, 20f);
                if (Vector3.Distance(transform.position, hover) > 40f) transform.position = hover;
                FaceTowards(_velocity.sqrMagnitude > 0.05f ? _velocity : player.transform.forward);
            }
        }
    }
}
