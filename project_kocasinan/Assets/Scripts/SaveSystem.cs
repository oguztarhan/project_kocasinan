using UnityEngine;

namespace BusJam
{
    /// <summary>Tiny PlayerPrefs-backed persistence for coins, level and settings.</summary>
    public static class SaveSystem
    {
        const string K_Coins = "bj_coins";
        const string K_Level = "bj_level";
        const string K_Best  = "bj_best";
        const string K_Sound = "bj_sound";

        public static int Coins
        {
            get => PlayerPrefs.GetInt(K_Coins, 150);
            set { PlayerPrefs.SetInt(K_Coins, Mathf.Max(0, value)); PlayerPrefs.Save(); }
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

        public static void AddCoins(int delta) => Coins = Coins + delta;
        public static bool TrySpend(int cost)
        {
            if (Coins < cost) return false;
            Coins -= cost;
            return true;
        }
    }
}
