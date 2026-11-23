using UnityEngine;

namespace WildPartner.Player
{
    /// <summary>
    /// Dynamic hair and skirt on top of the animation (试玩反馈 1123b-4). The clips still carry their keyed
    /// follow-through; this adds what keyframes cannot know about: how fast she is really moving, jumps, falls,
    /// landings and turns.
    ///
    /// Each chain (two 3-bone hair strands, four 1-bone skirt panels) is a string of point masses. Every point is
    /// pulled by a damped spring towards where the animation puts it (relative to its simulated parent), and pushed
    /// by air drag against its absolute velocity. That one drag term gives all the requested behaviour:
    ///   running -> drag points backwards, hair and hem trail behind, more the faster she runs;
    ///   rising in a jump -> drag points down, everything is pressed down;
    ///   falling -> drag points up, hair and skirt float up, more the longer she falls (speed keeps growing);
    ///   landing -> the body stops, the points keep their downward momentum: a whip down, then the under-damped
    ///   spring (damping ratio ~0.25) bounces 2-3 times and settles;
    ///   turning -> the points lag behind the body's rotation.
    /// A small side force in step with the stride adds the left-right sway of running, scaled by speed.
    /// Stability: fixed 1/120 s sub-steps, position-based (lengths kept exactly, velocity from positions), angle
    /// limits, and half-space limits so the skirt never folds into the legs and the hair never into the back.
    /// Teleports (PlayerController.CameraCut) reset the chains to the animated pose.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SecondaryMotion : MonoBehaviour
    {
        [Header("Hair")] public float hairStiffness = 34f, hairDamping = 3.2f, hairDrag = 1.0f, hairDragVertical = 0.5f, hairMaxAngle = 80f, hairTipLength = 0.14f;
        [Header("Skirt")] public float skirtStiffness = 60f, skirtDamping = 3.6f, skirtDrag = 1.9f, skirtDragVertical = 0.6f, skirtMaxAngle = 50f, skirtLength = 0.28f;
        [Header("Running sway")] public float swayForce = 0.6f;
        [Tooltip("Share of a sudden vertical speed change (take-off, landing) the chains keep as lag; the rest they follow at once")]
        public float verticalInertia = 0.25f;

        class Chain
        {
            public Transform[] bones;
            public float tipLength;
            public bool skirt, skirtBack;   // skirtBack: the panel MotionDemo reads (cached: Transform.name allocates a string)
            public float k, c, drag, dragV, maxAngle;
            public Vector3[] p, v, prevRest;
            public Quaternion[] bind;
            public float[] len;
        }

        Chain[] _chains;
        public float DebugHairLift { get; private set; }
        public float DebugSkirtLift { get; private set; }
        Transform _hips;
        PlayerController _pc;
        Vector3 _prevRoot, _prevRootVel;
        bool _reset = true;
        const float Step = 1f / 120f;

        void Awake()
        {
            _pc = GetComponentInParent<PlayerController>();
            Transform Find(string n) { foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == n) return t; return null; }
            _hips = Find("Hips");
            var list = new System.Collections.Generic.List<Chain>();
            foreach (var s in new[] { "L", "R" })
            {
                var a = Find("HairA." + s); var b = Find("HairB." + s); var c = Find("HairC." + s);
                if (a && b && c) list.Add(new Chain { bones = new[] { a, b, c }, tipLength = hairTipLength, k = hairStiffness, c = hairDamping, drag = hairDrag, dragV = hairDragVertical, maxAngle = hairMaxAngle });
            }
            foreach (var s in new[] { "F", "B", "L", "R" })
            {
                var t = Find("Skirt." + s);
                if (t) list.Add(new Chain { bones = new[] { t }, tipLength = skirtLength, skirt = true, skirtBack = s == "B", k = skirtStiffness, c = skirtDamping, drag = skirtDrag, dragV = skirtDragVertical, maxAngle = skirtMaxAngle });
            }
            _chains = list.ToArray();
            foreach (var ch in _chains)
            {
                int n = ch.bones.Length;
                ch.p = new Vector3[n + 1]; ch.v = new Vector3[n + 1]; ch.prevRest = new Vector3[n + 1]; ch.len = new float[n + 1];
                ch.bind = new Quaternion[n];
                for (int i = 0; i < n; i++) ch.bind[i] = ch.bones[i].localRotation;   // before the Animator's first pose: the bind pose
            }
            if (_chains.Length == 0) enabled = false;
            PlayerController.CameraCut += ResetChains;
        }

        void OnDestroy() => PlayerController.CameraCut -= ResetChains;
        void ResetChains() => _reset = true;

        /// <summary>Animated (rest) points of a chain: bone heads, then the tip along the last bone (local +Y).</summary>
        static void RestPoints(Chain ch, Vector3[] r)
        {
            int n = ch.bones.Length;
            for (int i = 0; i < n; i++) r[i] = ch.bones[i].position;
            r[n] = ch.bones[n - 1].position + ch.bones[n - 1].up * ch.tipLength;
        }

        readonly Vector3[] _rest = new Vector3[8], _restSub = new Vector3[8];

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            var root = transform;
            // the clips also key hair/skirt follow-through (rig_humanoid.py); with this layer on top it counted
            // twice (hair lifted 30° on the way up a jump instead of pressed down), so the chains start from their
            // bind pose under the animated head and hips and this layer does all of their motion
            foreach (var ch in _chains) for (int i = 0; i < ch.bones.Length; i++) ch.bones[i].localRotation = ch.bind[i];

            if (_reset)
            {
                foreach (var ch in _chains)
                {
                    RestPoints(ch, _rest);
                    for (int i = 0; i <= ch.bones.Length; i++)
                    {
                        ch.p[i] = ch.prevRest[i] = _rest[i]; ch.v[i] = Vector3.zero;
                        if (i > 0) ch.len[i] = Vector3.Distance(_rest[i], _rest[i - 1]);
                    }
                }
                _prevRoot = root.position; _prevRootVel = Vector3.zero;
                _reset = false;
                return;
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / Step), 1, 8);
            float h = dt / steps;
            Vector3 rootVel = (root.position - _prevRoot) / dt;
            _prevRoot = root.position;
            // take-off / landing change the body's vertical speed within a frame; letting the chains feel all of it
            // made the spring overshoot so far that hair flew up already at the top of a small hop
            float dVy = rootVel.y - _prevRootVel.y;
            _prevRootVel = rootVel;
            if (Mathf.Abs(dVy) > 1.5f)
                foreach (var ch in _chains) for (int i = 1; i < ch.v.Length; i++) ch.v[i].y += dVy * (1f - verticalInertia);
            Vector3 fwd = root.forward, right = root.right;
            float speed = _pc ? _pc.Speed : new Vector2(rootVel.x, rootVel.z).magnitude;
            // stride sway: one left-right cycle per two steps, only when moving on the ground
            float swaySign = _pc ? Mathf.Sin(_pc.StepPhase * Mathf.PI) : 0f;
            bool grounded = !_pc || !_pc.Airborne;
            Vector3 sway = right * (swaySign * swayForce * Mathf.Clamp01(speed / 4f) * speed * (grounded ? 1f : 0f));
            // in the air hair and skirt fall with her (same gravity); without this they lagged behind her fall and
            // floated to the limit on every small hop. On the ground the animated hanging pose stands for gravity.
            Vector3 gravity = !grounded && _pc ? Vector3.up * _pc.gravity : Vector3.zero;

            foreach (var ch in _chains)
            {
                int n = ch.bones.Length;
                RestPoints(ch, _rest);
                Vector3 outward = Vector3.zero;
                if (ch.skirt && _hips)
                {
                    outward = ch.bones[0].position - _hips.position; outward.y = 0;
                    outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : fwd;
                }
                for (int s = 1; s <= steps; s++)
                {
                    float f = s / (float)steps;
                    for (int i = 0; i <= n; i++) _restSub[i] = Vector3.Lerp(ch.prevRest[i], _rest[i], f);
                    ch.p[0] = _restSub[0];   // the chain root is driven by the skeleton
                    for (int i = 1; i <= n; i++)
                    {
                        Vector3 parent = ch.p[i - 1];
                        Vector3 target = parent + (_restSub[i] - _restSub[i - 1]);
                        Vector3 old = ch.p[i];
                        // air drag: weaker vertically, so a small hop lifts a little and only a long fall (high speed)
                        // floats hair and skirt right up; horizontally it gives the trailing of a run
                        Vector3 vel = ch.v[i];
                        Vector3 drag = new Vector3(vel.x * ch.drag, vel.y * ch.dragV, vel.z * ch.drag);
                        Vector3 a = ch.k * (target - old) - ch.c * (ch.v[i] - rootVel) - drag + sway + gravity;
                        ch.v[i] += a * h;
                        Vector3 np = old + ch.v[i] * h;

                        // keep the segment length, limit the bend away from the animated direction
                        Vector3 restDir = (target - parent).normalized;
                        Vector3 dir = np - parent;
                        if (dir.sqrMagnitude < 1e-8f) dir = restDir;
                        dir.Normalize();
                        float ang = Vector3.Angle(restDir, dir);
                        if (ang > ch.maxAngle) dir = Vector3.Slerp(restDir, dir, ch.maxAngle / ang);
                        np = parent + dir * ch.len[i];

                        // no folding into the body: skirt panels only move outwards of their animated place, hair
                        // never further forward than its animated place (into her back)
                        Vector3 disp = np - target;
                        if (ch.skirt)
                        {
                            float inward = Vector3.Dot(disp, -outward);
                            if (inward > 0.01f) np += outward * (inward - 0.01f);
                        }
                        else
                        {
                            float forward = Vector3.Dot(disp, fwd);
                            if (forward > 0.02f) np -= fwd * (forward - 0.02f);
                        }
                        np = parent + (np - parent).normalized * ch.len[i];

                        ch.v[i] = (np - old) / h;
                        if (ch.v[i].sqrMagnitude > 400f) ch.v[i] = ch.v[i].normalized * 20f;
                        ch.p[i] = np;
                    }
                }
                for (int i = 0; i <= n; i++) ch.prevRest[i] = _rest[i];
                // debug readout (MotionDemo): how far the simulated tip sits above (+) / below (-) the animated one
                if (ch == _chains[0]) DebugHairLift = ch.p[n].y - _rest[n].y;
                else if (ch.skirtBack) DebugSkirtLift = ch.p[n].y - _rest[n].y;

                // pose the bones along the simulated points, root to tip
                for (int i = 0; i < n; i++)
                {
                    var b = ch.bones[i];
                    Vector3 animTip = i < n - 1 ? ch.bones[i + 1].position : b.position + b.up * ch.tipLength;
                    Vector3 from = animTip - b.position, to = ch.p[i + 1] - b.position;
                    if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                        b.rotation = Quaternion.FromToRotation(from, to) * b.rotation;
                }
            }
        }
    }
}
