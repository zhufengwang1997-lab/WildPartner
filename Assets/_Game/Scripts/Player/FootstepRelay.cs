using UnityEngine;

namespace WildPartner.Player
{
    /// <summary>
    /// Receives the "OnFootstep" animation events baked into the witch's Walk / Run / Sneak clips at the frames
    /// where a foot touches down (AssetSetup.BakeStepEvents) and forwards them to the PlayerController.
    /// A blend tree fires the events of every clip it is mixing, so only the dominant clip counts.
    /// </summary>
    public class FootstepRelay : MonoBehaviour
    {
        public void OnFootstep(AnimationEvent e)
        {
            if (e.animatorClipInfo.weight < 0.5f) return;
            var pc = GetComponentInParent<PlayerController>();
            if (pc) pc.AnimFootstep();
        }
    }
}
