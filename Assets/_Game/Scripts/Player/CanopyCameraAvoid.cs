using Unity.Cinemachine;
using UnityEngine;
using WildPartner.World;

namespace WildPartner.Player
{
    /// <summary>
    /// 1123c-B4 leftover: while she climbs (and for a moment after, or while she stands high up, e.g. the tower
    /// top) the orbit camera often ends up inside a nearby tree crown. Crowns have no colliders, so the
    /// CinemachineDeoccluder misses them. This extension tests the line from the look-at point to the camera
    /// against the crown boxes (VegetationSpawner.TreeCrowns) and pulls the camera in front of the first crown,
    /// quickly in, slowly back out. On the ground it does nothing (walking under trees keeps the normal framing).
    /// </summary>
    [AddComponentMenu("")]
    public class CanopyCameraAvoid : CinemachineExtension
    {
        public PlayerController player;
        /// <summary>Not closer than this: the near-camera dither (EnvController.NearFade, up to 2.4 m) would fade her.</summary>
        // minDistance: not closer, the near-camera dither (EnvController.NearFade, up to 2.4 m) would fade her
        public float minDistance = 2.6f, margin = 0.35f, searchRadius = 30f, pullInTime = 0.12f, releaseTime = 0.6f;
        public float highAboveGround = 3f, holdAfterClimb = 2f;

        float _dist = -1f, _activeUntil;
        /// <summary>For the smoke test: was the camera inside a crown before / after the correction last frame.</summary>
        public static bool LastRawInCrown, LastFinalInCrown;
        public static bool Active { get; private set; }

        bool WantActive()
        {
            if (!player) return false;
            if (player.Climbing || player.Mantling) { _activeUntil = Time.time + holdAfterClimb; return true; }
            if (Time.time < _activeUntil) return true;
            var t = Terrain.activeTerrain;
            if (!t) return false;
            var p = player.transform.position;
            return p.y - (t.SampleHeight(p) + t.transform.position.y) > highAboveGround;
        }

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body) return;
            Active = WantActive();
            if (!Active || !state.HasLookAt()) { _dist = -1f; LastRawInCrown = LastFinalInCrown = false; return; }

            Vector3 target = state.ReferenceLookAt, cam = state.GetCorrectedPosition();
            Vector3 d = cam - target;
            float full = d.magnitude;
            if (full < 1e-3f) return;
            var ray = new Ray(target, d / full);

            // nearest crown entry along the line (crowns the look-at point is already inside do not count)
            float limit = full;
            bool rawIn = false;
            var crowns = VegetationSpawner.TreeCrowns;
            float r2 = searchRadius * searchRadius;
            for (int i = 0; i < crowns.Count; i++)
            {
                var c = crowns[i];
                if ((c.center - target).sqrMagnitude > r2) continue;
                if (c.Contains(cam)) rawIn = true;
                // she is under / in this crown too (branches overhang the wall): no clear line, just come in close
                if (c.Contains(target)) { if (c.Contains(cam)) limit = Mathf.Min(limit, minDistance + margin); continue; }
                if (c.IntersectRay(ray, out float enter) && enter < limit) limit = enter;
            }
            LastRawInCrown = rawIn;
            float want = limit < full ? Mathf.Max(minDistance, limit - margin) : full;

            if (_dist < 0f || deltaTime < 0f) _dist = want;   // first frame / camera cut
            else
            {
                float k = want < _dist ? pullInTime : releaseTime;
                _dist = Mathf.Lerp(_dist, want, 1f - Mathf.Exp(-deltaTime / Mathf.Max(k, 1e-3f)));
            }
            float use = Mathf.Min(_dist, full);
            if (use < full - 1e-3f) state.PositionCorrection += ray.direction * (use - full);

            var fin = state.GetCorrectedPosition();
            bool finIn = false;
            for (int i = 0; i < crowns.Count && !finIn; i++)
            {
                var c = crowns[i];
                if ((c.center - target).sqrMagnitude <= r2 && c.Contains(fin) && (!c.Contains(target) || (fin - target).sqrMagnitude > (minDistance + margin + 0.05f) * (minDistance + margin + 0.05f))) finIn = true;
            }
            LastFinalInCrown = finIn;
        }
    }
}
