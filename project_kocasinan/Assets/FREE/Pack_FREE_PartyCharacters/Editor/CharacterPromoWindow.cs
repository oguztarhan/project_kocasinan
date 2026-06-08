using UnityEngine;
using UnityEditor;
namespace CapsuleCharacter.EditorTools
{
    public class CharacterPromoWindow : EditorWindow
    {
        Texture2D promoTex;

        void OnEnable()
        {
            promoTex = Resources.Load<Texture2D>("Icons/promo_image");
            AdjustHeight();
        }

        void AdjustHeight()
        {
            if (promoTex == null) return;

            float w = 330 - 20f; // pencere genişliği - margin
            float h = w * ((float)promoTex.height / promoTex.width);

            float totalHeight =
                10 +    // top space
                h +
                10 +    // space after image
                28 +    // green button height
                5 +     // space
                24 +    // close button height
                10;     // bottom space

            var pos = position;
            pos.height = totalHeight;
            position = pos;
        }

        void OnGUI()
        {
            GUILayout.Space(10);

            // IMAGE
            if (promoTex != null)
            {
                float w = position.width - 20;
                float h = w * ((float)promoTex.height / promoTex.width);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(promoTex, GUILayout.Width(w), GUILayout.Height(h));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Image not found in Resources/Icons/");
            }

            GUILayout.Space(10);

            // GREEN BUTTON
            GUIStyle greenButton = new GUIStyle(GUI.skin.button);
            greenButton.normal.textColor = Color.white;
            greenButton.fontStyle = FontStyle.Bold;
            greenButton.padding = new RectOffset(8, 8, 6, 6);

            greenButton.normal.background = MakeTex(2, 2, new Color(0.18f, 0.65f, 0.25f));
            greenButton.hover.background = MakeTex(2, 2, new Color(0.22f, 0.75f, 0.30f));

            if (GUILayout.Button("Open Asset Store Page", greenButton, GUILayout.Height(30)))
                Application.OpenURL("https://assetstore.unity.com/packages/3d/characters/humanoids/party-game-character-pack-339036");

            GUILayout.Space(5);

        }

        Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;

            Texture2D tex = new Texture2D(width, height);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        public static void ShowWindow()
        {
            var window = CreateInstance<CharacterPromoWindow>();
            window.titleContent = new GUIContent("Upgrade");

            // Default width & rough height
            window.position = new Rect(600, 300, 330, 360);
            window.ShowUtility();
        }
    }
}
