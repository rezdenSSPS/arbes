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

        public static WeaponData[] GetAllWeapons()
        {
            return new WeaponData[]
            {
                new WeaponData {
                    weaponName = "M1911", damage = 22, fireRate = 4f, reloadTime = 1.5f,
                    magSize = 7, maxReserve = 56, spread = 1.5f, range = 80f,
                    isAutomatic = false, pelletsPerShot = 1, recoilUp = 3f, recoilSide = 1f,
                    aimFOV = 0, moveSpeedMult = 1f
                },
                new WeaponData {
                    weaponName = "Glock 18", damage = 18, fireRate = 12f, reloadTime = 1.4f,
                    magSize = 20, maxReserve = 100, spread = 2.5f, range = 60f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 2.5f, recoilSide = 1.5f,
                    aimFOV = 0, moveSpeedMult = 1f
                },
                new WeaponData {
                    weaponName = "MP5", damage = 20, fireRate = 13f, reloadTime = 2f,
                    magSize = 30, maxReserve = 150, spread = 2f, range = 90f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 2f, recoilSide = 1f,
                    aimFOV = 0, moveSpeedMult = 0.95f
                },
                new WeaponData {
                    weaponName = "P90", damage = 18, fireRate = 15f, reloadTime = 2.2f,
                    magSize = 50, maxReserve = 150, spread = 2.2f, range = 85f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 1.8f, recoilSide = 1.2f,
                    aimFOV = 0, moveSpeedMult = 0.95f
                },
                new WeaponData {
                    weaponName = "M4A1", damage = 28, fireRate = 11f, reloadTime = 2.3f,
                    magSize = 30, maxReserve = 120, spread = 1.2f, range = 150f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 3f, recoilSide = 0.8f,
                    aimFOV = 45f, moveSpeedMult = 0.9f
                },
                new WeaponData {
                    weaponName = "AK-47", damage = 32, fireRate = 10f, reloadTime = 2.5f,
                    magSize = 30, maxReserve = 120, spread = 1.8f, range = 140f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 4f, recoilSide = 1.5f,
                    aimFOV = 45f, moveSpeedMult = 0.88f
                },
                new WeaponData {
                    weaponName = "SCAR-H", damage = 35, fireRate = 9f, reloadTime = 2.6f,
                    magSize = 20, maxReserve = 80, spread = 1f, range = 160f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 4.5f, recoilSide = 1f,
                    aimFOV = 40f, moveSpeedMult = 0.85f
                },
                new WeaponData {
                    weaponName = "M870", damage = 12, fireRate = 1.2f, reloadTime = 0.5f,
                    magSize = 6, maxReserve = 30, spread = 6f, range = 40f,
                    isAutomatic = false, pelletsPerShot = 8, recoilUp = 6f, recoilSide = 2f,
                    aimFOV = 0, moveSpeedMult = 0.9f
                },
                new WeaponData {
                    weaponName = "AWP", damage = 95, fireRate = 0.7f, reloadTime = 3f,
                    magSize = 5, maxReserve = 20, spread = 0.2f, range = 300f,
                    isAutomatic = false, pelletsPerShot = 1, recoilUp = 8f, recoilSide = 0.5f,
                    aimFOV = 20f, moveSpeedMult = 0.8f
                },
                new WeaponData {
                    weaponName = "M249", damage = 25, fireRate = 14f, reloadTime = 4f,
                    magSize = 100, maxReserve = 300, spread = 3f, range = 130f,
                    isAutomatic = true, pelletsPerShot = 1, recoilUp = 3.5f, recoilSide = 2f,
                    aimFOV = 0, moveSpeedMult = 0.75f
                }
            };
        }
    }
}
