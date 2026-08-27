using System.Collections.Generic;
using UnityEngine;

namespace Ridebury
{
    /// <summary>Resolves the equipped vehicle skin and loads its car models (cached). Cars pick a RANDOM model from
    /// the equipped theme for on-screen variety; the actual build/tint lives in RideburyGame.</summary>
    public static class SkinService
    {
        public static SkinDef EquippedVehicleSkin()
        {
            var s = SkinCatalog.ById(SaveSystem.EquippedVehicleSkin);
            return s ?? SkinCatalog.ById(SkinCatalog.DefaultVehicleId);
        }

        static readonly Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>();

        static GameObject Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (modelCache.TryGetValue(path, out var go) && go != null) return go;
            go = Resources.Load<GameObject>(path);
            modelCache[path] = go;
            return go;
        }

        /// <summary>A RANDOM car prefab from the equipped theme (variety on the board), or null for Classic.</summary>
        public static GameObject RandomCarModel(SkinDef skin)
        {
            if (skin == null || !skin.HasModels) return null;
            return Load(skin.carModels[Random.Range(0, skin.carModels.Length)]);
        }

        /// <summary>The shared bus model (othercars/bus.fbx, copied to Resources), used for every skinned theme's
        /// 2-cell buses, recolored per-vehicle to the match-color. Null if not present -> caller falls back.</summary>
        public const string BusModelPath = "CarSkins/BusSep"; // SimplePoly separate-material bus (clean body-only recolor)
        public static GameObject BusModel() => Load(BusModelPath);
    }
}
