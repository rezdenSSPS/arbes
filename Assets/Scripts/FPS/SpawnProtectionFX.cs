using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Shows spawn protection (Health.spawnProtectedUntil): the local player gets a small
    // "SPAWN PROTECTION" badge with a draining bar under the crosshair, everyone else sees a
    // faint translucent shell around the protected player. Added at runtime by Health.
    public class SpawnProtectionFX : MonoBehaviour
    {
        Health health;
        NetworkObject netObj;

        // local HUD
        Canvas canvas;
        RectTransform badge;
        TextMeshProUGUI secs;
        Image bar;

        // remote shell
        GameObject shell;
        Material shellMat;
        static readonly Color ShellColor = new Color(0.92f, 0.95f, 1f, 1f);

        void Start()
        {
            health = GetComponent<Health>();
            netObj = GetComponent<NetworkObject>();
        }

        void Update()
        {
            if (health == null || netObj == null || !netObj.IsSpawned) return;
            float left = health.isDead.Value ? 0f : health.SpawnProtectionLeft;
            bool on = left > 0f;

            if (netObj.IsOwner && !health.IsBot)   // bots are server-owned: the host sees their shell, not a badge
            {
                if (on && canvas == null) BuildBadge();
                if (canvas != null)
                {
                    canvas.enabled = on && !HUDManager.InputBlocked;
                    if (on)
                    {
                        secs.text = left.ToString("0.0");
                        SetBar(bar, left / Health.SPAWN_PROTECTION_SECONDS);
                    }
                }
            }
            else
            {
                if (on && shell == null) BuildShell();
                if (shell != null)
                {
                    shell.SetActive(on);
                    if (on)
                    {
                        // gentle pulse, fading out over the last half second
                        float a = 0.16f + 0.08f * Mathf.Sin(Time.time * 7f);
                        a *= Mathf.Clamp01(left / 0.5f);
                        var c = ShellColor; c.a = a;
                        shellMat.color = c;
                    }
                }
            }
        }

        void BuildBadge()
        {
            canvas = UIKit.Canvas("SpawnProtectionCanvas", 11);
            var root = (RectTransform)canvas.transform;

            badge = Box(root, "SpawnProtection", A(Ground, 0.55f)).rectTransform.At(0.5f, 0.5f, 0, -70, 196, 30);
            Frame(badge, A(Fg, 0.35f), 1f);
            var label = Label(badge, "SPAWN PROTECTION", F.CondBold, 15, Fg, TextAlignmentOptions.MidlineLeft, 0.08f);
            label.rectTransform.Fill(10, 0, 44, 3);
            secs = Label(badge, "3.0", F.Mono, 13, Muted, TextAlignmentOptions.MidlineRight);
            secs.rectTransform.Fill(0, 0, 10, 3);
            bar = Bar(badge, "Bar", A(Fg, 0.12f), A(Fg, 0.7f));
            var brt = bar.transform.parent as RectTransform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(1f, 1f); brt.offsetMax = new Vector2(-1f, 3f);
        }

        void BuildShell()
        {
            var cc = GetComponent<CharacterController>();
            float h = cc ? cc.height : 2f, r = cc ? cc.radius : 0.4f;
            Vector3 centre = cc ? cc.center : new Vector3(0, 1f, 0);

            shell = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shell.name = "SpawnProtectionShell";
            Destroy(shell.GetComponent<Collider>());
            shell.transform.SetParent(transform, false);
            shell.transform.localPosition = centre;
            float w = r * 2f + 0.45f;
            shell.transform.localScale = new Vector3(w, h * 0.5f + 0.12f, w);

            var sh = Shader.Find("Sprites/Default");   // transparent, always in builds
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            shellMat = new Material(sh);
            var mr = shell.GetComponent<MeshRenderer>();
            mr.sharedMaterial = shellMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            if (shellMat != null) Destroy(shellMat);
        }
    }
}
