using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Inspector-editable config for the (code-built) garage + wardrobe (vehicles) windows. <see cref="GameUI"/>
    /// ALWAYS builds the window chrome + cards in code (so they reflect the latest code), and reads the OVERRIDES
    /// below to restyle individual elements without touching code. GameUI reads this marker even when its canvas is
    /// INACTIVE, so you don't have to enable anything — just assign the Sprite/Colour you want.
    ///
    /// Each override: leave the Sprite empty AND the Colour at alpha 0 to keep that element's built-in look. Assign a
    /// Sprite to swap its image; set a Colour (alpha &gt; 0) to tint it. Assign both to use your image at that colour.
    /// </summary>
    public class InGameGarage : MonoBehaviour
    {
        public static InGameGarage Instance;

        // ---- Per-element image / colour overrides. Assign in the Inspector to restyle one specific garage element.
        //      Empty Sprite + alpha-0 Colour = keep the built-in look (nothing changes).
        [Header("Element overrides — Garage / Vehicles windows")]
        public Sprite garageWindowSprite;    public Color garageWindowColor;    // the Garage window (the panel that opens)
        public Sprite vehiclesWindowSprite;  public Color vehiclesWindowColor;  // the Vehicles ("Araçlar") window

        [Header("Element overrides — rows")]
        public Sprite vehiclesButtonSprite;  public Color vehiclesButtonColor;  // the "ARAÇLAR" button (opens the wardrobe)
        public Sprite freeChestSprite;       public Color freeChestColor;       // the FREE CHEST row image
        public Sprite legendaryChestSprite;  public Color legendaryChestColor;  // the key-only LEGENDARY chest row image

        [Header("Element overrides — CRAFT rows")]
        public Sprite commonSprite;          public Color commonColor;          // CRAFT: Common row image
        public Sprite uncommonSprite;        public Color uncommonColor;        // CRAFT: Uncommon row image
        public Sprite epicSprite;            public Color epicColor;            // CRAFT: Epic row image
        public Sprite legendarySprite;       public Color legendaryColor;       // CRAFT: Legendary row image

        // Returns the per-tier CRAFT override (tier: 0 Common, 1 Uncommon, 2 Epic, 3 Legendary).
        public Sprite CraftSprite(int tier) => tier == 3 ? legendarySprite : tier == 2 ? epicSprite : tier == 1 ? uncommonSprite : commonSprite;
        public Color  CraftColor(int tier)  => tier == 3 ? legendaryColor  : tier == 2 ? epicColor  : tier == 1 ? uncommonColor  : commonColor;

        // ---- CHESTS / CRAFT section headers — make them prominent. Sprite = banner image behind the header; Colour
        //      (alpha > 0) = tint; Height / Font-size = 0 keeps the built-in value.
        [Header("Section headers — CHESTS / CRAFT (0 height/size = keep default)")]
        public Sprite chestsHeaderSprite; public Color chestsHeaderColor; public float chestsHeaderHeight = 74f; public int chestsHeaderFontSize = 40; // "CHESTS" header
        public Sprite craftHeaderSprite;  public Color craftHeaderColor;  public float craftHeaderHeight  = 72f; public int craftHeaderFontSize  = 40; // "CRAFT" header

        // ---- Chest CARD backgrounds (behind the Bronze / Silver / Gold chests). Sprite swaps the image; Colour
        //      (alpha > 0) tints it. Empty = the built-in dark card.
        [Header("Chest card backgrounds — Bronze / Silver / Gold")]
        public Sprite bronzeCardSprite; public Color bronzeCardColor;
        public Sprite silverCardSprite; public Color silverCardColor;
        public Sprite goldCardSprite;   public Color goldCardColor;

        // ---- Button size + position. Tick the "override" box to drive the buttons from here (size = width/height in px,
        //      offset = position; +x right, +y up). Left unticked, the buttons keep their built-in layout.
        [Header("Button size & position — tick 'override' to control")]
        public bool overrideChestButtons; public Vector2 chestButtonSize = new Vector2(250, 78);  public Vector2 chestButtonOffset = new Vector2(0, 16);   // BRONZE/SILVER/GOLD buy buttons
        public bool overrideCraftButtons; public Vector2 craftButtonSize = new Vector2(280, 92);  public Vector2 craftButtonOffset = new Vector2(-160, 0); // CRAFT buttons

        // LEGACY baked-chrome refs — no longer adopted (GameUI builds the chrome in code so it always reflects the
        // latest changes). Kept only so old scene bakes deserialize; Awake still hides any leftover baked panels.
        [Header("Garage window (legacy baked refs — unused)")]
        public GameObject garageRoot;     // full-screen panel, shown/hidden by ShowGarage/HideGarage
        public Transform  garageContent;  // scroll "Content" the chest/entry cards are spawned under
        public Text       garageGold;     // gold counter label
        public Button     garageClose;    // red X (wired to HideGarage at runtime)

        [Header("Vehicles (wardrobe) window (legacy baked refs — unused)")]
        public GameObject vehiclesRoot;
        public Transform  vehiclesContent;
        public Text       vehiclesGold;
        public Button     vehiclesClose;

        // ---- Garage layout: tweak these in the Inspector to reposition/resize without baking. The runtime reads them
        //      each time the garage is built, so changes show on the next Play. Leave at default to keep the current look.
        [Header("Garage layout (Inspector-tunable, no bake)")]
        public Vector2 chestCellSize = new Vector2(275, 275); // size of each chest button
        public Vector2 chestSpacing  = new Vector2(15, 18);   // gap between chest buttons
        public int     chestColumns  = 3;                      // chests per row (1 = stack them vertically)
        public Vector2 shardOffset   = new Vector2(0, -102);   // shard counter offset from the gold chip (+x right, +y up; default = just below gold)
        public Vector2 revealSize    = new Vector2(820, 980); // chest-open popup size
        public Vector2 revealPos     = Vector2.zero;           // chest-open popup offset from screen centre

        // Optional DRAGGABLE slots created by "BusJam ▸ Bake Garage Cards". Drag/resize them in the Hierarchy to place
        // the chests / popup / shard counter; the runtime fills them. Leave empty to keep the default (Inspector) layout.
        [Header("Baked card slots (optional — 'BusJam ▸ Bake Garage Cards')")]
        public RectTransform chestArea;    // CHEST section (chests + legendary + free) is generated inside this box
        public RectTransform revealCard;   // chest-open popup takes this object's position + size
        public RectTransform shardSlot;    // shard counter chip is placed inside this object

        void Awake()
        {
            Instance = this;
            if (garageRoot)   garageRoot.SetActive(false);
            if (vehiclesRoot) vehiclesRoot.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
