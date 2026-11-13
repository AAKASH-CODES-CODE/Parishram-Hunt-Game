using UnityEngine;

namespace CoachingPropHunt.Core
{
    /// <summary>
    /// Converts Unity world positions back into exact Parishram Coaching Campus
    /// floor & room labels using the building partition coordinates.
    /// </summary>
    public static class CoachingRoomLocator
    {
        public static string GetRoomLabel(Vector3 unityPos)
        {
            // Importer maps Blender (bx, by, bz) -> Unity (-bx, bz, -by)
            float bx = -unityPos.x;
            float by = -unityPos.z;
            bool isFirstFloor = unityPos.y >= 3.2f;

            if (!isFirstFloor)
            {
                if (by < -13.2f) return "GF • MAIN GATE";
                if (bx <= -3.3f && by >= 4.2f) return "GF • OFFICE ROOM";
                if (bx <= -2.8f && by >= -1.8f && by < 4.2f) return "GF • STAIRCASE";
                if (bx >= 3.2f) return "GF • CYCLE STAND";
                if (by < -8.0f) return "GF • FRONT LOBBY";
                return "GF • COACHING HALL";
            }

            if (bx <= -0.3f && by >= 4.2f) return "1F • BIG CLASSROOM";
            if (bx > -0.3f && by >= 9.2f) return "1F • CLASSROOM 2";
            if (bx >= 3.3f && by >= 3.6f && by < 9.2f) return "1F • SELF STUDY";
            if (bx >= 1.0f && by <= -1.6f && by > -7.0f) return "1F • STAFF ROOM";
            if (bx <= -4.2f && by <= -1.6f && by >= -6.8f) return "1F • KITCHEN";
            if (bx <= -4.0f && by < -6.8f) return "1F • RECORDS ROOM";
            if (bx > -4.0f && by <= -7.0f) return "1F • DOUBT CLASS";
            return "1F • CORRIDOR";
        }
    }
}
