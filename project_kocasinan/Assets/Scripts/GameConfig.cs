using System.Collections.Generic;

namespace BusJam
{
    /// <summary>
    /// Central, remotely-tunable game values. The fields below are the SHIPPED defaults; <see cref="FirebaseManager"/>
    /// overrides them from Firebase Remote Config on launch, so you can change the economy / feature flags from the
    /// Firebase console without an app update. If Firebase is unavailable, the defaults are used — the game always runs.
    ///
    /// To add a remote value: add a field, add it to <see cref="Defaults"/> (the key + default), and read it in
    /// <see cref="ApplyRemote"/>. Then reference GameConfig.&lt;field&gt; wherever the old constant was used.
    /// </summary>
    public static class GameConfig
    {
        // --- jokers / economy ---
        public static int RecolorCost = 75, SwapCost = 50, HeliCost = 100, SlotUnlockCost = 75, ContinueBaseCost = 150;
        public static int Joker1Unlock = 5, Joker2Unlock = 10, Joker3Unlock = 15;
        public static int LevelReward = 25, BonusReward = 50;

        // --- chests / crafting ---
        public static int ChestBronze = 100, ChestSilver = 250, ChestGold = 600;
        public static int CraftT0 = 50, CraftT1 = 125, CraftT2 = 400, CraftT3 = 1000; // craft cost by car tier (0..3)
        public static long FreeChestCooldownSec = 8 * 3600;

        // --- flags ---
        public static bool NotificationsEnabled = true;   // GLOBAL remote kill-switch (ANDed with the player's own toggle)
        public static bool FeatureDiagonals = true, FeatureMystery = true, FeatureBonusLevels = true;

        // Key -> current value. Seeds Remote Config's in-app defaults so GetValue() never returns 0/"" for a known key.
        public static Dictionary<string, object> Defaults() => new Dictionary<string, object>
        {
            { "recolor_cost", RecolorCost }, { "swap_cost", SwapCost }, { "heli_cost", HeliCost },
            { "slot_unlock_cost", SlotUnlockCost }, { "continue_base_cost", ContinueBaseCost },
            { "joker1_unlock", Joker1Unlock }, { "joker2_unlock", Joker2Unlock }, { "joker3_unlock", Joker3Unlock },
            { "level_reward", LevelReward }, { "bonus_reward", BonusReward },
            { "chest_bronze_cost", ChestBronze }, { "chest_silver_cost", ChestSilver }, { "chest_gold_cost", ChestGold },
            { "craft_t0", CraftT0 }, { "craft_t1", CraftT1 }, { "craft_t2", CraftT2 }, { "craft_t3", CraftT3 },
            { "free_chest_cooldown_sec", FreeChestCooldownSec },
            { "notifications_enabled", NotificationsEnabled },
            { "feature_diagonals", FeatureDiagonals }, { "feature_mystery", FeatureMystery }, { "feature_bonus_levels", FeatureBonusLevels },
        };

        // Pull every value from Remote Config (FirebaseManager supplies the typed getters).
        public static void ApplyRemote(System.Func<string, long> L, System.Func<string, bool> B)
        {
            RecolorCost = (int)L("recolor_cost"); SwapCost = (int)L("swap_cost"); HeliCost = (int)L("heli_cost");
            SlotUnlockCost = (int)L("slot_unlock_cost"); ContinueBaseCost = (int)L("continue_base_cost");
            Joker1Unlock = (int)L("joker1_unlock"); Joker2Unlock = (int)L("joker2_unlock"); Joker3Unlock = (int)L("joker3_unlock");
            LevelReward = (int)L("level_reward"); BonusReward = (int)L("bonus_reward");
            ChestBronze = (int)L("chest_bronze_cost"); ChestSilver = (int)L("chest_silver_cost"); ChestGold = (int)L("chest_gold_cost");
            CraftT0 = (int)L("craft_t0"); CraftT1 = (int)L("craft_t1"); CraftT2 = (int)L("craft_t2"); CraftT3 = (int)L("craft_t3");
            FreeChestCooldownSec = L("free_chest_cooldown_sec");
            NotificationsEnabled = B("notifications_enabled");
            FeatureDiagonals = B("feature_diagonals"); FeatureMystery = B("feature_mystery"); FeatureBonusLevels = B("feature_bonus_levels");
        }
    }
}
