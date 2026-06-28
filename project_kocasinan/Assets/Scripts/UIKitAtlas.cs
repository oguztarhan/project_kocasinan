using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Build-safe sprite registry for the 300Mind UI kit. <see cref="UIKit"/> normally resolves atlas sub-sprites via
    /// AssetDatabase, which exists ONLY in the editor — so in a PLAYER BUILD every code-built UI sprite came back null
    /// and rendered as a default white box ("looks bad/default on device"). This ScriptableObject — baked into
    /// Resources by "BusJam ▸ Bake UIKit Resources" — holds hard references to every kit sprite by name, so the build
    /// actually includes the textures and UIKit can look them up at runtime. Re-run the baker if the atlas slicing changes.
    /// </summary>
    public class UIKitAtlas : ScriptableObject
    {
        // Parallel arrays (sprite[i] is named name[i]); kept as arrays so they serialize cleanly into the .asset.
        public string[] names;
        public Sprite[] sprites;

        Dictionary<string, Sprite> _map;

        /// <summary>Sprite registered under <paramref name="name"/> (the atlas sub-sprite name), or null.</summary>
        public Sprite Find(string name)
        {
            if (_map == null)
            {
                int n = names != null ? names.Length : 0;
                _map = new Dictionary<string, Sprite>(n);
                if (names != null && sprites != null)
                    for (int i = 0; i < names.Length && i < sprites.Length; i++)
                        if (!string.IsNullOrEmpty(names[i]) && sprites[i] != null) _map[names[i]] = sprites[i];
            }
            return _map.TryGetValue(name, out var s) ? s : null;
        }
    }
}
