using System.Collections.Generic;
using UnityEngine;

namespace CoachingPropHunt.Props
{
    /// <summary>
    /// Modular floor root descriptor so Ground Floor (floorIndex=0), First Floor (floorIndex=1),
    /// and future expansion floors (Floor_2, Floor_3 at +3.8m/+4.0m increments) plug in cleanly.
    /// </summary>
    public sealed class CoachingFloorModule : MonoBehaviour
    {
        [SerializeField] private int floorIndex;
        [SerializeField] private string floorLabel = "Floor";
        [SerializeField] private float floorElevationY;
        [SerializeField] private List<Transform> hunterSpawns = new List<Transform>();
        [SerializeField] private List<Transform> propSpawns = new List<Transform>();

        public int FloorIndex => floorIndex;
        public string FloorLabel => floorLabel;
        public float FloorElevationY => floorElevationY;
        public IReadOnlyList<Transform> HunterSpawns => hunterSpawns;
        public IReadOnlyList<Transform> PropSpawns => propSpawns;

        public void Configure(int index, string label, float elevationY, List<Transform> hunters, List<Transform> props)
        {
            floorIndex = index;
            floorLabel = label;
            floorElevationY = elevationY;
            hunterSpawns = hunters ?? new List<Transform>();
            propSpawns = props ?? new List<Transform>();
        }
    }
}
