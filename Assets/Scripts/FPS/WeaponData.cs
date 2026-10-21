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
        public float range;
        public bool isAutomatic;
        public int pelletsPerShot;    // >1 for shotgun
        public float recoilUp;
        public float recoilSide;
        public float aimFOV;          // 0 = no scope
        public float moveSpeedMult;   // 1 = normal

        // Low-poly model (Resources/ArbesGuns/<model>.fbx) + display
        public string model;
        public string shortName;
        public float modelLength = 0.6f; // metres, barrel tip to stock

        public static WeaponData[] GetAllWeapons()
        {
            return new WeaponData[]
            {
                new WeaponData {
                    weaponName = "Pistol", shortName = "Pistol", model = "Pistol", modelLength = 0.24f,
                    damage = 22, fireRate = 4f, reloadTime = 1.5f, magSize = 12, maxReserve = 72,
                    spread = 1.5f, range = 80f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 3f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 1f
                },
                new WeaponData {
                    weaponName = "Revolver", shortName = "Revolv", model = "Revolver", modelLength = 0.3f,
                    damage = 45, fireRate = 2.2f, reloadTime = 2.2f, magSize = 6, maxReserve = 36,
                    spread = 1f, range = 90f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 6f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 1f
                },
                new WeaponData {
                    weaponName = "Ray Gun", shortName = "RayGun", model = "SciFi_RayGun", modelLength = 0.34f,
                    damage = 30, fireRate = 6f, reloadTime = 1.8f, magSize = 15, maxReserve = 90,
                    spread = 1.2f, range = 90f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 2.5f, recoilSide = 0.8f, aimFOV = 0f, moveSpeedMult = 1f
                },
                new WeaponData {
                    weaponName = "Pulse SMG", shortName = "PulseSMG", model = "SciFi_LongPistol", modelLength = 0.4f,
                    damage = 20, fireRate = 13f, reloadTime = 2f, magSize = 30, maxReserve = 150,
                    spread = 2f, range = 90f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 2f, recoilSide = 1f, aimFOV = 0f, moveSpeedMult = 0.95f
                },
                new WeaponData {
                    weaponName = "P90", shortName = "P90", model = "P90", modelLength = 0.48f,
                    damage = 18, fireRate = 15f, reloadTime = 2.2f, magSize = 50, maxReserve = 150,
                    spread = 2.2f, range = 85f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 1.8f, recoilSide = 1.2f, aimFOV = 0f, moveSpeedMult = 0.95f
                },
                new WeaponData {
                    weaponName = "AK-47", shortName = "AK-47", model = "Rifle", modelLength = 0.75f,
                    damage = 32, fireRate = 10f, reloadTime = 2.5f, magSize = 30, maxReserve = 120,
                    spread = 1.8f, range = 140f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 4f, recoilSide = 1.5f, aimFOV = 45f, moveSpeedMult = 0.88f
                },
                new WeaponData {
                    weaponName = "Pulse Rifle", shortName = "PulseRf", model = "SciFi_Rifle", modelLength = 0.7f,
                    damage = 28, fireRate = 11f, reloadTime = 2.3f, magSize = 30, maxReserve = 120,
                    spread = 1.2f, range = 150f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 3f, recoilSide = 0.8f, aimFOV = 45f, moveSpeedMult = 0.9f
                },
                new WeaponData {
                    weaponName = "M870 Shotgun", shortName = "M870", model = "Shotgun", modelLength = 0.8f,
                    damage = 12, fireRate = 1.2f, reloadTime = 0.5f, magSize = 6, maxReserve = 30,
                    spread = 6f, range = 40f, isAutomatic = false, pelletsPerShot = 8,
                    recoilUp = 6f, recoilSide = 2f, aimFOV = 0f, moveSpeedMult = 0.9f
                },
                new WeaponData {
                    weaponName = "Lightning Gun", shortName = "Lightng", model = "SciFi_LightningGun", modelLength = 0.7f,
                    damage = 25, fireRate = 14f, reloadTime = 4f, magSize = 100, maxReserve = 300,
                    spread = 3f, range = 130f, isAutomatic = true, pelletsPerShot = 1,
                    recoilUp = 3.5f, recoilSide = 2f, aimFOV = 0f, moveSpeedMult = 0.75f
                },
                new WeaponData {
                    weaponName = "AWP Sniper", shortName = "AWP", model = "SniperRifle", modelLength = 1.0f,
                    damage = 95, fireRate = 0.7f, reloadTime = 3f, magSize = 5, maxReserve = 20,
                    spread = 0.2f, range = 300f, isAutomatic = false, pelletsPerShot = 1,
                    recoilUp = 8f, recoilSide = 0.5f, aimFOV = 20f, moveSpeedMult = 0.8f
                },
            };
        }
    }
}
