using UnityEngine;

namespace WildPartner.Player
{
    /// <summary>
    /// Procedural animation for rigid (un-rigged) Tripo models — PRD 4.2 fallback. Animates the "Visual"
    /// child only: bob / hop / lean / sway / wing-flap / swim-wiggle driven by the owner's speed.
    /// Rigid transforms still produce correct per-object motion vectors (URP writes them from the
    /// previous object matrix), so no extra work is needed for IN-03.
    /// </summary>
    public class ProceduralAnimator : MonoBehaviour
    {
        public enum Style { Walker, Hopper, Quadruped, Flyer, Swimmer, Perched }

        public Style style = Style.Walker;
        public Transform visual;
        public float scale = 1f;           // amplitude multiplier (model size)
        public float stepLength = 0.9f;    // metres per step cycle

        Vector3 _basePos;
        Quaternion _baseRot;
        Vector3 _baseScale;
        Vector3 _lastPos;
        float _phase, _speed, _idleT;
        float _seed;

        /// <summary>Optional external speed source (player). Otherwise derived from position change.</summary>
        public System.Func<float> speedSource;
        /// <summary>0..1 fear / excitement, used by creatures to shiver when alert.</summary>
        public float alert;

        void Start()
        {
            if (!visual) visual = transform.Find("Visual");
            if (!visual) { enabled = false; return; }
            _basePos = visual.localPosition;
            _baseRot = visual.localRotation;
            _baseScale = visual.localScale;
            _lastPos = transform.position;
            _seed = Random.value * 10f;
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float measured = Vector3.ProjectOnPlane(transform.position - _lastPos, Vector3.up).magnitude / dt;
            _lastPos = transform.position;
            float target = speedSource != null ? speedSource() : measured;
            _speed = Mathf.Lerp(_speed, target, 1 - Mathf.Exp(-12f * dt));
            _phase += _speed / Mathf.Max(stepLength, 0.05f) * dt;
            _idleT += dt;

            float s = scale;
            Vector3 pos = _basePos;
            Vector3 euler = Vector3.zero;
            Vector3 scl = _baseScale;
            float moving = Mathf.Clamp01(_speed / 1.5f);
            float breathe = Mathf.Sin((_idleT + _seed) * 2.2f);

            switch (style)
            {
                case Style.Walker:
                {
                    float c = _phase * Mathf.PI * 2;
                    pos.y += Mathf.Abs(Mathf.Sin(c)) * 0.06f * s * moving;
                    euler.x = Mathf.Clamp(_speed * 1.4f, 0, 12);                    // lean into the run
                    euler.z = Mathf.Sin(c) * 3.5f * moving;                         // hip sway
                    euler.y = Mathf.Sin(c) * 4f * moving;
                    scl = _baseScale * (1 + breathe * 0.008f * (1 - moving));
                    break;
                }
                case Style.Hopper:
                {
                    float c = Mathf.Repeat(_phase, 1f);
                    float hop = Mathf.Sin(c * Mathf.PI);
                    pos.y += hop * 0.22f * s * moving;
                    euler.x = (c < 0.5f ? -15 : 12) * moving;
                    float squash = 1 - 0.12f * moving * (1 - hop) + 0.03f * breathe * (1 - moving);
                    scl = new Vector3(_baseScale.x * (2 - squash), _baseScale.y * squash, _baseScale.z * (2 - squash));
                    if (moving < 0.1f) euler.y = Mathf.Sin((_idleT + _seed) * 0.7f) * 8f;  // looks around
                    break;
                }
                case Style.Quadruped:
                {
                    float c = _phase * Mathf.PI * 2;
                    pos.y += Mathf.Abs(Mathf.Sin(c)) * 0.08f * s * moving;
                    euler.x = Mathf.Sin(c * 2) * 3f * moving;                       // pitch rock
                    euler.z = Mathf.Sin(c) * 2f * moving;
                    if (moving < 0.1f) { euler.x = 6f + Mathf.Sin(_idleT * 0.4f + _seed) * 6f; }   // grazing nod
                    scl = _baseScale * (1 + breathe * 0.01f * (1 - moving));
                    break;
                }
                case Style.Flyer:
                {
                    float c = (_idleT + _seed) * (6f + _speed * 2f);
                    pos.y += Mathf.Sin((_idleT + _seed) * 1.7f) * 0.25f * s + Mathf.Sin(c) * 0.05f * s;
                    float flap = Mathf.Sin(c) * 0.5f + 0.5f;
                    scl = new Vector3(_baseScale.x * Mathf.Lerp(0.35f, 1.05f, flap), _baseScale.y, _baseScale.z);
                    euler.z = Mathf.Sin((_idleT + _seed) * 1.1f) * 10f;
                    euler.x = Mathf.Clamp(_speed * 3f, 0, 20);
                    break;
                }
                case Style.Swimmer:
                {
                    float c = (_idleT + _seed) * (3f + _speed * 3f);
                    euler.y = Mathf.Sin(c) * 14f;
                    euler.z = Mathf.Sin(c * 0.5f) * 6f;
                    pos.y += Mathf.Sin((_idleT + _seed) * 0.9f) * 0.05f * s;
                    break;
                }
                case Style.Perched:
                {
                    // Owl: head-bob idle, flap when moving.
                    float c = (_idleT + _seed) * 9f;
                    if (moving > 0.2f)
                    {
                        pos.y += 0.4f * s + Mathf.Sin(c) * 0.08f * s;
                        scl = new Vector3(_baseScale.x * (1 + 0.25f * Mathf.Sin(c)), _baseScale.y, _baseScale.z);
                        euler.x = 15;
                    }
                    else
                    {
                        euler.y = Mathf.Round(Mathf.Sin((_idleT + _seed) * 0.5f) * 2f) * 35f;  // snappy head turns
                        scl = _baseScale * (1 + breathe * 0.015f);
                    }
                    break;
                }
            }

            // alert shiver
            if (alert > 0.01f) euler.z += Mathf.Sin(_idleT * 40f) * 2f * alert;

            visual.localPosition = pos;
            visual.localRotation = _baseRot * Quaternion.Euler(euler);
            visual.localScale = scl;
        }
    }
}
