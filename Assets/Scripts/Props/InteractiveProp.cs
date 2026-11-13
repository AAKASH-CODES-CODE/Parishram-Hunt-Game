using UnityEngine;

namespace CoachingPropHunt.Props
{
    /// <summary>
    /// Attached to every morphable prop on the Coaching map and every spawned decoy clone.
    /// Carries metadata without altering shaders, outlines, or materials.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractiveProp : MonoBehaviour
    {
        [SerializeField] private string propId;
        [SerializeField] private string roomTag;
        [SerializeField] private bool isDecoyClone;
        [SerializeField] private float decoyRemainingSeconds;
        [SerializeField] private int decoyNetId;

        public string PropId => propId;
        public string RoomTag => roomTag;
        public bool IsDecoyClone => isDecoyClone;
        public int DecoyNetId => decoyNetId;

        public void Configure(string id, string room = "Map", bool decoy = false, float ttlSeconds = 0f, int netId = 0)
        {
            propId = id;
            roomTag = room;
            isDecoyClone = decoy;
            decoyRemainingSeconds = ttlSeconds;
            decoyNetId = netId;
        }

        private void Update()
        {
            if (!isDecoyClone || decoyRemainingSeconds <= 0f) return;
            decoyRemainingSeconds -= Time.deltaTime;
            if (decoyRemainingSeconds <= 0f)
            {
                if (CoachingPropHunt.Core.ObjectPoolManager.Instance != null)
                    CoachingPropHunt.Core.ObjectPoolManager.Instance.ReleaseDecoy(gameObject);
                else
                    Destroy(gameObject);
            }
        }
    }
}
