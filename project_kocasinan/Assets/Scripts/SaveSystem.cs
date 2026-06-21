using UnityEngine;

namespace BusJam
{
    /// <summary>Tiny PlayerPrefs-backed persistence for coins, level and settings.</summary>
    public static class SaveSystem
    {
        const string K_Coins    = "bj_coins";
        const string K_Diamonds = "bj_diamonds";
        const string K_Level    = "bj_level";
        const string K_Best     = "bj_best";
        const string K_Sound    = "bj_sound";
        const string K_Music    = "bj_music";
        const string K_Lang     = "bj_language"; // 0 = Türkçe, 1 = English
        const string K_Vib      = "bj_vibration";
        const string K_Avatar   = "bj_avatar";
        const string K_Name     = "bj_name";

        // ============================================================================================
        // DEBUG / TESTING — unlock levels so you can play ANY level from Settings → LEVELS.
        // On launch this raises your saved level to DEBUG_UNLOCK_TO_LEVEL (only ever UP, so it never
        // wipes higher progress), which makes the LevelSelect map show 1..N as tappable.
        //   • Set to 100 = levels 1–100 unlocked.    • Set to 0 (the normal/release setting) = no unlock.
        // ============================================================================================
        public const int DEBUG_UNLOCK_TO_LEVEL = 0;   // OFF: normal progression (was 100, which forced every "Next" to jump to 100)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void DebugUnlockLevels()
        {
            // ONE-TIME cleanup: earlier debug builds force-unlocked progress to 100 and PERSISTED it (PlayerPrefs),
            // so even with the unlock off the saved level stays 100. Reset progress back to level 1 exactly once,
            // gated by a flag, so normal progression resumes and this never re-wipes real progress afterwards.
            if (PlayerPrefs.GetInt("bj_progress_reset_v1", 0) == 0)
            {
                PlayerPrefs.SetInt("bj_progress_reset_v1", 1);
                PlayerPrefs.SetInt(K_Level, 1);
                PlayerPrefs.SetInt(K_Best, 1);
                PlayerPrefs.Save();
            }

            if (DEBUG_UNLOCK_TO_LEVEL > 0 && Level < DEBUG_UNLOCK_TO_LEVEL)
            {
                Level = DEBUG_UNLOCK_TO_LEVEL;
                BestLevel = DEBUG_UNLOCK_TO_LEVEL;
            }
        }

        public static int Coins
        {
            get => PlayerPrefs.GetInt(K_Coins, 150);
            set { PlayerPrefs.SetInt(K_Coins, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static int Diamonds
        {
            get => PlayerPrefs.GetInt(K_Diamonds, 0);
            set { PlayerPrefs.SetInt(K_Diamonds, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static int Level
        {
            get => Mathf.Max(1, PlayerPrefs.GetInt(K_Level, 1));
            set { PlayerPrefs.SetInt(K_Level, Mathf.Max(1, value)); PlayerPrefs.Save(); }
        }

        public static int BestLevel
        {
            get => Mathf.Max(1, PlayerPrefs.GetInt(K_Best, 1));
            set { PlayerPrefs.SetInt(K_Best, Mathf.Max(BestLevel, value)); PlayerPrefs.Save(); }
        }

        public static bool Sound
        {
            get => PlayerPrefs.GetInt(K_Sound, 1) == 1;
            set { PlayerPrefs.SetInt(K_Sound, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool Music
        {
            get => PlayerPrefs.GetInt(K_Music, 1) == 1;
            set { PlayerPrefs.SetInt(K_Music, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // Selected language (0 = Türkçe, 1 = English). Stored only for now; hooking it up
        // to actual text translation is a separate (localization) task.
        public static int Language
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(K_Lang, 0));
            set { PlayerPrefs.SetInt(K_Lang, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(K_Vib, 1) == 1;
            set { PlayerPrefs.SetInt(K_Vib, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // Selected preset-avatar index and player name (Profile panel).
        public static int AvatarIndex
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(K_Avatar, 0));
            set { PlayerPrefs.SetInt(K_Avatar, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static string PlayerName
        {
            get => PlayerPrefs.GetString(K_Name, "Player");
            set { PlayerPrefs.SetString(K_Name, string.IsNullOrWhiteSpace(value) ? "Player" : value); PlayerPrefs.Save(); }
        }

        public static void AddCoins(int delta) => Coins = Coins + delta;
        public static void AddDiamonds(int delta) => Diamonds = Diamonds + delta;
        public static bool TrySpend(int cost)
        {
            if (Coins < cost) return false;
            Coins -= cost;
            return true;
        }

        // ---- Free joker charges (0 = Recolor, 1 = Swap, 2 = Heli), granted by daily
        //      rewards. A joker consumes a free charge before spending gold.
        static string FreeJokerKey(int kind) =>
            kind == 0 ? "bj_freeRecolor" : kind == 1 ? "bj_freeSwap" : "bj_freeHeli";

        public static int FreeJoker(int kind) => Mathf.Max(0, PlayerPrefs.GetInt(FreeJokerKey(kind), 0));

        public static void AddFreeJoker(int kind, int count)
        {
            PlayerPrefs.SetInt(FreeJokerKey(kind), Mathf.Max(0, FreeJoker(kind) + count));
            PlayerPrefs.Save();
        }

        public static bool TryUseFreeJoker(int kind)
        {
            int n = FreeJoker(kind);
            if (n <= 0) return false;
            PlayerPrefs.SetInt(FreeJokerKey(kind), n - 1);
            PlayerPrefs.Save();
            return true;
        }

        // (#6) One-time "mandatory" free joker: granted the first time RECOLOR unlocks (level 5).
        public static bool FreeJokerGranted
        {
            get => PlayerPrefs.GetInt("bj_freejoker_granted", 0) == 1;
            set { PlayerPrefs.SetInt("bj_freejoker_granted", value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
