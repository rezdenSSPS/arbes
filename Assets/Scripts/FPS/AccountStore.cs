using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AirsoftFPS
{
    // Simple local accounts (username + password) stored on this computer in airsoft_accounts.json.
    // Passwords are salted-SHA256 hashed, never stored in clear. Signing in switches the active
    // PlayerProfile to a per-account file (airsoft_profile_<user>.json); guests keep airsoft_profile.json.
    // This is local only (no server) — a stepping stone to real UGS accounts for the web build later.
    public static class AccountStore
    {
        [Serializable] class Account { public string user; public string salt; public string hash; }
        [Serializable] class AccountDB { public List<Account> accounts = new List<Account>(); }

        const string FileName = "airsoft_accounts.json";
        const string CurrentPref = "Airsoft.Account";

        static AccountDB db;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { db = null; Changed = null; }

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        static AccountDB Db { get { if (db == null) Load(); return db; } }

        static void Load()
        {
            try { if (File.Exists(FilePath)) db = JsonUtility.FromJson<AccountDB>(File.ReadAllText(FilePath)); }
            catch (Exception e) { Debug.LogWarning("[Airsoft] accounts could not be read: " + e.Message); }
            db ??= new AccountDB();
            db.accounts ??= new List<Account>();
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(db, true)); }
            catch (Exception e) { Debug.LogWarning("[Airsoft] accounts could not be saved: " + e.Message); }
        }

        public static string Current
        {
            get => PlayerPrefs.GetString(CurrentPref, "");
            private set { PlayerPrefs.SetString(CurrentPref, value ?? ""); PlayerPrefs.Save(); }
        }

        public static bool IsSignedIn => !string.IsNullOrEmpty(Current);

        // The suffix appended to the profile filename for the active account ("" = guest).
        public static string ProfileSuffix => IsSignedIn ? "_" + Current.ToLowerInvariant() : "";

        public static string Sanitize(string u) => (u ?? "").Trim();

        static string Hash(string pass, string salt)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(salt + ":" + pass)));
        }

        static Account Find(string user)
        {
            foreach (var a in Db.accounts)
                if (string.Equals(a.user, user, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        // All return null on success, or a short error message.
        public static string Create(string user, string pass)
        {
            user = Sanitize(user);
            if (user.Length < 2 || user.Length > 16) return "Username must be 2–16 characters";
            if (string.IsNullOrEmpty(pass) || pass.Length < 3) return "Password must be at least 3 characters";
            if (Find(user) != null) return "That username is taken";
            string salt = Guid.NewGuid().ToString("N");
            Db.accounts.Add(new Account { user = user, salt = salt, hash = Hash(pass, salt) });
            Save();
            SetCurrent(user);
            return null;
        }

        public static string SignIn(string user, string pass)
        {
            user = Sanitize(user);
            var a = Find(user);
            if (a == null) return "No account with that name";
            if (a.hash != Hash(pass, a.salt)) return "Wrong password";
            SetCurrent(user);
            return null;
        }

        public static void SignOut() => SetCurrent("");

        static void SetCurrent(string user)
        {
            Current = user;
            PlayerProfile.Reload();   // switch to that account's profile
            Changed?.Invoke();
        }
    }
}
