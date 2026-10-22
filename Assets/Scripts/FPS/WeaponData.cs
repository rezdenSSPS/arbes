using UnityEngine;

namespace AirsoftFPS
{
    [System.Serializable]
    public class WeaponData
    {
        public string weaponName;
        public int damage;
        public float fireRate;        // rounds per second
        public float reloadTime;
        public int magSize;
        public int maxReserve;
        public float spread;          // degrees
        public float range;           // max BB flight distance (path length, metres)
        public bool isAutomatic;
        public int pelletsPerShot;    // >1 for shotgun
        public float recoilUp;
        public float recoilSide;
        public float aimFOV;          // 0 = no scope
        public float moveSpeedMult;   // 1 = normal

        // Low-poly model (Resources/AirsoftGuns/<model>.fbx) + display
        public string model;
        public string shortName;
        public float modelLength = 0.6f; // metres, barrel tip to stock

        // Firing sound family (Resources/AirsoftAudio/Sfx/shot_<sound>_N): aeg (electric), gbb (gas
        // blowback), co2, shotgun (spring pump), springbolt, sniper (spring bolt-action, loudest).
        public string sound = "aeg";

        // BB ballistics (see BBBallistics). Tuned so a level shot stays flat for the first
        // ~60% of `range`, then drops away; long shots take ~1 s, so a moving target can dodge.
        public float muzzleVelocity = 100f; // m/s at the muzzle
        public float bbGravity = 9.81f;     // m/s^2
        public float hopUp = 1.05f;         // hop-up lift at the muzzle, as a fraction of gravity
        public float hopTime = 0.45f;       // seconds until the backspin (and its lift) is spent
        public float drag = 0.012f;         // quadratic air drag k (1/m): dv/dt = -k |v| v

        // Airsoft replica lineup. Slot order = keys 1-0 = Gun Game progression (sidearms -> SMGs ->
        // shotgun -> rifles -> bolt-action sniper). Muzzle velocities are typical 0.20 g BB ratings.
        // Rough time-to-kill at 100 HP (all hits): GBB 0.6 s, revolver 0.5 s, SMGs/AEGs 0.3-0.4 s,
        // shotgun 1 pump up close, DMR 0.5 s, bolt rifle 0.8 s, sniper 1 hit.
        public static WeaponData[] GetAllWeapons()
        {
            return new WeaponData[]
            {
                // 1. Gas blowback pistol, 15-round gas magazine; snappy blowback, short hop.
                new WeaponData {
                    weaponName = "1911 GBB Pistol", shortName = "1911 GBB", model = "Pistol", modelLength = 0.22f, sound = "gbb",
                    damage = 25, fireRate = 5f, reloadTime = 1.5f, magSize = 15, maxReserve = 75,
                    spread = 1.4f, range = 45f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 3.5f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 1f,
                    muzzleVelocity = 90f, hopUp = 1f, hopTime = 0.35f, drag = 0.014f
                },
                // 2. CO2 revolver, 6 shells; hard hitting but slow double-action pull, shell-by-shell reload.
                new WeaponData {
                    weaponName = "CO2 Revolver", shortName = "Revolver", model = "Revolver", modelLength = 0.3f, sound = "co2",
                    damage = 50, fireRate = 2f, reloadTime = 2.4f, magSize = 6, maxReserve = 36,
                    spread = 0.9f, range = 55f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 4.5f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 1f,
                    muzzleVelocity = 115f, hopUp = 1f, hopTime = 0.4f, drag = 0.013f
                },
                // 3. Compact gas blowback SMG, full auto, 32-round gas stick; very fast but sprays.
                new WeaponData {
                    weaponName = "Uzi GBB SMG", shortName = "Uzi GBB", model = "Uzi", modelLength = 0.36f, sound = "gbb",
                    damage = 17, fireRate = 15f, reloadTime = 1.8f, magSize = 32, maxReserve = 160,
                    spread = 2.6f, range = 45f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 2.2f, recoilSide = 1.3f, aimFOV = 0f, moveSpeedMult = 1f,
                    muzzleVelocity = 95f, hopUp = 1f, hopTime = 0.4f, drag = 0.013f
                },
                // 4. Electric PDW, 68-round mid-cap; smooth AEG, almost no kick.
                new WeaponData {
                    weaponName = "P90 AEG", shortName = "P90 AEG", model = "P90", modelLength = 0.5f, sound = "aeg",
                    damage = 19, fireRate = 13f, reloadTime = 2.2f, magSize = 68, maxReserve = 204,
                    spread = 2f, range = 60f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 1.2f, recoilSide = 0.8f, aimFOV = 0f, moveSpeedMult = 0.97f,
                    muzzleVelocity = 100f, hopUp = 1.05f, hopTime = 0.45f, drag = 0.012f
                },
                // 5. Spring pump shotgun, 3 BBs per pump; a 30-BB shell = 10 shots.
                new WeaponData {
                    weaponName = "M870 Spring Shotgun", shortName = "Shotgun", model = "Shotgun", modelLength = 0.85f, sound = "shotgun",
                    damage = 34, fireRate = 1.25f, reloadTime = 2f, magSize = 10, maxReserve = 40,
                    spread = 4.5f, range = 32f, isAutomatic = false, pelletsPerShot = 3,
                    recoilUp = 5f, recoilSide = 1.5f, aimFOV = 0f, moveSpeedMult = 0.92f,
                    muzzleVelocity = 85f, hopUp = 0.9f, hopTime = 0.3f, drag = 0.016f
                },
                // 6. Electric assault rifle (folding stock AK), 120-round mid-cap, iron sights.
                new WeaponData {
                    weaponName = "AK AEG", shortName = "AK AEG", model = "AK47", modelLength = 0.68f, sound = "aeg",
                    damage = 22, fireRate = 12f, reloadTime = 2.5f, magSize = 120, maxReserve = 240,
                    spread = 1.7f, range = 80f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 1.8f, recoilSide = 1f, aimFOV = 50f, moveSpeedMult = 0.9f,
                    muzzleVelocity = 115f, hopUp = 1.1f, hopTime = 0.55f, drag = 0.01f
                },
                // 7. Support AEG (RPK style), 300-round box mag; long wind-up reload, heavy to carry.
                new WeaponData {
                    weaponName = "RPK Support AEG", shortName = "RPK AEG", model = "AK47", modelLength = 0.88f, sound = "aeg",
                    damage = 18, fireRate = 14f, reloadTime = 4.5f, magSize = 300, maxReserve = 300,
                    spread = 2.6f, range = 85f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 1.5f, recoilSide = 1.2f, aimFOV = 55f, moveSpeedMult = 0.75f,
                    muzzleVelocity = 120f, hopUp = 1.1f, hopTime = 0.55f, drag = 0.01f
                },
                // 8. Designated marksman: semi-only AEG with a tight barrel, 50-round mid-cap.
                new WeaponData {
                    weaponName = "M14 DMR", shortName = "M14 DMR", model = "Rifle", modelLength = 0.9f, sound = "aeg",
                    damage = 40, fireRate = 4f, reloadTime = 2.4f, magSize = 50, maxReserve = 150,
                    spread = 0.6f, range = 105f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 2.5f, recoilSide = 0.6f, aimFOV = 38f, moveSpeedMult = 0.86f,
                    muzzleVelocity = 135f, hopUp = 1.12f, hopTime = 0.6f, drag = 0.009f
                },
                // 9. Spring bolt-action rifle on iron sights, 25-round mag; quick to cycle.
                new WeaponData {
                    weaponName = "Spring Bolt Rifle", shortName = "Bolt Rfl", model = "BoltRifle", modelLength = 0.95f, sound = "springbolt",
                    damage = 55, fireRate = 1.3f, reloadTime = 2.5f, magSize = 25, maxReserve = 75,
                    spread = 0.4f, range = 110f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 4f, recoilSide = 0.5f, aimFOV = 45f, moveSpeedMult = 0.88f,
                    muzzleVelocity = 130f, hopUp = 1.12f, hopTime = 0.6f, drag = 0.008f
                },
                // 10. Scoped spring bolt-action sniper (L96 style), 30-round mag; one hit, slow bolt.
                new WeaponData {
                    weaponName = "L96 Spring Sniper", shortName = "Sniper", model = "SniperRifle", modelLength = 1.0f, sound = "sniper",
                    damage = 100, fireRate = 0.75f, reloadTime = 3f, magSize = 30, maxReserve = 60,
                    spread = 0.15f, range = 130f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 5f, recoilSide = 0.4f, aimFOV = 20f, moveSpeedMult = 0.8f,
                    muzzleVelocity = 150f, hopUp = 1.15f, hopTime = 0.7f, drag = 0.007f
                },
            };
        }
    }
}
