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

        // Selected language index (0 Türkçe … 8 Bahasa Indonesia, see Loc). Auto-detected from the device region on
        // the FIRST launch (see AutoDetectLanguage); after that the saved value — including the in-game language menu
        // choice — always wins.
        public static int Language
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(K_Lang, 0));
            set { PlayerPrefs.SetInt(K_Lang, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        // First launch only (no language saved yet): pick the language from the DEVICE REGION so a Turkish phone opens
        // in Turkish, a German phone in German, etc. Maps the device language to one of the 9 supported, English for
        // anything else. Runs before the first scene so the menu opens already localized; the saved choice wins after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoDetectLanguage()
        {
            if (PlayerPrefs.HasKey(K_Lang)) return; // already chosen by the player or a previous launch — respect it
            PlayerPrefs.SetInt(K_Lang, SystemLanguageIndex());
            PlayerPrefs.Save();
        }

        static int SystemLanguageIndex()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Turkish:            return 0;
                case SystemLanguage.English:            return 1;
                case SystemLanguage.German:             return 2;
                case SystemLanguage.Italian:            return 3;
                case SystemLanguage.Spanish:            return 4;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return 5;
                case SystemLanguage.French:             return 6;
                case SystemLanguage.Portuguese:         return 7;
                case SystemLanguage.Indonesian:         return 8;
                default:                                return 1; // English
            }
        }

        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(K_Vib, 1) == 1;
            set { PlayerPrefs.SetInt(K_Vib, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // Player's push-notification opt-in. FirebaseManager subscribes/unsubscribes when this flips; the remote
        // kill-switch GameConfig.NotificationsEnabled is ANDed on top.
        public static bool NotificationsEnabled
        {
            get => PlayerPrefs.GetInt("bj_notifications", 1) == 1;
            set { PlayerPrefs.SetInt("bj_notifications", value ? 1 : 0); PlayerPrefs.Save(); }
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

        // ===================== COSMETIC SKINS / GACHA ECONOMY =====================
        // Owned skins = a comma-separated list of ids in ONE key (bounded, <1KB). Classic is implicitly owned, so
        // a fresh save needs no migration: OwnsSkin(Classic)==true and the equipped value self-heals to Classic.
        const string K_SkinsOwned   = "bj_skins_owned";
        const string K_SkinEquipVeh = "bj_skin_veh";
        const string K_Shards       = "bj_shards";

        public static string SkinsOwnedRaw
        {
            get => PlayerPrefs.GetString(K_SkinsOwned, "");
            set { PlayerPrefs.SetString(K_SkinsOwned, value ?? ""); PlayerPrefs.Save(); }
        }

        public static bool OwnsSkin(string id)
        {
            if (string.IsNullOrEmpty(id) || id == SkinCatalog.DefaultVehicleId) return true; // Classic always owned
            return ("," + SkinsOwnedRaw + ",").Contains("," + id + ",");
        }

        public static void AddOwnedSkin(string id)
        {
            if (string.IsNullOrEmpty(id) || OwnsSkin(id)) return;
            string raw = SkinsOwnedRaw;
            SkinsOwnedRaw = string.IsNullOrEmpty(raw) ? id : raw + "," + id;
        }

        // Equipped vehicle skin; self-heals a blank/unowned value back to Classic.
        public static string EquippedVehicleSkin
        {
            get
            {
                string id = PlayerPrefs.GetString(K_SkinEquipVeh, SkinCatalog.DefaultVehicleId);
                return OwnsSkin(id) ? id : SkinCatalog.DefaultVehicleId;
            }
            set { PlayerPrefs.SetString(K_SkinEquipVeh, string.IsNullOrEmpty(value) ? SkinCatalog.DefaultVehicleId : value); PlayerPrefs.Save(); }
        }

        // Crafting currency (duplicate pulls melt into these).
        public static int Shards
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(K_Shards, 0));
            set { PlayerPrefs.SetInt(K_Shards, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }
        public static void AddShards(int delta) => Shards = Shards + delta;
        public static bool TrySpendShards(int cost)
        {
            if (Shards < cost) return false;
            Shards -= cost;
            return true;
        }

        // Per-chest-tier pity counter (opens since the last Rare-or-better).
        public static int Pity(string tier) => Mathf.Max(0, PlayerPrefs.GetInt("bj_pity_" + tier, 0));
        public static void SetPity(string tier, int v) { PlayerPrefs.SetInt("bj_pity_" + tier, Mathf.Max(0, v)); PlayerPrefs.Save(); }

        // Chest keys per tier: a rare bonus drop from chests; one key opens that tier's chest for free.
        public static int Keys(string tier) => Mathf.Max(0, PlayerPrefs.GetInt("bj_key_" + tier, 0));
        public static void AddKeys(string tier, int n = 1) { PlayerPrefs.SetInt("bj_key_" + tier, Keys(tier) + Mathf.Max(0, n)); PlayerPrefs.Save(); }
        public static bool TrySpendKey(string tier)
        {
            int k = Keys(tier);
            if (k <= 0) return false;
            PlayerPrefs.SetInt("bj_key_" + tier, k - 1); PlayerPrefs.Save();
            return true;
        }

        // ===================== VEHICLE CARS (the "dolap" / wardrobe) =====================
        // A "set" is really ONE collectible car (+ the shared Connect/Bus). Cars are WON FROM CHESTS (OwnsSet =
        // collected); equip is PER TYPE and INDEPENDENT. Owned cars = a CSV of ids; the free starter is implicitly
        // owned, so a fresh save needs no migration. DefaultSetId MUST match the catalog's set 0 id (the builder
        // makes set 0 = Firenze -> "set_firenze"); change both together if set 0 changes.
        public const string DefaultSetId     = "set_firenze"; // free starter CAR (catalog set 0)
        public const string DefaultMinivanId = "mv_classic";  // free "Classic" minivan (Connect)
        public const string DefaultBusId     = "bus_classic"; // free "Classic" bus
        public static string DefaultFor(VehicleType t) =>
            t == VehicleType.Car ? DefaultSetId : t == VehicleType.Minivan ? DefaultMinivanId : DefaultBusId;
        const string K_SetsOwned    = "bj_sets_owned";
        const string K_SetEquipCar  = "bj_set_car";
        const string K_SetEquipMini = "bj_set_mini";
        const string K_SetEquipBus  = "bj_set_bus";

        public static string SetsOwnedRaw
        {
            get => PlayerPrefs.GetString(K_SetsOwned, "");
            set { PlayerPrefs.SetString(K_SetsOwned, value ?? ""); PlayerPrefs.Save(); }
        }

        public static bool DebugOwnAll = false; // TEST flag: true = own EVERY car (then CRAFT/chests have nothing to give — every tier shows "0 left" and the buttons grey out). MUST stay false for the chest/shard/craft economy to work AND for release.
        public static bool OwnsSet(string id)
        {
            if (DebugOwnAll) return true; // test mode — everything unlocked
            // the free starter of EACH type (car / minivan "Classic" / bus "Classic") is always owned
            if (string.IsNullOrEmpty(id) || id == DefaultSetId || id == DefaultMinivanId || id == DefaultBusId) return true;
            return ("," + SetsOwnedRaw + ",").Contains("," + id + ",");
        }

        public static void AddOwnedSet(string id)
        {
            if (string.IsNullOrEmpty(id) || OwnsSet(id)) return;
            string raw = SetsOwnedRaw;
            SetsOwnedRaw = string.IsNullOrEmpty(raw) ? id : raw + "," + id;
        }

        static string EquipKey(VehicleType t) =>
            t == VehicleType.Car ? K_SetEquipCar : (t == VehicleType.Minivan ? K_SetEquipMini : K_SetEquipBus);

        // The set id equipped for a given vehicle type; self-heals a blank/unowned value to the free default.
        public static string EquippedSet(VehicleType t)
        {
            string id = PlayerPrefs.GetString(EquipKey(t), DefaultFor(t));
            return OwnsSet(id) ? id : DefaultFor(t);
        }

        public static void SetEquippedSet(VehicleType t, string id)
        {
            PlayerPrefs.SetString(EquipKey(t), string.IsNullOrEmpty(id) ? DefaultSetId : id);
            PlayerPrefs.Save();
        }

        // Free-chest cooldown gate (epoch seconds; stored as a string to hold a long).
        public static long FreeChestReadyAt
        {
            get => long.TryParse(PlayerPrefs.GetString("bj_freechest_at", "0"), out long t) ? t : 0L;
            set { PlayerPrefs.SetString("bj_freechest_at", value.ToString()); PlayerPrefs.Save(); }
        }
    }
}
