using System;
using UnityEngine;
using WildPartner.Core;
using WildPartner.Player;

namespace WildPartner.Creatures
{
    /// <summary>
    /// FR-G04 capture: get within range -> prompt -> hold E to fill the bar -> if the creature's alert
    /// meter reaches 1 during the hold it flees and the capture is interrupted -> success makes it a
    /// companion. Sneaking keeps the alert meter low, which is what makes captures succeed.
    /// </summary>
    public class CaptureSystem : MonoBehaviour
    {
        public static Creature Target { get; private set; }
        public static float Progress { get; private set; }
        public static event Action<Creature> Captured;
        public static event Action<Creature> Interrupted;
        public static Creature Candidate { get; private set; }

        Creatures.Creature[] _all;

        void Start() => _all = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        /// <summary>The capture was spoiled but the creature only startled (rabbit): progress lost, it stays.</summary>
        public static event Action<Creature> Startled;
        public static void NotifyStartled(Creature c)
        {
            if (Target != c) return;
            Startled?.Invoke(c);
            Target = null;
            Progress = 0;
        }

        public static void NotifyFled(Creature c)
        {
            if (Target == c)
            {
                Interrupted?.Invoke(c);
                Target = null;
                Progress = 0;
            }
        }

        void Update()
        {
            var player = PlayerController.Instance;
            if (!player || GameFlow.Instance == null || GameFlow.Instance.State != GameFlow.FlowState.Playing)
            {
                Candidate = null; Target = null; Progress = 0;
                return;
            }

            // nearest capturable creature in range
            Candidate = null;
            float best = float.MaxValue;
            foreach (var c in _all)
            {
                if (!c || !c.CanBeCaptured || player.Riding) continue;   // no captures from the deer's back
                Vector3 d = c.transform.position - player.transform.position; d.y = 0;
                float dist = d.magnitude;
                if (dist < c.Def.captureRange && dist < best) { best = dist; Candidate = c; }
            }

            if (Candidate && GameInput.CaptureHeld)
            {
                if (Target != Candidate) { Target = Candidate; Progress = 0; }
                player.Frozen = true;
                Progress += Time.deltaTime / Target.Def.captureSeconds;
                if (Progress >= 1f)
                {
                    var c = Target;
                    Target = null;
                    Progress = 0;
                    player.Frozen = false;
                    Debug.Log($"[Capture] {c.Def.id} captured");
                    Captured?.Invoke(c);
                }
            }
            else
            {
                if (Target) player.Frozen = false;
                Target = null;
                Progress = Mathf.MoveTowards(Progress, 0, Time.deltaTime * 2f);
            }
            if (!Candidate && player.Frozen && Target == null) player.Frozen = false;
        }
    }
}
