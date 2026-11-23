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
        public enum State { Hidden, Wander, Alert, Flee, Companion, Startled }

        public string defId;
        /// <summary>Which of the species' <see cref="CreatureDef.homes"/> this one lives at (0 for single creatures).</summary>
        public int instance;
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

        /// <summary>The agent is usable: enabled and standing on the NavMesh. When it has slipped off (water, a cliff edge, a

        /// teleport) it is snapped back to the nearest mesh point within 3 m first; agent calls off the mesh log errors.</summary>

        bool AgentOn

        {

            get

            {

                if (!_agent || !_agent.enabled) return false;

                if (_agent.isOnNavMesh) return true;

                if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) _agent.Warp(hit.position);

                return _agent.isOnNavMesh;

            }

        }
        ProceduralAnimator _anim;
        Transform _visual;
        Vector3 _target;
        float _stateTime, _nextWander;
        int _anchor;
        Vector3 _velocity;
        float _appear;            // 0..1 grow-in scale
        Vector3 _visualScale;

        /// <summary>Wild, calm enough, and its species not collected yet: once one of a species is caught the others
        /// stay wild, still run from her, but show no prompt and cannot be caught (试玩反馈 1123b-6).</summary>
        public bool CanBeCaptured => (Current == State.Wander || Current == State.Alert) && !SpeciesTaken && !Def.fishable;
        /// <summary>Moved by someone else for now (the koi on the line, 1123d-1): no senses, no wandering.</summary>
        public bool Scripted;
        public bool SpeciesTaken => Current != State.Companion && GameFlow.Instance != null && GameFlow.Instance.Save.HasCaught(defId);
        /// <summary>Centre of its home range: its own home for multi-instance species, else the current anchor.</summary>
        public Vector2 Home => Def.homes != null ? Def.homes[Mathf.Clamp(instance, 0, Def.homes.Length - 1)] : Def.anchors[Mathf.Clamp(_anchor, 0, Def.anchors.Length - 1)];

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
            FindRabbitBones();
            if (Def != null && Def.id == "moon_butterfly" && !GetComponent<ButterflyGlow>()) gameObject.AddComponent<ButterflyGlow>();   // 1123e-3
        }

        public void Init(int anchor, bool startHidden)
        {
            if (Ridden && PlayerController.Instance) { Ridden = false; PlayerController.Instance.SetMount(null, 0f); RidePrompt = null; }
            _anchor = anchor;
            if (_agent) _agent.enabled = false;
            Place(AnchorPos(anchor));
            SetState(startHidden ? State.Hidden : State.Wander);
        }

        Vector3 AnchorPos(int i)
        {
            var a = Def.homes != null ? Home : Def.anchors[Mathf.Clamp(i, 0, Def.anchors.Length - 1)];
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
                // lake, pond or river; a point on dry land is moved to the nearest water deep enough to swim in
                if (!SwimmablePoint(ref x, ref z, out float wl)) WorldLayout.RiverDistance(x, z, out wl);
                y = wl - 0.06f;
            }
            else y = GroundHeight(x, z) + Def.hover;
            return new Vector3(x, y, z);
        }

        /// <summary>Water at least 0.35 m deep at (x, z), or the nearest such point within 8 m (spiral search).</summary>
        public static bool SwimmablePoint(ref float x, ref float z, out float level)
        {
            for (int ring = 0; ring <= 8; ring++)
            {
                int n = ring == 0 ? 1 : ring * 6;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n, px = x + Mathf.Cos(a) * ring, pz = z + Mathf.Sin(a) * ring;
                    if (WorldLayout.WaterSurface(px, pz, out level) && level - GroundHeight(px, pz) > 0.35f) { x = px; z = pz; return true; }
                }
            }
            level = 0f;
            return false;
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
            if (AgentOn)
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
            if (Current == State.Hidden && want && Def.homes != null)
            {
                // several of a kind: each comes out at its own home, never right next to her
                if (Vector2.Distance(Home, new Vector2(player.x, player.z)) < 18f) return;
                Place(AnchorPos(0));
                SetState(State.Wander);
            }
            else if (Current == State.Hidden && want)
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
            else if (Current != State.Hidden && Current != State.Startled && !want && Vector3.Distance(transform.position, player) > 25f)
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
            if (AgentOn) { _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance; _agent.avoidancePriority = 40 + slot; _agent.stoppingDistance = 0.8f; }
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
            if (Current == State.Hidden || Scripted) return;
            _stateTime += Time.deltaTime;
            _appear = Mathf.MoveTowards(_appear, 1, Time.deltaTime * 1.5f);
            if (_visual && Current != State.Companion) _visual.localScale = _visualScale * Mathf.SmoothStep(0.01f, 1, _appear);

            var player = PlayerController.Instance;
            if (!player) return;
            Vector3 pp = player.transform.position;
            Vector3 toPlayer = pp - transform.position; toPlayer.y = 0;
            float dist = toPlayer.magnitude;

            if (Current == State.Companion) { UpdateCompanion(player); DriveAnimator(player); return; }

            if (Current == State.Startled) { UpdateStartled(player, toPlayer); return; }

            // Senses
            float radius = Def.alertRadius * player.Noise * (player.Sneaking ? Def.sneakRadiusMul : 1f);
            bool sensed = dist < radius;
            float senseMul = 1f;
            if (Def.viewAngle > 0f)
            {
                // 1123c-B1 (deer): eyes see a cone in front (much shorter while grazing, head down); ears hear all
                // round by how much noise she makes (run > walk > sneak). Seen = the meter rises fast; only heard =
                // slower, and it turns to look.
                Vector3 fwd = transform.forward; fwd.y = 0;
                SeesPlayer = Vector3.Angle(fwd, toPlayer) < Def.viewAngle * 0.5f &&
                             dist < Def.alertRadius * (Grazing ? Def.grazeViewMul : 1f) * (player.Sneaking ? 0.75f : 1f);
                HearsPlayer = dist < Def.hearRadius * player.Noise;
                sensed = SeesPlayer || HearsPlayer;
                radius = SeesPlayer ? Def.alertRadius : Def.hearRadius * player.Noise;
                senseMul = SeesPlayer ? 1f : 0.6f;
            }
            // Sneaking makes the meter rise much slower; holding capture while sneaking calms the creature
            // (it "accepts" you), holding it while not sneaking only halves the rise -> the hint "sneak up".
            // (in plain view sneaking helps less: 0.7 instead of 0.35)
            float rate = sensed ? Def.alertRate * senseMul * Mathf.Lerp(2.2f, 1f, dist / Mathf.Max(radius, 0.01f)) * (player.Sneaking ? (SeesPlayer ? 0.7f : 0.35f) : 1f) : -0.25f;
            if (CaptureSystem.Target == this)
                rate = Def.viewAngle > 0f && SeesPlayer ? Mathf.Max(rate * 0.8f, 0.9f)   // casting the spell in front of a deer: it bolts (~1 s)
                     : player.Sneaking ? -0.15f : (sensed ? rate * 0.5f : -0.1f);
            // deer: once alert (head up), standing still lets it relax again
            else if (Def.viewAngle > 0f && Current == State.Alert && player.Speed < 0.2f && !player.Airborne) rate = -0.3f;
            AlertLevel = Mathf.Clamp01(AlertLevel + rate * Time.deltaTime);
            if (_anim) _anim.alert = Current == State.Alert ? AlertLevel : 0;

            switch (Current)
            {
                case State.Wander:
                    if (AlertLevel > 0.25f) { SetState(State.Alert); break; }
                    // held by the capture spell: it stops and looks at you instead of strolling out of range
                    if (CaptureSystem.Target == this) { if (AgentOn) _agent.isStopped = true; if (Def.viewAngle <= 0f || SeesPlayer) FaceTowards(toPlayer); break; }   // a deer held from behind stays put (1123c-B1)
                    Wander();
                    break;
                case State.Alert:
                    if (AgentOn) _agent.isStopped = true;
                    if (sensed || Def.viewAngle <= 0f) FaceTowards(toPlayer);   // a deer that lost track of her keeps looking where it was
                    if (AlertLevel >= 1f)
                    {
                        if (Def.startleFirst && CaptureSystem.Target == this) Startle();
                        else Flee(pp);
                    }
                    else if (AlertLevel < 0.1f) SetState(State.Wander);
                    break;
                case State.Flee:
                    MoveTo(_target, Def.fleeSpeed);
                    if (_stateTime > 6f || Arrived(_target))
                    {
                        AlertLevel = 0.3f;
                        SetState(State.Wander);
                        if (Def.grazes) { _paused = true; _pauseUntil = Random.Range(3f, 7f); }   // stops and grazes again
                    }
                    break;
            }
        }

        void Wander()
        {
            if (Def.wanderSpeed <= 0.01f)   // perched: stays put until disturbed (and settles back on its perch after)
            {
                if (Def.movement == Movement.Flying && (transform.position - AnchorPos(_anchor)).sqrMagnitude > 0.004f) MoveTo(AnchorPos(_anchor), 1.2f);
                return;
            }
            // rigged grazers stop at the end of a stroll and graze for a while (deer: GrazeDown -> Graze)
            if (_pauseUntil > _stateTime)
            {
                if (AgentOn) _agent.isStopped = true;
                return;
            }
            if (_paused) { _paused = false; NewWanderTarget(); _walkTime = 0f; }
            // grazers stop to graze: deer every ~3 s of walking (试玩反馈 1123b-8), rabbits mostly at the end of a
            // stroll; rigged ones play GrazeDown -> Graze (Mood 1), the rabbit lowers its head procedurally
            if (Def.grazes || (_hasMood && Def.id != "cat"))
            {
                _walkTime += Time.deltaTime;
                if (_burstLen <= 0f) _burstLen = Def.walkBurst * Random.Range(0.75f, 1.3f);
                bool burstDone = Def.walkBurst > 0f && _walkTime > _burstLen;
                if (burstDone || (Arrived(_target) && Random.value < 0.75f))
                {
                    _paused = true;
                    _pauseUntil = _stateTime + (Def.walkBurst > 0f ? Random.Range(3.5f, 7f) : Random.Range(4f, 10f));
                    _burstLen = 0f;
                    if (AgentOn) _agent.isStopped = true;
                    return;
                }
            }
            if (_stateTime > _nextWander || Arrived(_target)) NewWanderTarget();
            MoveTo(_target, Def.wanderSpeed);
        }

        float _walkTime, _burstLen;

        void NewWanderTarget()
        {
            var a = Home;
            var r = Random.insideUnitCircle * Def.wanderRadius;
            _target = GroundPoint(a.x + r.x, a.y + r.y);
            _nextWander = _stateTime + Random.Range(4f, 9f);
        }

        // ---- 试玩反馈 1123b-6: a spoiled capture first only startles the rabbit -------------------------------
        void Startle()
        {
            CaptureSystem.NotifyStartled(this);
            SetState(State.Startled);
            AlertLevel = 1f;
            Debug.Log($"[Creature] {Def.id} startled (ears up)");
        }

        /// <summary>Ears up, frozen, staring at her for about a second. If she moves (or jumps) it runs; if she keeps
        /// still it relaxes back to Alert at half the meter and she may try again.</summary>
        void UpdateStartled(PlayerController player, Vector3 toPlayer)
        {
            if (AgentOn) _agent.isStopped = true;
            FaceTowards(toPlayer);
            if (player.Speed > 0.3f || player.Airborne) { Flee(player.transform.position); return; }
            if (_stateTime > 1.1f)
            {
                AlertLevel = 0.45f;
                SetState(State.Alert);
                Debug.Log($"[Creature] {Def.id} relaxes");
            }
        }
        public bool IsStartled => Current == State.Startled;
        /// <summary>Smoke test only: put the alert meter at a value (as if she had been noticed).</summary>
        public void DebugSetAlert(float a) { AlertLevel = a; if (Current == State.Wander && a > 0.25f) SetState(State.Alert); }

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
                away = away.normalized * (Def.fleeDistance > 0f ? Def.fleeDistance : Def.alertRadius * 1.8f);
                Vector3 p = transform.position + away;
                // stay inside the habitat
                var a = Home;
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
            if (Def.movement == Movement.Ground && AgentOn)
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
                bool water = WorldLayout.WaterSurface(p.x, p.z, out float wl) && wl - GroundHeight(p.x, p.z) > 0.3f;
                if (!water) { p = transform.position; _velocity = -_velocity * 0.5f; _target = AnchorPos(_anchor); WorldLayout.WaterSurface(p.x, p.z, out wl); }
                p.y = wl - 0.06f;
            }
            else
            {
                // a perched flyer (moon butterfly) startled off its flower flutters up 2.5 m, then settles back
                float g = GroundHeight(p.x, p.z) + (Def.hover > 0 ? Def.hover + (Current == State.Flee && Def.wanderSpeed <= 0f ? 2.5f : 0f) : (Current == State.Flee ? 4f : 0f));
                p.y = Mathf.Lerp(p.y, g, 1 - Mathf.Exp(-4f * Time.deltaTime));
            }
            transform.position = p;
            FaceTowards(_velocity);
        }

        void LateUpdate()
        {
            if (_animator && Current != State.Companion && Current != State.Hidden) DriveAnimator(PlayerController.Instance);
            if (_ears != null && Current != State.Hidden) RabbitPose();
        }

        // ---- rabbit: ears up when alert, head down when grazing (no clips for either; on top of the Animator) -----
        Transform[] _ears;
        Transform _head, _spine;
        float _earsW, _grazeW;
        /// <summary>0..1 how far the ears are pricked up (smoke test / debug).</summary>
        public float EarsUp => _earsW;
        public bool Grazing => Current == State.Wander && _pauseUntil > _stateTime;
        /// <summary>Deer senses this frame (1123c-B1): she is inside its view cone / within hearing.</summary>
        public bool SeesPlayer { get; private set; }
        public bool HearsPlayer { get; private set; }

        void FindRabbitBones()
        {
            if (Def == null || Def.id != "moon_rabbit") return;
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Ear1.") || t.name.StartsWith("Ear2.")) list.Add(t);
                else if (t.name == "Head") _head = t;
                else if (t.name == "Spine") _spine = t;
            }
            if (list.Count > 0 && _head) _ears = list.ToArray();
        }

        void RabbitPose()
        {
            float dt = Time.deltaTime;
            float earsTarget = Current == State.Startled ? 1f : Current == State.Alert ? Mathf.Lerp(0.35f, 0.8f, AlertLevel) : Current == State.Flee ? 0.6f : 0f;
            _earsW = Mathf.MoveTowards(_earsW, earsTarget, dt * (earsTarget > _earsW ? 9f : 1.6f));   // snap up, relax slowly
            _grazeW = Mathf.MoveTowards(_grazeW, Grazing ? 1f : 0f, dt * 2.2f);
            Vector3 right = transform.right;
            if (_grazeW > 0.001f)
            {
                float nibble = Mathf.Sin(Time.time * 9f + instance) * 4f * _grazeW;
                if (_spine) _spine.rotation = Quaternion.AngleAxis(28f * _grazeW, right) * _spine.rotation;
                _head.rotation = Quaternion.AngleAxis(52f * _grazeW + nibble, right) * _head.rotation;
            }
            if (_earsW > 0.001f)
            {
                // both ears straight up, tipped slightly towards her
                Vector3 up = (Vector3.up + transform.forward * 0.18f).normalized;
                foreach (var e in _ears)
                    e.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(e.up, up), 0.9f * _earsW) * e.rotation;
            }
        }

        /// <summary>Rigged models: blend-tree speed from the actual ground speed (playback sped up above the Run
        /// clip's own speed so the paws keep up), and the cat's moods while it keeps the witch company:
        /// she stands still a moment -> sit and lick a paw; ~20 s -> lie down belly-up; she walks up to it -> rub.</summary>
        void DriveAnimator(PlayerController player)
        {
            float dt = Time.deltaTime;
            Vector3 v = AgentOn ? _agent.velocity : _velocity;
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
                    if (AgentOn)
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
            // 1123d-3: resting on the moonflower it opens and closes its wings slowly
            if (Def.id == "moon_butterfly" && Current != State.Companion && Current != State.Flee) _animator.speed = 0.22f;
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
        Vector3 _lookAt; float _lookTime;
        /// <summary>Stop, look at a point for a few seconds and meow once (the cat on a clear night by the lake).</summary>
        public void LookAndMeow(Vector3 at, float seconds = 3f) { _lookAt = at; _lookTime = seconds; _nextMeow = 0.4f; }
        public bool LookingAround => _lookTime > 0f;
        public Vector3 LookTarget => _lookAt;
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
                    if (c == this || (c.Current != State.Wander && c.Current != State.Alert) || c.SpeciesTaken) continue;
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
            if (AgentOn)
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
            if (_playerStill < 0.35f || _rubTime > 0f || !AgentOn)
            {
                if (_hasSpot && AgentOn) _agent.stoppingDistance = 0.8f;
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

        // ---- 试玩反馈 1123b-8: the caught white deer can be ridden ------------------------------------------------
        /// <summary>"按 E 骑上白鹿" / "按 E 下来" while it applies (shown by GameUI like the cabin prompts).</summary>
        public static string RidePrompt { get; private set; }
        public bool Ridden { get; private set; }
        Vector3 _seatLocal;     // the deer's back, in deer space (where her hips go)
        Vector3 _prevRidePos;

        /// <summary>Returns true while she is riding this one (the follow behaviour is skipped).</summary>
        bool UpdateRide(PlayerController player)
        {
            var flow = GameFlow.Instance;
            bool can = flow != null && flow.State == GameFlow.FlowState.Playing && !EnvController.Indoors && !CabinInterior.Busy;
            // pausing, the journal or the map keep her on the deer; the menu, the ending and going indoors do not
            bool keep = flow != null && !EnvController.Indoors && flow.State != GameFlow.FlowState.MainMenu && flow.State != GameFlow.FlowState.Ending;
            bool e = can && GameInput.InteractPressed;
            if (Ridden)
            {
                RidePrompt = can ? "按 E 从白鹿背上下来" : null;
                if (e || !keep) { Dismount(player); return false; }
                // the deer carries her: its back under her hips, facing where she faces
                var rot = player.transform.rotation;
                Vector3 p = player.transform.position - rot * new Vector3(_seatLocal.x, 0f, _seatLocal.z);
                p.y = GroundHeight(p.x, p.z);
                if (Terrain.activeTerrain == null) p.y = player.transform.position.y;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                _velocity = (p - _prevRidePos) / dt;
                _prevRidePos = p;
                transform.SetPositionAndRotation(p, rot);
                return true;
            }
            Vector3 d = transform.position - player.transform.position; d.y = 0;
            bool near = can && !player.Riding && d.magnitude < 2.8f && CaptureSystem.Candidate == null && CabinInterior.Prompt == null;
            if (near) RidePrompt = "按 E 骑上白鹿";
            else if (RidePrompt == "按 E 骑上白鹿") RidePrompt = null;
            if (near && e) Mount(player);
            return false;
        }

        public void Mount(PlayerController player)
        {
            // her hips go just above the deer's spine
            Transform spine = null;
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == "Spine") { spine = t; break; }
            _seatLocal = spine ? transform.InverseTransformPoint(spine.position) : new Vector3(0, 1.1f, 0);
            float seat = Mathf.Max(0.3f, _seatLocal.y + 0.14f - 0.756f);   // 0.756 = her standing hip height
            if (_agent) _agent.enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;   // her capsule would bump into it
            Ridden = true;
            RidePrompt = null;
            _prevRidePos = transform.position;
            // she climbs on: put her where the deer's back is, facing the way it faces
            var p = transform.TransformPoint(new Vector3(_seatLocal.x, 0f, _seatLocal.z));
            p.y = player.transform.position.y;
            player.Place(p);
            player.transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            player.SetMount(transform, seat);
            Debug.Log($"[Ride] mounted {Def.id}, seat {seat:0.00} m");
        }

        public void Dismount(PlayerController player)
        {
            if (!Ridden) return;
            Ridden = false;
            RidePrompt = null;
            player.SetMount(null, 0f);
            // step off to the left side of the deer
            Vector3 off = transform.position - transform.right * 1.1f;
            if (NavMesh.SamplePosition(off, out var hit, 2f, NavMesh.AllAreas)) off = hit.position;
            player.Place(off + Vector3.up * 0.1f);
            Place(transform.position);
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            if (AgentOn) { _agent.speed = 6f; _agent.stoppingDistance = 0.8f; }
            Debug.Log($"[Ride] dismounted {Def.id}");
        }

        // ---- 1123c-B2: only the current pet is out --------------------------------------------------------------
        /// <summary>False while this collected pet is not the current one (kept out of sight, does nothing).</summary>
        public bool PetShown { get; private set; } = true;
        TrailRenderer _trail;
        KoiCompanion _koiPet;
        /// <summary>Set by KoiCompanion so the swim clip follows its motion.</summary>
        internal Vector3 CompanionVelocity { set => _velocity = value; }
        Light _glow;

        public void SetPetShown(bool on)
        {
            if (PetShown == on) return;
            PetShown = on;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!(r is TrailRenderer)) r.enabled = on;
            foreach (var c in GetComponents<Collider>()) c.enabled = on;
            if (_trail) { _trail.Clear(); _trail.emitting = on; }
            if (_glow) _glow.enabled = false;
            if (!on)
            {
                if (Ridden && PlayerController.Instance) Dismount(PlayerController.Instance);
                if (_agent && _agent.enabled) _agent.enabled = false;
                if (RidePrompt != null && Def.rideable) RidePrompt = null;
            }
            else if (Def.movement == Movement.Ground && PlayerController.Instance) Place(PlayerController.Instance.transform.position);
        }

        /// <summary>The moon butterfly as current pet circles her shoulders, trailing purple light; at night it
        /// carries a soft light (1123c-B2).</summary>
        void UpdateButterflyPet(PlayerController player)
        {
            // 1123e-3: no light trail any more; ButterflyGlow scatters stardust and lights it at night only
            if (!_glow)
            {
                var lg = new GameObject("PetGlow", typeof(Light));
                lg.transform.SetParent(transform, false);
                _glow = lg.GetComponent<Light>();
                _glow.type = LightType.Point; _glow.color = new Color(0.72f, 0.62f, 1f); _glow.range = 7f; _glow.intensity = 0f; _glow.shadows = LightShadows.None;
                var bg = GetComponent<ButterflyGlow>(); if (bg) bg.petLight = _glow;
            }
            float t = Time.time * 1.6f;
            Vector3 c = player.transform.position + Vector3.up * (1.5f + 0.25f * Mathf.Sin(t * 1.7f));
            Vector3 orbit = c + new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * 1.15f;
            transform.position = Vector3.SmoothDamp(transform.position, orbit, ref _velocity, 0.18f, 30f);
            if (Vector3.Distance(transform.position, orbit) > 30f) transform.position = orbit;
            FaceTowards(_velocity.sqrMagnitude > 0.01f ? _velocity : player.transform.forward);
        }

        void UpdateCompanion(PlayerController player)
        {
            if (!PetShown) { transform.position = player.transform.position + Vector3.down * 50f; return; }   // stowed away
            if (Def.id == "moon_butterfly") { UpdateButterflyPet(player); return; }
            if (Def.id == "ink_koi")
            {
                // 1123e-2: in a water bottle at her hip on land, in the water near her / under her feet otherwise
                if (!_koiPet) _koiPet = GetComponent<KoiCompanion>() ?? gameObject.AddComponent<KoiCompanion>();
                _koiPet.Drive(player);
                return;
            }
            if (Def.rideable && UpdateRide(player)) return;
            if (_lookTime > 0f)
            {
                // 1123d-3: the cat stops and stares across the lake at the glowing moonflower
                _lookTime -= Time.deltaTime;
                if (AgentOn) _agent.isStopped = true;
                FaceTowards(_lookAt - transform.position);
                _nextMeow -= Time.deltaTime;
                if (_nextMeow <= 0f) { Meowed?.Invoke(transform.position); _nextMeow = 9f; }
                if (_lookTime <= 0f && AgentOn) _agent.isStopped = false;
                return;
            }
            if (Def.id == "cat" && Def.movement == Movement.Ground && UpdateLead(player)) return;
            if (Def.id == "cat" && Def.movement == Movement.Ground && UpdateSideSpot(player)) return;
            // Formation slots behind the player so companions never stack on each other.
            float ang = 140f + CompanionSlot * 22f;
            float side = CompanionSlot % 2 == 0 ? 1 : -1;
            Vector3 offset = Quaternion.Euler(0, side * (ang - 140f) + 180f, 0) * Vector3.forward * (2.2f + CompanionSlot * 0.6f);
            Vector3 slot = player.transform.position + player.transform.rotation * offset;
            if (Def.movement == Movement.Ground && AgentOn)
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
