/*
===================================================================
Unity Assets by Yel Scrypt Fire Studio: https://yelsfstudio.com/game-assets/
===================================================================

Online Docs (Latest): https://yelsfstudio.com/game-assets/
Offline Docs: You have a PDF file in the package folder.

=======
SUPPORT
=======

First of all, read the docs. If it didn’t help, get the support.

Web: https://yelsfstudio.com/contact/
Email: support@yelsfstudio.com

If you find a bug or you can’t use the asset as you need,
please first send email to support@yelsfstudio.com
before leaving a review to the asset store.

We are here to help you and to improve our products for the best.
*/

using System;

using UnityEditor;
using UnityEngine;

using Debug = UnityEngine.Debug;

namespace LPRVFP
{
    [HelpURL("https://yelsfstudio.com/game-assets/")]
    [InitializeOnLoad]
    public class PublisherWelcomeScreen : EditorWindow
    {
        private static PublisherWelcomeScreen window;
        private static Vector2 headerSize = new Vector2(500f, 130f);
        private static Vector2 windowSize = new Vector2(500f, 650f);
        private Vector2 scrollPosition;

        private static string windowHeaderText = "Unity Asset Store Publisher";
        private string copyright =
            "© Copyright " + DateTime.Now.Year + " Yel Scrypt Fire Studio";

        private const string isShowAtStartEditorPrefs = "WelcomeScreenShowAtStart";
        private static bool isShowAtStart = true;

        private static bool isInited;

        private static GUIStyle headerStyle;
        private static GUIStyle copyrightStyle;

        private static Texture2D allOurAssetsIcon;
        private static Texture2D SSP25DP1Icon;
        private static Texture2D docsIcon;
        private static Texture2D youTubeIcon;
        private static Texture2D facebookIcon;
        private static Texture2D supportIcon;
        private static Texture2D instagramIcon;
        private static Texture2D twitterIcon;

        static PublisherWelcomeScreen()
        {
            EditorApplication.update -= GetShowAtStart;
            EditorApplication.update += GetShowAtStart;
        }

        private void OnGUI()
        {
            if (!isInited)
            {
                Init();
            }

            if (GUI.Button(new Rect(0f, 0f, headerSize.x, headerSize.y),
                string.Empty, headerStyle))
            {
                Application.OpenURL("https://yelsfstudio.com/");
            }

            GUILayoutUtility.GetRect(position.width, 140f);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (DrawButton(docsIcon, "Online Documentation — Latest",
                "You also have offline docs in Unity Package."))
            {
                Application.OpenURL("https://yelsfstudio.com/game-assets/");
            }

            if (DrawButton(supportIcon, "Support — support@yelsfstudio.com",
                "First of all, read the docs. If it didn't help, get support."))
            {
                Application.OpenURL("https://yelsfstudio.com/contact/");
            }

            if (DrawButton(allOurAssetsIcon, "Unity Assets",
                "All our assets."))
            {
                Application.OpenURL("https://assetstore.unity.com/publishers/50616");
            }

            if (DrawButton(SSP25DP1Icon, "Space Shooter Projectile 2.5D Part 1 Package",
                "Buy Now!!!"))
            {
                Application.OpenURL("https://u3d.as/31Mn");
            }

            if (DrawButton(twitterIcon, "Twitter", "News page."))
            {
                Application.OpenURL("https://twitter.com/yelsfstudio");
            }

            if (DrawButton(instagramIcon, "Instagram", "News page."))
            {
                Application.OpenURL("https://www.instagram.com/yelsfstudio/");
            }

            if (DrawButton(youTubeIcon, "YouTube Channel",
                "Unity Tutorials & Demos."))
            {
                Application.OpenURL("https://www.youtube.com/@yelscryptfirestudio4203");
            }

            if (DrawButton(facebookIcon, "Facebook", "News page."))
            {
                Application.OpenURL("https://www.facebook.com/yelscryptfirestudio");
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(copyright, copyrightStyle);
        }

        private static bool Init()
        {
            try
            {
                headerStyle = new GUIStyle();
                headerStyle.normal.background =
                    Resources.Load("YelHeaderLogo") as Texture2D;
                headerStyle.normal.textColor = Color.white;
                headerStyle.fontStyle = FontStyle.Bold;
                headerStyle.padding = new RectOffset(340, 0, 27, 0);
                headerStyle.margin = new RectOffset(0, 0, 0, 0);

                copyrightStyle = new GUIStyle
                {
                    alignment = TextAnchor.MiddleRight
                };

                docsIcon = Resources.Load("Docs") as Texture2D;
                allOurAssetsIcon = Resources.Load("CyberpunkHammerIcon") as Texture2D;
                SSP25DP1Icon = Resources.Load("SSP25DP1_Icon") as Texture2D;
                supportIcon = Resources.Load("Support") as Texture2D;
                youTubeIcon = Resources.Load("YouTube") as Texture2D;
                facebookIcon = Resources.Load("Facebook") as Texture2D;
                instagramIcon = Resources.Load("Instagram") as Texture2D;
                twitterIcon = Resources.Load("Twitter") as Texture2D;

                isInited = true;
            }
            catch (Exception e)
            {
                Debug.Log("WELCOME SCREEN INIT: " + e);
                return false;
            }

            return true;
        }

        private static bool DrawButton(
            Texture2D icon, string title = "", string description = "")
        {
            GUILayout.BeginHorizontal();

            GUILayout.Space(34f);
            GUILayout.Box(icon, GUIStyle.none,
                GUILayout.MaxWidth(48f), GUILayout.MaxHeight(48f));
            GUILayout.Space(10f);

            GUILayout.BeginVertical();

            GUILayout.Space(1f);
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.Label(description);

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            Rect rect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            GUILayout.Space(10f);

            return Event.current.type == EventType.MouseDown
                && rect.Contains(Event.current.mousePosition);
        }


        private static void GetShowAtStart()
        {
            EditorApplication.update -= GetShowAtStart;

            isShowAtStart = EditorPrefs.GetBool(isShowAtStartEditorPrefs, true);

            if (isShowAtStart)
            {
                EditorApplication.update -= OpenAtStartup;
                EditorApplication.update += OpenAtStartup;
            }
        }

        private static void OpenAtStartup()
        {
            if (isInited && Init())
            {
                OpenWindow();

                EditorApplication.update -= OpenAtStartup;
            }
        }

        [MenuItem("Window/Yel Scrypt Fire Studio/Low Poly Road Vehicles Free Package/Welcome Screen", false)]
        public static void OpenWindow()
        {
            if (window == null)
            {
                window = GetWindow<PublisherWelcomeScreen>(
                    true, windowHeaderText, true);

                window.maxSize = window.minSize = windowSize;
            }
        }

        private void OnEnable()
        {
            window = this;
        }

        private void OnDestroy()
        {
            window = null;

            EditorPrefs.SetBool(isShowAtStartEditorPrefs, false);
        }
    }
}