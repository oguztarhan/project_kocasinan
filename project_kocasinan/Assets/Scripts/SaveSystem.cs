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
        const string K_Vib      = "bj_vibration";
        const string K_Avatar   = "bj_avatar";
        const string K_Name     = "bj_name";

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
    }
}
