using System.Collections.Generic;
using UnityEngine;

namespace Ridebury
{
    /// <summary>The static skin list. v3 = TEXTURE skins: each is a code-generated pattern (accent-coloured,
    /// transparent background) laid over the default vehicle as a roof decal or a full wrap. The body keeps the
    /// gameplay match-colour, so colour stays clear. Classic (index 0) = the plain default, always owned.</summary>
    public static class SkinCatalog
    {
        public const string DefaultVehicleId = "veh_classic";
        public static readonly List<SkinDef> Vehicles = new List<SkinDef>();

        static readonly Color White  = Color.white;
        static readonly Color Cyan   = new Color(0.20f, 0.90f, 0.98f);
        static readonly Color Yellow = new Color(0.99f, 0.83f, 0.12f);
        static readonly Color Red    = new Color(0.93f, 0.18f, 0.18f);
        static readonly Color Lime   = new Color(0.45f, 0.90f, 0.25f);
        static readonly Color Army   = new Color(0.42f, 0.50f, 0.26f);
        static readonly Color Dark   = new Color(0.13f, 0.14f, 0.17f);
        static readonly Color Orange = new Color(1.00f, 0.50f, 0.08f);
        static readonly Color Gold   = new Color(1.00f, 0.80f, 0.22f);
        static readonly Color Purple = new Color(0.66f, 0.30f, 0.96f);

        static SkinCatalog()
        {
            // id, rarity, name, pattern, apply, accent, glow, isDefault
            // ---- Common ----
            V("veh_classic", SkinRarity.Common, "Classic", SkinPattern.None,    SkinApply.RoofDecal, White, def: true);
            V("veh_racer",   SkinRarity.Common, "Racer",   SkinPattern.Stripes, SkinApply.RoofDecal, White);
            V("veh_bubbles", SkinRarity.Common, "Bubbles", SkinPattern.Dots,    SkinApply.RoofDecal, Cyan);

            // ---- Uncommon ----
            V("veh_hazard",  SkinRarity.Uncommon, "Hazard", SkinPattern.Hazard,  SkinApply.BodyWrap,  Yellow);
            V("veh_sport",   SkinRarity.Uncommon, "Sport",  SkinPattern.Stripes, SkinApply.BodyWrap,  Red);
            V("veh_grid",    SkinRarity.Uncommon, "Grid",   SkinPattern.Grid,    SkinApply.RoofDecal, Lime);

            // ---- Rare ----
            V("veh_checker", SkinRarity.Rare, "Checker", SkinPattern.Checker, SkinApply.BodyWrap, Yellow);
            V("veh_camo",    SkinRarity.Rare, "Camo",    SkinPattern.Camo,    SkinApply.BodyWrap, Army);
            V("veh_carbon",  SkinRarity.Rare, "Carbon",  SkinPattern.Carbon,  SkinApply.BodyWrap, Dark);

            // ---- Epic ----
            V("veh_flames",  SkinRarity.Epic, "Flames", SkinPattern.Flames, SkinApply.BodyWrap,  Orange);
            V("veh_star",    SkinRarity.Epic, "Star",   SkinPattern.Star,   SkinApply.RoofDecal, Gold);
            V("veh_bolt",    SkinRarity.Epic, "Bolt",   SkinPattern.Bolt,   SkinApply.RoofDecal, Cyan, glow: true);

            // ---- Legendary ----
            V("veh_galaxy",  SkinRarity.Legendary, "Galaxy",  SkinPattern.Star,   SkinApply.BodyWrap, Purple, glow: true);
            V("veh_inferno", SkinRarity.Legendary, "Inferno", SkinPattern.Flames, SkinApply.BodyWrap, Red,    glow: true);
        }

        static void V(string id, SkinRarity r, string name, SkinPattern pat, SkinApply ap, Color accent, bool glow = false, bool def = false)
        {
            Vehicles.Add(new SkinDef
            {
                id = id, category = SkinCategory.Vehicle, rarity = r, displayName = name,
                pattern = pat, apply = ap, accent = accent, glow = glow, isDefault = def
            });
        }

        public static SkinDef ById(string id)
        {
            if (!string.IsNullOrEmpty(id))
                for (int i = 0; i < Vehicles.Count; i++) if (Vehicles[i].id == id) return Vehicles[i];
            return null;
        }

        public static List<SkinDef> ByCategory(SkinCategory c) =>
            c == SkinCategory.Vehicle ? Vehicles : new List<SkinDef>();

        public static List<SkinDef> ByRarity(SkinCategory c, SkinRarity r)
        {
            var list = new List<SkinDef>();
            foreach (var s in ByCategory(c)) if (s.rarity == r) list.Add(s);
            return list;
        }
    }
}
