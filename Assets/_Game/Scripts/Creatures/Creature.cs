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

            if (Current == State.Companion) { UpdateCompanion(player); return; }

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
            if (_stateTime > _nextWander || Arrived(_target))
            {
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
            if (Def.id == "forest_owl")
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

        void FaceTowards(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0004f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-6f * Time.deltaTime));
        }

        void UpdateCompanion(PlayerController player)
        {
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
