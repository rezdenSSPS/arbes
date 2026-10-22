using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Procedural animation for the humanoid enemy: a walk cycle driven by the
    // NavMeshAgent's speed, a small aim pose while attacking, and a death topple.
    public class EnemyAnimator : MonoBehaviour
    {
        public Transform model;     // visual root (toppled on death)
        public Transform hipL, hipR, shoulderL, shoulderR;
        public Health health;

        NavMeshAgent agent;
        float walkPhase;
        bool dead;
        Quaternion modelRest;
        AirsoftLook.Avatar avatar;   // low-poly soldier (replaces the primitive body when available)

        // PvP bot (Health.IsBot): the bot's own random look with team armbands, the gun it is using,
        // and the Milsim HIT pose -- like PlayerAvatar does for players.
        bool isBot;
        EnemyAI ai;
        GameObject handGun;
        Vector3 gunMuzzleLocal;
        int builtLook = int.MinValue, builtTeam = -1, builtGun = -2;
        float nextLookCheck, botSpeed, deadTilt, handUp;
        Vector3 lastPos;

        // Where the gun's barrel ends (for the visual BB), or a point in front of the chest.
        public Vector3 MuzzlePosition => handGun != null
            ? handGun.transform.TransformPoint(gunMuzzleLocal)
            : transform.position + Vector3.up * 1.4f + transform.forward * 0.6f;

        void Start()
        {
            agent = GetComponent<NavMeshAgent>();
            if (health == null) health = GetComponent<Health>();
            if (model) modelRest = model.localRotation;

            if (health != null)
            {
                health.isDead.OnValueChanged += OnDeadChanged;
                dead = health.isDead.Value;
            }

            isBot = health != null && health.IsBot;
            if (isBot)
            {
                ai = GetComponent<EnemyAI>();
                lastPos = transform.position;
                // players have no floating health bar; neither do bots
                var bar = GetComponentInChildren<WorldHealthBar>(true);
                if (bar != null) bar.gameObject.SetActive(false);
                if (model) foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                return;
            }

            BuildLowPoly();
        }

        ulong BotId => health != null ? health.botId.Value : 0;

        void UpdateBot()
        {
            var mn = MatchNet.Instance;
            if (Time.time >= nextLookCheck)
            {
                nextLookCheck = Time.time + 0.5f;
                int look = mn != null ? mn.LookOf(BotId) : -1;
                if (look < 0) look = AirsoftLook.DefaultFor(BotId);
                int team = mn != null && mn.TeamMode ? mn.TeamOf(BotId) : 0;
                if (look != builtLook || team != builtTeam) RebuildBot(look, team);
            }

            // speed from movement (the agent only runs on the server)
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 d = transform.position - lastPos; d.y = 0;
            lastPos = transform.position;
            botSpeed = Mathf.Lerp(botSpeed, d.magnitude / dt, dt * 10f);

            if (avatar == null) return;
            AirsoftLook.Animate(avatar, botSpeed, dead);
            if (string.IsNullOrEmpty(avatar.death))
            {
                deadTilt = Mathf.MoveTowards(deadTilt, dead ? 90f : 0f, dt * 300f);
                avatar.root.transform.localRotation = Quaternion.Euler(-deadTilt, 0, 0);
            }
            int gun = ai != null && ai.IsSpawned ? ai.botWeapon.Value : -1;
            if (gun != builtGun) AttachBotGun(gun);
            if (handGun) handGun.SetActive(!dead);
        }

        void RebuildBot(int look, int team)
        {
            builtLook = look; builtTeam = team; builtGun = -2;
            if (avatar != null && avatar.root) Destroy(avatar.root);
            handGun = null;
            try { avatar = AirsoftLook.BuildAvatar(transform, look, team); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] bot avatar: " + e.Message); avatar = null; }
            if (avatar == null && model) foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }

        void AttachBotGun(int idx)
        {
            builtGun = idx;
            if (handGun) Destroy(handGun);
            handGun = null;
            if (avatar == null || avatar.rightHand == null) return;
            var all = WeaponData.GetAllWeapons();
            var w = idx >= 0 && idx < all.Length ? all[idx] : null;
            AirsoftGuns.Gun g = null;
            try { g = w != null ? AirsoftGuns.Build(w.model, w.modelLength, null) : AirsoftGuns.Build("AK47", 0.68f, null); } catch { }
            if (g == null) return;
            handGun = g.root;
            gunMuzzleLocal = g.muzzleLocal;
            handGun.transform.SetPositionAndRotation(avatar.rightHand.position, transform.rotation);
            handGun.transform.SetParent(avatar.rightHand, true);
        }

        // Milsim: a HIT bot raises its hand (after the animation has posed the bones this frame).
        void LateUpdate()
        {
            if (!isBot) return;
            bool hit = health != null && health.IsSpawned && health.IsOut;
            handUp = Mathf.MoveTowards(handUp, hit ? 1f : 0f, Time.deltaTime * 5f);
            if (avatar != null) AirsoftLook.RaiseHand(avatar, handUp);
        }

        void BuildLowPoly()
        {
            var no = GetComponent<NetworkObject>();
            int seed = no != null ? (int)no.NetworkObjectId : Random.Range(0, 9999);
            var rng = new System.Random(seed * 31 + 7);
            var look = new LookData
            {
                body = rng.Next(4),                 // male bodies
                hat = 3,                            // helmet
                hatColor = rng.Next(2) == 0 ? 9 : 6,
                shirt = rng.Next(3) switch { 0 => 6, 1 => 9, _ => 8 },
                pants = rng.Next(2) == 0 ? 6 : 10,
                skin = rng.Next(AirsoftLook.SkinTones.Length),
                hair = rng.Next(3)
            };
            try { avatar = AirsoftLook.BuildAvatar(transform, AirsoftLook.Pack(look)); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] enemy avatar: " + e.Message); avatar = null; }
            if (avatar == null) return;

            if (model) foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            if (avatar.rightHand)
            {
                AirsoftGuns.Gun g = null;
                try { g = AirsoftGuns.Build("AK47", 0.68f, null); } catch { }
                if (g != null)
                {
                    g.root.transform.SetPositionAndRotation(avatar.rightHand.position, transform.rotation);
                    g.root.transform.SetParent(avatar.rightHand, true);
                }
            }
        }

        void OnDeadChanged(bool prev, bool curr) => dead = curr;

        void Update()
        {
            if (isBot) { UpdateBot(); return; }
            if (avatar != null)
            {
                float sp = (agent != null && agent.enabled && agent.isOnNavMesh) ? agent.velocity.magnitude : 0f;
                // enemies move at ~3.5 m/s: map that to the run clip
                AirsoftLook.Animate(avatar, sp > 2.5f ? 7f : sp, dead);
                return;
            }

            if (model == null) return;

            if (dead)
            {
                // Topple forward and sink slightly
                model.localRotation = Quaternion.Slerp(model.localRotation,
                    modelRest * Quaternion.Euler(88f, 0f, 0f), Time.deltaTime * 5f);
                return;
            }

            float speed = (agent != null && agent.enabled && agent.isOnNavMesh)
                ? agent.velocity.magnitude : 0f;

            float swing = 0f;
            if (speed > 0.15f)
            {
                walkPhase += Time.deltaTime * (6f + speed);
                swing = Mathf.Sin(walkPhase) * Mathf.Clamp(speed * 10f, 10f, 40f);
            }
            else
            {
                walkPhase = Mathf.Lerp(walkPhase, 0f, Time.deltaTime * 6f);
                swing = Mathf.Sin(walkPhase) * 2f;
            }

            if (hipL) hipL.localRotation = Quaternion.Euler(swing, 0, 0);
            if (hipR) hipR.localRotation = Quaternion.Euler(-swing, 0, 0);
            // Left arm swings with the walk; right arm stays forward holding the gun
            if (shoulderL) shoulderL.localRotation = Quaternion.Euler(-swing * 0.6f, 0, 0);
            if (shoulderR) shoulderR.localRotation = Quaternion.Euler(-75f, 0, 0);
        }

        void OnDestroy()
        {
            if (health != null) health.isDead.OnValueChanged -= OnDeadChanged;
        }
    }
}
