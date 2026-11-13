using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoachingPropHunt.Props
{
    public enum PropSizeTier
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    [Serializable]
    public sealed class PropDefinition
    {
        public string propId;
        public string displayName;
        public PropSizeTier sizeTier = PropSizeTier.Medium;
        public GameObject prefab;
        public int maxHealth = 70;
        [Range(0.75f, 1.25f)] public float moveSpeedMultiplier = 1.0f;
        public Vector3 boundsSize = Vector3.one * 0.5f;
        public float pivotBottomOffset = 0f;
    }

    /// <summary>
    /// Shared catalog of all 30 morphable prop types. Both real map instances and player/decoy
    /// clones instantiate from the exact same prefab so material/scale parity stays 100% identical.
    /// </summary>
    [CreateAssetMenu(menuName = "CoachingPropHunt/Prop Registry", fileName = "PropRegistry")]
    public sealed class PropRegistrySO : ScriptableObject
    {
        [SerializeField] private List<PropDefinition> entries = new List<PropDefinition>();

        private Dictionary<string, PropDefinition> _lookup;

        public IReadOnlyList<PropDefinition> Entries => entries;

        public void SetEntries(List<PropDefinition> definitions)
        {
            entries = definitions ?? new List<PropDefinition>();
            _lookup = null;
        }

        public PropDefinition GetById(string propId)
        {
            if (string.IsNullOrEmpty(propId)) return null;
            EnsureLookup();
            return _lookup.TryGetValue(propId, out var def) ? def : null;
        }

        public PropDefinition GetByIndex(int index)
        {
            if (entries == null || entries.Count == 0) return null;
            int clamped = ((index % entries.Count) + entries.Count) % entries.Count;
            return entries[clamped];
        }

        public int IndexOf(string propId)
        {
            if (entries == null || string.IsNullOrEmpty(propId)) return -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].propId == propId) return i;
            }
            return -1;
        }

        public PropDefinition GetRandomNonLarge(System.Random rng = null)
        {
            if (entries == null || entries.Count == 0) return null;
            var candidates = new List<PropDefinition>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].sizeTier != PropSizeTier.Large)
                    candidates.Add(entries[i]);
            }
            if (candidates.Count == 0) return entries[0];
            int pick = rng != null ? rng.Next(candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
            return candidates[pick];
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, PropDefinition>(StringComparer.Ordinal);
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var def = entries[i];
                if (def != null && !string.IsNullOrEmpty(def.propId))
                    _lookup[def.propId] = def;
            }
        }
    }
}
