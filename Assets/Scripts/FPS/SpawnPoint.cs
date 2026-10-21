using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // A player spawn point in the arena. In team modes (TDM, Milsim) each team only uses its own side
    // (Red = north half, Blue = south half); the other modes use every point.
    // PlayerSpawner picks between them.
    public class SpawnPoint : MonoBehaviour
    {
        [Tooltip("0 = anyone, 1 = Red, 2 = Blue (Team Deathmatch side)")]
        public int team;

        public static readonly List<SpawnPoint> All = new List<SpawnPoint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = team == 1 ? new Color(0.94f, 0.35f, 0.31f) : team == 2 ? new Color(0.49f, 0.72f, 1f) : Color.white;
            Gizmos.DrawWireSphere(transform.position, 0.6f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
        }
    }
}
