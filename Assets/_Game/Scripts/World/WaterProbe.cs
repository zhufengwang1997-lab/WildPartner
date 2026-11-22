using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// 1123c-C2: the reflection the water shows besides the analytic sky — one realtime reflection probe over the
    /// valley, rendered on demand (not every frame): at start, and again when the time of day moves on by a quarter
    /// of an hour or the weather changes, one cube face per frame (time-sliced). 128 px is plenty for the blurred,
    /// normal-mapped reflection. Chosen over SSR (DECISIONS: URP has no built-in SSR, and screen-space reflections
    /// lose everything off screen at the low camera angles the water is seen from).
    /// </summary>
    public class WaterProbe : MonoBehaviour
    {
        ReflectionProbe _probe;
        float _lastHour = -99f, _lastRain = -1f, _next;
        int _renderId = -1;

        void Start()
        {
            var go = new GameObject("WaterReflectionProbe");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(WorldLayout.Size * 0.5f, WorldLayout.LakeLevel + 6f, WorldLayout.Size * 0.5f);
            _probe = go.AddComponent<ReflectionProbe>();
            _probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            _probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            _probe.resolution = 128;
            _probe.size = new Vector3(WorldLayout.Size + 80f, 120f, WorldLayout.Size + 80f);
            _probe.boxProjection = false;
            _probe.nearClipPlane = 1f; _probe.farClipPlane = 600f;
            _probe.hdr = true;
            _probe.shadowDistance = 0f;
            _probe.importance = 10;
        }

        void Update()
        {
            if (!_probe || Time.unscaledTime < _next) return;
            var env = EnvController.Instance;
            float hour = env ? env.timeOfDay : 12f;
            float rain = Weather.Instance ? Weather.Instance.RainAmount : 0f;
            float dh = Mathf.Abs(Mathf.DeltaAngle(hour * 15f, _lastHour * 15f)) / 15f;
            bool busy = _renderId >= 0 && !_probe.IsFinishedRendering(_renderId);
            if (!busy && (dh > 0.25f || Mathf.Abs(rain - _lastRain) > 0.2f))
            {
                _lastHour = hour; _lastRain = rain;
                _renderId = _probe.RenderProbe();
                _next = Time.unscaledTime + 1f;
            }
        }
    }
}
