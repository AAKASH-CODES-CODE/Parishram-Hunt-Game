using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoachingPropHunt.Networking.SocketIO
{
    [Serializable]
    public class PlayerStateData
    {
        public string sid;
        public string player_name;
        public string preferred_role; // "Hunter", "Prop", "Random"
        public string assigned_role;  // "Hunter", "Prop"
        public bool is_host;
        public bool is_ready;
        public int ping_ms;
        public int health = 100;
    }

    [Serializable]
    public class RoomStateEnvelope
    {
        public bool success;
        public string event_type;
        public string notification;
        public double timestamp;
        public RoomStateData room;
    }

    [Serializable]
    public class RoomCreatedData
    {
        public bool success;
        public string room_code;
        public string message;
    }

    [Serializable]
    public class RoomStateData
    {
        public string room_code;
        public string room_name;
        public string host_id;
        public string status; // "waiting", "in_game", "finished"
        public int max_players;
        public bool is_private;
        public int match_seed;
        public List<PlayerStateData> players = new List<PlayerStateData>();
        public string notification;
    }

    [Serializable]
    public class ChatMessageData
    {
        public string room_code;
        public string sender_sid;
        public string sender_name;
        public string sender_role;
        public bool is_host;
        public string message;
        public double timestamp;
    }

    [Serializable]
    public class OpenRoomSummaryData
    {
        public string room_code;
        public string room_name;
        public int player_count;
        public int max_players;
    }

    [Serializable]
    public class OpenRoomsListWrapper
    {
        public List<OpenRoomSummaryData> rooms = new List<OpenRoomSummaryData>();
    }

    [Serializable]
    public class MatchStartedData
    {
        public bool success;
        public string room_code;
        public int match_seed;
        public int hunters_count;
        public int props_count;
    }

    [Serializable]
    public class PlayerDamagedData
    {
        public string room_code;
        public string target_sid;
        public string target_name;
        public string attacker_sid;
        public string attacker_name;
        public int damage;
        public int remaining_hp;
        public bool is_fatal;
        public float[] muzzle_pos;
        public float[] impact_pos;

        public Vector3 GetMuzzlePos() => muzzle_pos != null && muzzle_pos.Length >= 3 ? new Vector3(muzzle_pos[0], muzzle_pos[1], muzzle_pos[2]) : Vector3.zero;
        public Vector3 GetImpactPos() => impact_pos != null && impact_pos.Length >= 3 ? new Vector3(impact_pos[0], impact_pos[1], impact_pos[2]) : Vector3.zero;
    }

    [Serializable]
    public class KillCamTriggerData
    {
        public string room_code;
        public string attacker_sid;
        public string attacker_name;
        public string victim_sid;
        public string victim_name;
        public float[] muzzle_pos;
        public float[] impact_pos;

        public Vector3 GetMuzzlePos() => muzzle_pos != null && muzzle_pos.Length >= 3 ? new Vector3(muzzle_pos[0], muzzle_pos[1], muzzle_pos[2]) : Vector3.zero;
        public Vector3 GetImpactPos() => impact_pos != null && impact_pos.Length >= 3 ? new Vector3(impact_pos[0], impact_pos[1], impact_pos[2]) : Vector3.zero;
    }

    [Serializable]
    public class KickedData
    {
        public string message;
    }

    [Serializable]
    public class ErrorResponseData
    {
        public bool success;
        public string message;
    }
}
