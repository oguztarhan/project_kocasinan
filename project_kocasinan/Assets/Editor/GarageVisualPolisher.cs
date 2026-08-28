using UnityEditor;
using UnityEngine;
using Ridebury;

/// <summary>Assigns the project's cut storefront artwork to the code-built Garage and Vehicles screens.</summary>
public static class GarageVisualPolisher
{
    const string PrefabPath = "Assets/Resources/UI/GaragePanel.prefab";
    const string Art = "Assets/kesilmis-ikonlar/";
    const string Marker = "GarageVisualDesign_20260828_V2";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.update -= TryPatch;
        EditorApplication.update += TryPatch;
    }

    [MenuItem("Tools/300Mind UI/Polish Garage And Vehicles")]
    public static void PatchFromMenu() => Patch();

    static void TryPatch()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= TryPatch;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null && FindDeep(prefab.transform, Marker) != null) return;
        Patch();
    }

    static void Patch()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var cfg = root.GetComponentInChildren<InGameGarage>(true);
            if (cfg == null)
            {
                Debug.LogError("[GarageVisualPolisher] InGameGarage marker is missing from " + PrefabPath);
                return;
            }

            cfg.garageWindowSprite = Cut("lacivert-panel-uzun-temiz.png");
            cfg.vehiclesWindowSprite = Cut("lacivert-panel-uzun-temiz.png");
            cfg.vehiclesButtonSprite = Cut("bar_red.png");
            cfg.freeChestSprite = Cut("bar_cream.png");
            cfg.legendaryChestSprite = Cut("panel_row_gold.png");

            cfg.commonSprite = Cut("bar_cream.png");
            cfg.uncommonSprite = Cut("bar_cream.png");
            cfg.epicSprite = Cut("bar_cream.png");
            cfg.legendarySprite = Cut("panel_row_gold.png");
            cfg.chestsHeaderSprite = Cut("bar_red.png");
            cfg.craftHeaderSprite = Cut("bar_red.png");
            cfg.chestsHeaderHeight = 104f;
            cfg.craftHeaderHeight = 104f;
            cfg.chestsHeaderFontSize = 40;
            cfg.craftHeaderFontSize = 40;

            cfg.bronzeCardSprite = Cut("panel_row_cream.png");
            cfg.silverCardSprite = Cut("panel_row_cream.png");
            cfg.goldCardSprite = Cut("panel_row_gold.png");
            cfg.bronzeChestIcon = Cut("chest_bronze.png");
            cfg.silverChestIcon = Cut("chest_silver.png");
            cfg.goldChestIcon = Cut("chest_gold.png");
            cfg.legendaryChestIcon = Cut("chest_legendary.png");
            cfg.counterBarSprite = Cut("panel_row_gold.png");
            cfg.coinCounterIcon = Cut("icon_coin.png");
            cfg.gemCounterIcon = Cut("icon_gem.png");
            cfg.actionButtonSprite = Cut("btn_orange.png");
            cfg.chestCellSize = new Vector2(250f, 280f);
            cfg.chestSpacing = new Vector2(18f, 18f);

            // A transparent override colour means the source asset is shown at full colour by GOverride.
            cfg.garageWindowColor = cfg.vehiclesWindowColor = cfg.vehiclesButtonColor = Color.clear;
            cfg.freeChestColor = cfg.legendaryChestColor = Color.clear;
            cfg.commonColor = cfg.uncommonColor = cfg.epicColor = cfg.legendaryColor = Color.clear;
            cfg.chestsHeaderColor = cfg.craftHeaderColor = Color.clear;
            cfg.bronzeCardColor = cfg.silverCardColor = cfg.goldCardColor = Color.clear;

            var old = FindDeep(root.transform, Marker);
            if (old == null)
            {
                var marker = new GameObject(Marker);
                marker.transform.SetParent(root.transform, false);
                marker.SetActive(false);
            }

            EditorUtility.SetDirty(cfg);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[GarageVisualPolisher] Applied cut UI artwork -> " + PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Sprite Cut(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
