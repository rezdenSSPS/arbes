using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Killcam: when the local player is eliminated, a temporary camera placed just behind
    // where they stood turns smoothly to watch whoever eliminated them (a player, or the
    // AI enemy in Co-op) for ~2 s, then the normal death screen and respawn countdown take
    // over. Who the killer is comes from the server (MatchNet.LastDeath), not from this
    // client. Added at runtime by HUDManager; Settings > Gameplay can turn it off.
    public class KillCam : MonoBehaviour
    {
        public const float Duration = 2f;
        const float TurnTime = 0.7f;        // seconds to swing from the victim's view onto the killer
        const float PullBack = 1.6f;        // metres behind the victim's eyes (away from the killer)
        const float Zoom = 0.72f;           // FOV multiplier once locked on

        public bool Playing => cam != null;
        public float Progress => Playing ? Mathf.Clamp01((Time.unscaledTime - start) / length) : 0f;
        public float TimeLeft => Playing ? Mathf.Max(0f, length - (Time.unscaledTime - start)) : 0f;
        public MatchNet.DeathInfo Info { get; private set; }

        Health health;
        FPSController fps;
        WeaponController weapons;
        Camera playerCam, cam;
        float start, length, fov0;
        Vector3 eye, camGoal;
        Quaternion rot0;
        Vector3 lastLookPoint;
        bool wasDead, handled;
        float deadSince;
        readonly List<Renderer> hidden = new List<Renderer>();

        void Update()
        {
            if (health == null && !FindLocalPlayer()) return;

            bool dead = health.isDead.Value;
            if (!dead)
            {
                if (Playing) Stop();
                wasDead = handled = false;
                return;
            }
            if (!wasDead) { wasDead = true; deadSince = Time.unscaledTime; handled = false; }

            if (!handled)
            {
                // The server's death message and the isDead change can arrive in either order.
                var mn = MatchNet.Instance;
                var info = mn != null ? mn.LastDeath : default;
                bool fresh = info.time > 0f && info.time >= deadSince - 1f;
                if (fresh)
                {
                    handled = true;
                    float fit = Mathf.Min(Duration, health.respawnDelay - 0.8f);   // leave the death screen a moment
                    if (GameSettings.Killcam && info.kind != MatchNet.KillerKind.None && fit >= 0.75f)
                        Begin(info, fit);
                }
                else if (Time.unscaledTime - deadSince > 0.5f) handled = true;   // no killer info: plain death screen
            }

            if (Playing)
            {
                var phase = MatchNet.Instance != null ? MatchNet.Instance.CurrentPhase : MatchNet.Phase.InMatch;
                if (Time.unscaledTime - start >= length || playerCam == null || phase == MatchNet.Phase.Results) Stop();
                else Track();
            }
        }

        bool FindLocalPlayer()
        {
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po == null) return false;
            health = po.GetComponent<Health>();
            fps = po.GetComponent<FPSController>();
            weapons = po.GetComponent<WeaponController>();
            playerCam = fps != null && fps.playerCamera != null ? fps.playerCamera : po.GetComponentInChildren<Camera>(true);
            return health != null && playerCam != null;
        }

        void Begin(MatchNet.DeathInfo info, float seconds)
        {
            Info = info;
            start = Time.unscaledTime;
            length = seconds;

            var t = playerCam.transform;
            eye = t.position;
            rot0 = t.rotation;
            fov0 = playerCam.fieldOfView;
            lastLookPoint = info.killerPos + Vector3.up * 1.4f;
            Vector3 look = LookPoint();

            // Back off a little so the shot frames the killer from over the victim's shoulder.
            Vector3 away = eye - look; away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -t.forward;
            Vector3 back = (away * PullBack + Vector3.up * 0.5f);
            float dist = back.magnitude;
            if (Physics.SphereCast(eye, 0.2f, back / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(health.transform))
                dist = Mathf.Max(0f, hit.distance - 0.05f);
            camGoal = eye + back.normalized * dist;

            var go = new GameObject("KillCam");
            cam = go.AddComponent<Camera>();
            cam.CopyFrom(playerCam);
            cam.depth = playerCam.depth + 1;
            go.transform.SetPositionAndRotation(eye, rot0);
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = GameSettings.PostProcessing;
            urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            playerCam.enabled = false;   // the AudioListener on its GameObject keeps working

            // The first-person gun would float in front of the lens; hide it for the shot.
            hidden.Clear();
            if (weapons != null && weapons.weaponHolder != null)
                foreach (var r in weapons.weaponHolder.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
        }

        Vector3 LookPoint()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SpawnManager != null
                && nm.SpawnManager.SpawnedObjects.TryGetValue(Info.killerObjectId, out var no) && no != null)
            {
                float h = Info.kind == MatchNet.KillerKind.Player ? 1.5f : 1.4f;
                lastLookPoint = no.transform.position + Vector3.up * h;
            }
            return lastLookPoint;
        }

        void Track()
        {
            float e = Time.unscaledTime - start;
            float turn = Mathf.SmoothStep(0f, 1f, e / TurnTime);
            cam.transform.position = Vector3.Lerp(eye, camGoal, turn);
            Vector3 to = LookPoint() - cam.transform.position;
            Quaternion target = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : rot0;
            cam.transform.rotation = Quaternion.Slerp(rot0, target, turn);
            cam.fieldOfView = Mathf.Lerp(fov0, fov0 * Zoom, Mathf.SmoothStep(0f, 1f, (e - 0.2f) / 0.9f));
        }

        void Stop()
        {
            if (cam != null) Destroy(cam.gameObject);
            cam = null;
            if (playerCam != null) playerCam.enabled = true;
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
        }

        void OnDestroy() => Stop();
    }
}
