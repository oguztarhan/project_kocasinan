using UnityEngine;

namespace BusJam
{
    public enum SkinCategory { Vehicle, Passenger, Board }
    public enum SkinRarity { Common, Uncommon, Rare, Epic, Legendary }

    // A code-GENERATED pattern, drawn as a transparent texture in the skin's accent colour and laid over the vehicle.
    public enum SkinPattern { None, Stripes, Checker, Dots, Hazard, Halves, Grid, Camo, Flames, Star, Carbon, Bolt }

    // RoofDecal = a small patch on the roof (body shows around it). BodyWrap = covers the whole roof. EITHER WAY the
    // pattern texture is transparent where there's no design, so the body's gameplay match-colour always shows through.
    public enum SkinApply { RoofDecal, BodyWrap }

    /// <summary>
    /// A cosmetic vehicle skin. v3 = TEXTURE skins: a procedurally-generated pattern (accent-coloured, transparent
    /// background) is laid over the default vehicle as a roof decal or a full wrap. The body keeps the gameplay
    /// match-colour underneath, so colour-matching is ALWAYS clear. Classic = no pattern (plain).
    /// </summary>
    public class SkinDef
    {
        public string id;             // stable key persisted in the owned-CSV, e.g. "veh_racer"
        public SkinCategory category;
        public SkinRarity rarity;
        public string displayName;
        public bool isDefault;        // Classic = the plain look, always owned

        // ---- texture skin (v3) ----
        public SkinPattern pattern = SkinPattern.None;
        public SkinApply apply = SkinApply.RoofDecal;
        public Color accent = Color.white; // the pattern colour
        public bool glow;                  // brighter / emissive accent (neon / galaxy)

        public bool HasPattern => pattern != SkinPattern.None;

        // ---- dormant model-swap fields (unused by the texture skins) ----
        public string[] carModels;
        public Color busAccent = Color.white;
        public bool HasModels => carModels != null && carModels.Length > 0;
    }
}
