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

        // BB ballistics (see BBBallistics). Tuned so a level shot stays flat for the first
        // ~60% of `range`, then drops away; long shots take ~1 s, so a moving target can dodge.
        public float muzzleVelocity = 100f; // m/s at the muzzle
        public float bbGravity = 9.81f;     // m/s^2
        public float hopUp = 1.05f;         // hop-up lift at the muzzle, as a fraction of gravity
        public float hopTime = 0.45f;       // seconds until the backspin (and its lift) is spent
        public float drag = 0.012f;         // quadratic air drag k (1/m): dv/dt = -k |v| v

        public static WeaponData[] GetAllWeapons()
        {
            return new WeaponData[]
            {
                new WeaponData {
                    weaponName = "Pistol", shortName = "Pistol", model = "Pistol", modelLength = 0.24f,
                    damage = 22, fireRate = 4f, reloadTime = 1.5f, magSize = 12, maxReserve = 72,
                    spread = 1.5f, range = 45f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 3f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 1f,
                    muzzleVelocity = 80f, hopUp = 1f, hopTime = 0.35f, drag = 0.014f
                },
                new WeaponData {
                    weaponName = "Revolver", shortName = "Revolv", model = "Revolver", modelLength = 0.3f,
                    damage = 45, fireRate = 2.2f, reloadTime = 2.2f, magSize = 6, maxReserve = 36,
                    spread = 1f, range = 50f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 6f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 1f,
                    muzzleVelocity = 90f, hopUp = 1f, hopTime = 0.4f, drag = 0.013f
                },
                new WeaponData {
                    weaponName = "Ray Gun", shortName = "RayGun", model = "SciFi_RayGun", modelLength = 0.34f,
                    damage = 30, fireRate = 6f, reloadTime = 1.8f, magSize = 15, maxReserve = 90,
                    spread = 1.2f, range = 55f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 2.5f, recoilSide = 0.8f, aimFOV = 0f, moveSpeedMult = 1f,
                    muzzleVelocity = 95f, hopUp = 1.05f, hopTime = 0.4f, drag = 0.013f
                },
                new WeaponData {
                    weaponName = "Pulse SMG", shortName = "PulseSMG", model = "SciFi_LongPistol", modelLength = 0.4f,
                    damage = 20, fireRate = 13f, reloadTime = 2f, magSize = 30, maxReserve = 150,
                    spread = 2f, range = 60f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 2f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 0.95f,
                    muzzleVelocity = 100f, hopUp = 1.05f, hopTime = 0.45f, drag = 0.012f
                },
                new WeaponData {
                    weaponName = "P90", shortName = "P90", model = "P90", modelLength = 0.48f,
                    damage = 18, fireRate = 15f, reloadTime = 2.2f, magSize = 50, maxReserve = 150,
                    spread = 2.2f, range = 60f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 1.8f, recoilSide = 1.2f, aimFOV = 0f, moveSpeedMult = 0.95f,
                    muzzleVelocity = 105f, hopUp = 1.05f, hopTime = 0.45f, drag = 0.012f
                },
                new WeaponData {
                    weaponName = "AK-47", shortName = "AK-47", model = "Rifle", modelLength = 0.75f,
                    damage = 32, fireRate = 10f, reloadTime = 2.5f, magSize = 30, maxReserve = 120,
                    spread = 1.8f, range = 80f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 4f, recoilSide = 1.5f, aimFOV = 45f, moveSpeedMult = 0.88f,
                    muzzleVelocity = 120f, hopUp = 1.1f, hopTime = 0.55f, drag = 0.01f
                },
                new WeaponData {
                    weaponName = "Pulse Rifle", shortName = "PulseRf", model = "SciFi_Rifle", modelLength = 0.7f,
                    damage = 28, fireRate = 11f, reloadTime = 2.3f, magSize = 30, maxReserve = 120,
                    spread = 1.2f, range = 85f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 3f, recoilSide = 0.8f, aimFOV = 45f, moveSpeedMult = 0.9f,
                    muzzleVelocity = 125f, hopUp = 1.1f, hopTime = 0.55f, drag = 0.01f
                },
                new WeaponData {
                    weaponName = "M870 Shotgun", shortName = "M870", model = "Shotgun", modelLength = 0.8f,
                    damage = 30, fireRate = 1.2f, reloadTime = 0.5f, magSize = 6, maxReserve = 30,
                    spread = 6f, range = 30f, isAutomatic = false, pelletsPerShot = 3,
                    recoilUp = 6f, recoilSide = 2f, aimFOV = 0f, moveSpeedMult = 0.9f,
                    muzzleVelocity = 85f, hopUp = 0.9f, hopTime = 0.3f, drag = 0.016f
                },
                new WeaponData {
                    weaponName = "Lightning Gun", shortName = "Lightng", model = "SciFi_LightningGun", modelLength = 0.7f,
                    damage = 25, fireRate = 14f, reloadTime = 4f, magSize = 100, maxReserve = 300,
                    spread = 3f, range = 75f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 3.5f, recoilSide = 2f, aimFOV = 0f, moveSpeedMult = 0.75f,
                    muzzleVelocity = 115f, hopUp = 1.1f, hopTime = 0.5f, drag = 0.011f
                },
                new WeaponData {
                    weaponName = "AWP Sniper", shortName = "AWP", model = "SniperRifle", modelLength = 1.0f,
                    damage = 95, fireRate = 0.7f, reloadTime = 3f, magSize = 5, maxReserve = 20,
                    spread = 0.2f, range = 130f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 8f, recoilSide = 0.5f, aimFOV = 20f, moveSpeedMult = 0.8f,
                    muzzleVelocity = 150f, hopUp = 1.15f, hopTime = 0.7f, drag = 0.007f
                },
            };
        }
    }
}
