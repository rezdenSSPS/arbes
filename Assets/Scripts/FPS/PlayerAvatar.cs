using UnityEngine;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Visual third-person character for a networked player (added at runtime by FPSController).
    // Replaces the capsule "Body" with the customised low-poly character, animates it from
    // movement speed, plays the death animation and shows the current gun in the hand.
    // The local player only sees the shadow of their own character.
    public class PlayerAvatar : MonoBehaviour
    {
        NetworkObject netObj;
        Health health;
        WeaponController weapons;
        AirsoftLook.Avatar avatar;
        GameObject handGun;
        int builtLook = int.MinValue, builtTeam = -1, builtGun = -1;
        float nextCheck;
        Vector3 lastPos;
        float speed;
        float deadTilt;

        void Start()
        {
            netObj = GetComponent<NetworkObject>();
            health = GetComponent<Health>();
            weapons = GetComponent<WeaponController>();
            lastPos = transform.position;

            var body = transform.Find("Body");
            if (body)
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        bool IsMine => netObj != null && netObj.IsOwner;
        ulong Owner => netObj != null ? netObj.OwnerClientId : 0;

        int WantedLook()
        {
            int look = MatchNet.Instance != null ? MatchNet.Instance.LookOf(Owner) : -1;
            if (look < 0 && IsMine && MatchNet.Instance != null) look = MatchNet.Instance.LocalLook;
            if (look < 0) look = AirsoftLook.DefaultFor(Owner);
            return look;
        }

        int WantedTeam()
        {
            var mn = MatchNet.Instance;
            if (mn == null || mn.Mode != MatchNet.GameModeType.TeamDeathmatch) return 0;
            return mn.TeamOf(Owner);
        }

        void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.5f;
                int look = WantedLook(), team = WantedTeam();
                if (look != builtLook || team != builtTeam) Rebuild(look, team);
            }

            // horizontal speed (works for remote players too: position is network-synced)
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 d = transform.position - lastPos; d.y = 0;
            lastPos = transform.position;
            speed = Mathf.Lerp(speed, d.magnitude / dt, dt * 10f);

            bool dead = health != null && health.isDead.Value;
            if (avatar != null)
            {
                AirsoftLook.Animate(avatar, speed, dead);
                // characters without a death clip just tip over
                if (string.IsNullOrEmpty(avatar.death))
                {
                    deadTilt = Mathf.MoveTowards(deadTilt, dead ? 90f : 0f, dt * 300f);
                    avatar.root.transform.localRotation = Quaternion.Euler(-deadTilt, 0, 0);
                }
                if (handGun) handGun.SetActive(!dead);
            }

            int gun = weapons != null && weapons.AmmoReady ? weapons.CurrentSlot - 1 : -1;
            if (gun != builtGun && avatar != null) AttachGun(gun);
        }

        void Rebuild(int look, int team)
        {
            builtLook = look; builtTeam = team; builtGun = -1;
            if (avatar != null && avatar.root) Destroy(avatar.root);
            handGun = null;
            try { avatar = AirsoftLook.BuildAvatar(transform, look, team); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] avatar build failed: " + e.Message); avatar = null; }
            if (avatar == null)
            {
                // fall back to the capsule
                var body = transform.Find("Body");
                if (body) foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                return;
            }
            if (IsMine) AirsoftLook.SetShadowsOnly(avatar.root, true);
        }

        void AttachGun(int idx)
        {
            builtGun = idx;
            if (handGun) Destroy(handGun);
            handGun = null;
            if (idx < 0 || weapons == null || weapons.weapons == null || idx >= weapons.weapons.Length) return;
            if (avatar.rightHand == null) return;
            var w = weapons.weapons[idx];
            AirsoftGuns.Gun g = null;
            try { g = AirsoftGuns.Build(w.model, w.modelLength, null); } catch { }
            if (g == null) return;
            handGun = g.root;
            // point the barrel where the character faces, grip in the right hand
            handGun.transform.SetPositionAndRotation(avatar.rightHand.position, transform.rotation);
            handGun.transform.SetParent(avatar.rightHand, true);
            if (IsMine) AirsoftLook.SetShadowsOnly(handGun, true);
        }

        void OnDestroy()
        {
            if (avatar != null && avatar.root) Destroy(avatar.root);
        }
    }
}
