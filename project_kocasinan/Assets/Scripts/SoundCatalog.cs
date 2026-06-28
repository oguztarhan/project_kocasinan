using UnityEngine;
using UnityEngine.Serialization;

namespace BusJam
{
    /// <summary>
    /// All game audio in one editable asset (Resources/SoundCatalog.asset). Assign clips + tune volumes here.
    ///
    /// SFX: each sound has a clip slot and its OWN volume slider. An empty clip falls back to the built-in
    /// procedural sound (except the looping engine, which is silent until you assign one).
    ///
    /// MUSIC: the menu track plus one track PER THEME (night themes get a night track, etc.). The per-theme list
    /// auto-lists every theme with a sensible default already assigned — replace or revolume any of them freely.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "BusJam/Sound Catalog")]
    public class SoundCatalog : ScriptableObject
    {
        // One in-game music track for a theme. `theme` is an auto-filled label that matches a Theme name.
        [System.Serializable]
        public class ThemeMusic
        {
            public string theme;                               // label only (auto-synced to the Theme name)
            [Tooltip("Music that loops while you play levels of this theme.")]
            public AudioClip music;
            [Range(0f, 1f)] public float volume = 0.5f;
        }

        [Header("GAMEPLAY — vehicles & passengers")]

        [Tooltip("THE engine/move sound. LOOPS continuously while ANY vehicle is moving (slide out, crawl, drive away, helicopter) and stops the instant they all stop. " +
                 "NO built-in — SILENT until you add a clip. Use a SEAMLESS engine LOOP (steady hum) — the bus_vroom_* files loop cleanly.")]
        [FormerlySerializedAs("deploy")]
        public AudioClip vehicleSlidesOutOfJam;
        [Range(0f, 1f)] public float vehicleSlidesOutVolume = 0.35f;

        [Tooltip("Plays once when a vehicle pulls into its parking stop. Look for: a single short car horn 'honk' (~0.3s).")]
        [FormerlySerializedAs("honk")]
        public AudioClip vehicleArrivesAtStop;
        [Range(0f, 1f)] public float vehicleArrivesAtStopVolume = 1f;

        [Tooltip("Plays when a FULL bus drifts away off-screen (also layered onto a crash). Look for: a tyre screech / skid (~0.5-0.8s).")]
        [FormerlySerializedAs("screech")]
        public AudioClip fullBusDrivesAway;
        [Range(0f, 1f)] public float fullBusDrivesAwayVolume = 0.55f;

        [Tooltip("Plays when you tap a BLOCKED vehicle, and on a bonus-level collision. Look for: a metallic bump / crunch (~0.4s).")]
        [FormerlySerializedAs("crash")]
        public AudioClip vehicleBlockedCrash;
        [Range(0f, 1f)] public float vehicleBlockedCrashVolume = 1f;

        [Tooltip("Plays when a passenger from the queue takes a seat on a bus. Look for: a soft 'pop' / 'ding' (~0.1s).")]
        [FormerlySerializedAs("board")]
        public AudioClip passengerBoardsBus;
        [Range(0f, 1f)] public float passengerBoardsBusVolume = 0.9f;

        [Tooltip("Looping rotor sound for the HELICOPTER joker — plays while the chopper flies in, lifts the vehicle, " +
                 "carries it to the stop, and leaves. Leave EMPTY to randomly use one of the 3 built-in helicopter_0N " +
                 "clips (Resources/Sounds). Look for: a steady rotor 'whomp-whomp' LOOP (~2-4s).")]
        public AudioClip helicopter;
        [Range(0f, 1f)] public float helicopterVolume = 0.6f;

        [Header("REWARDS & ERRORS")]

        [Tooltip("Plays when you earn or spend coins — rewards, purchases, joker costs. Look for: a coin jingle / chime (~0.3s).")]
        [FormerlySerializedAs("coin")]
        public AudioClip coinReward;
        [Range(0f, 1f)] public float coinRewardVolume = 1f;

        [Tooltip("Plays on a blocked/invalid action — not enough coins, locked joker, no valid move. Look for: a low 'buzz' (~0.2s).")]
        [FormerlySerializedAs("error")]
        public AudioClip invalidActionError;
        [Range(0f, 1f)] public float invalidActionErrorVolume = 0.9f;

        [Header("LEVEL RESULT")]

        [Tooltip("Plays when you finish a level/bonus successfully. Look for: a happy victory jingle (~0.4-1s).")]
        [FormerlySerializedAs("win")]
        public AudioClip levelComplete;
        [Range(0f, 1f)] public float levelCompleteVolume = 1f;

        [Tooltip("Plays when a level fails (deadlock or bonus timeout). Look for: a short sad / 'fail' tone (~0.4-1s).")]
        [FormerlySerializedAs("lose")]
        public AudioClip levelFailed;
        [Range(0f, 1f)] public float levelFailedVolume = 1f;

        [Header("UI")]

        [Tooltip("Plays on EVERY UI button press (menu, HUD, panels, shop). Look for: a crisp click / tap (~0.05-0.1s).")]
        [FormerlySerializedAs("click")]
        public AudioClip uiButtonClick;
        [Range(0f, 1f)] public float uiButtonClickVolume = 0.7f;

        [Header("CHESTS")]

        [Tooltip("Plays when a chest pops open revealing a COMMON car. Empty -> built-in procedural fanfare.")]
        public AudioClip chestCommon;
        [Tooltip("Chest open — UNCOMMON car. Empty -> built-in fanfare.")]
        public AudioClip chestUncommon;
        [Tooltip("Chest open — EPIC car. Empty -> built-in fanfare.")]
        public AudioClip chestEpic;
        [Tooltip("Chest open — LEGENDARY car (the grail). Empty -> built-in fanfare.")]
        public AudioClip chestLegendary;
        [Tooltip("Volume for ALL chest-open sounds.")]
        [Range(0f, 1f)] public float chestVolume = 0.85f;

        [Header("MUSIC")]

        [Tooltip("Main-menu background music — loops on the menu screen.")]
        public AudioClip menuMusic;
        [Range(0f, 1f)] public float menuMusicVolume = 0.5f;

        [Tooltip("One track per theme; loops while you play that theme's levels. Night/Bonus default to the night track.")]
        public ThemeMusic[] themeMusic;

        [Header("MASTER")]
        [Tooltip("Master multiplier applied on top of every SFX (does not affect music).")]
        [Range(0f, 1f)] public float volume = 1f;

        // ---- lookups used by Sfx / MusicManager ----

        public bool TryGetThemeMusic(string theme, out AudioClip clip, out float vol)
        {
            if (themeMusic != null)
                foreach (var t in themeMusic)
                    if (t != null && t.theme == theme) { clip = t.music; vol = t.volume; return true; }
            clip = null; vol = 0.5f;
            return false;
        }

        // Chest-open fanfare override for a won car's rarity (0 Common .. 3 Legendary); null -> Sfx uses its built-in.
        public AudioClip ChestClip(int rarity)
        {
            switch (Mathf.Clamp(rarity, 0, 3))
            {
                case 1:  return chestUncommon;
                case 2:  return chestEpic;
                case 3:  return chestLegendary;
                default: return chestCommon;
            }
        }

        const string MusicPath = "Sounds/musics/";

        public static AudioClip DefaultMenuTrack() => Resources.Load<AudioClip>(MusicPath + "track_01_sunny_cruise");

        // Default theme -> track mapping (night themes get the night track). Used both to pre-fill the list in the
        // editor and as a runtime fallback if a theme's slot is left empty.
        public static AudioClip DefaultThemeTrack(string theme)
        {
            string n;
            switch (theme)
            {
                case "Night":  case "Bonus": n = "track_02_night_drive";   break;
                case "Sunset":               n = "track_08_sunset_coast";  break;
                case "Beach":                n = "track_04_lazy_afternoon";break;
                case "Forest":               n = "track_10_dreamy_sky";    break;
                case "Desert":               n = "track_05_arcade_rush";   break;
                case "City":                 n = "track_09_funky_jam";     break;
                case "Candy":                n = "track_07_bubble_pop";    break;
                case "Park":                 n = "track_06_happy_hop";     break;
                case "Snow":                 n = "track_03_bouncy_town";   break;
                case "Autumn":               n = "track_08_sunset_coast";  break;
                default:                     n = "track_06_happy_hop";     break;
            }
            return Resources.Load<AudioClip>(MusicPath + n);
        }

#if UNITY_EDITOR
        // Keep the per-theme music list in sync with the actual themes: one entry each, labelled, with a sensible
        // default track pre-assigned. Existing assignments (by theme name) are preserved across syncs.
        void OnValidate()
        {
            if (menuMusic == null) menuMusic = DefaultMenuTrack();

            var themes = Themes.AllThemes();
            var prev = themeMusic;
            themeMusic = new ThemeMusic[themes.Length];
            for (int i = 0; i < themes.Length; i++)
            {
                string nm = themes[i].name;
                ThemeMusic keep = null;
                if (prev != null)
                    foreach (var o in prev) if (o != null && o.theme == nm) { keep = o; break; }
                if (keep != null) { keep.theme = nm; themeMusic[i] = keep; }
                else themeMusic[i] = new ThemeMusic { theme = nm, music = DefaultThemeTrack(nm), volume = 0.5f };
            }
        }
#endif
    }
}
