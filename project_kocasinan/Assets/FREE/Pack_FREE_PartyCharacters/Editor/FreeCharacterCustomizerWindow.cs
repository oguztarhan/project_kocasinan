using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;


/// <summary>
/// Character customization editor window for selecting materials, hats, and blendshapes.
/// Automatically applies visual changes to the character in the scene.
/// </summary>
namespace CapsuleCharacter.EditorTools
{
    public class FreeCharacterCustomizerWindow : EditorWindow
    {
        #region Fields

        // Camera State
        Vector3 originalCamPos;
        Quaternion originalCamRot;
        bool cameraChanged = false;
        Vector3 defaultCamPos;
        Quaternion defaultCamRot;

        bool lockBody = false;
        bool lockFace = false;
        bool lockHat = false;
        bool lockBlendShapes = false;

        // Character Data
        static GameObject spawnedCharacter;
        Material[] bodyMaterials;
        Material[] faceMaterials;
        GameObject[] hatObjects;
        List<GameObject> instantiatedHats = new List<GameObject>();

        // Selection Indices
        int bodyIndex = 0;
        int faceIndex = 0;
        int hatIndex = 0;
        string characterName = "";
        bool isLoading = true;
        bool watchingCharacter = false;

        // BlendShape Data (Kullanıcı isteği üzerine sahte verilerle dolduruldu)
        Vector2 blendScroll;
        float[] blendValues = new float[] { 19.1903f, 62.9862f, 17.2358f, -12.647f, 55.2633f, 42.6461f };
        string[] blendNames = new string[] { "Top", "Mid", "Bottom", "Stomach", "Fat", "Limbs" };

        // GUI Styles
        Color sectionColor = new Color(0.15f, 0.15f, 0.15f);

        Texture2D iconLockOn;
        Texture2D iconLockOff;
        Texture2D iconArrowLeft;
        Texture2D iconArrowRight;
        Texture2D iconResetCam;

        Dictionary<int, float> arrowScales = new Dictionary<int, float>();
        Dictionary<int, float> buttonScales = new Dictionary<int, float>();



        #endregion

        #region Unity Messages

        void Update()
        {
            // If the character is deleted from the scene, return the camera to its original position
            if (cameraChanged && spawnedCharacter == null)
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null)
                {
                    sceneView.pivot = originalCamPos;
                    sceneView.rotation = originalCamRot;
                    sceneView.orthographic = false;
                    sceneView.Repaint();
                }
                cameraChanged = false;
            }
        }


        [MenuItem("Tools/Free Customization Window")]
        public static void Init()
        {
            var window = GetWindow<FreeCharacterCustomizerWindow>("Free Character Customization");
            window.minSize = new Vector2(400, 500);
            window.Show();

            // ✅ Find and delete the old character
            var existingCharacter = GameObject.Find("Character_Customize");
            if (existingCharacter != null)
                Object.DestroyImmediate(existingCharacter);

            window.isLoading = true;

            EditorApplication.delayCall += () =>
            {
                window.SpawnCharacterFromResources();
            };
        }


        /// <summary>
        /// Loads the default character prefab from Resources and spawns it in the scene.
        /// </summary>
        void SpawnCharacterFromResources()
        {
            // 🔹 Delete any previous instance
            var existingCharacter = GameObject.Find("Character_Customize");
            if (existingCharacter != null)
                DestroyImmediate(existingCharacter);

            // 🔹 Try to load prefab
            GameObject prefab = Resources.Load<GameObject>("Prefabs/character_default");

            if (prefab == null)
            {
                Debug.LogError("❌ Character prefab not found! Make sure it exists under: Assets/Resources/Prefabs/character_default.prefab");
                isLoading = false;
                Repaint();
                return;
            }

            // 🔹 Instantiate prefab
            spawnedCharacter = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            spawnedCharacter.name = "Character_Customize";

            // 🔹 Load and apply data
            LoadCharacterData();

            bodyIndex = Random.Range(0, bodyMaterials.Length);
            faceIndex = Random.Range(0, faceMaterials.Length);
            if (hatObjects != null && hatObjects.Length > 0)
                hatIndex = Random.Range(0, hatObjects.Length);

            ApplySelections();

            // 🔹 Focus the camera
            FocusCameraOnCharacter();
        }




        void OnGUI()
        {

            GUI.FocusControl(null);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            GUIStyle arrowStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                fixedWidth = 45,
                fixedHeight = 25
            };

            // Screen to show when character is not found instead of Loading state
            if (isLoading || bodyMaterials == null || faceMaterials == null || bodyMaterials.Length == 0 || faceMaterials.Length == 0)
            {
                DrawNoCharacterScreen();
                return;
            }

            // --- HEADER BAR ---
            EditorGUILayout.BeginVertical("box");
            GUILayout.Space(4);

            Rect titleRect = EditorGUILayout.GetControlRect(false, 26);
            GUIStyle customHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                normal = { textColor = Color.white }
            };

            GUI.Label(titleRect, "CHARACTER CUSTOMIZATION", customHeaderStyle);

            // 🎥 Reset Camera icon
            if (iconResetCam != null)
            {
                float camSize = 20f;
                Rect camRect = new Rect(titleRect.xMax - camSize - 4, titleRect.y + 3, camSize, camSize);
                GUIContent camContent = new GUIContent(iconResetCam, "Toggle camera position");
                if (GUI.Button(camRect, camContent, GUIStyle.none))
                    ResetCameraPosition();

                GUI.DrawTexture(camRect, iconResetCam, ScaleMode.ScaleToFit, true);
            }

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();


            if (spawnedCharacter == null)
            {
                DrawNoCharacterScreen();
                return;
            }


            DrawSelector(ref bodyIndex, "Body Color", System.Array.ConvertAll(bodyMaterials, m => m.name), arrowStyle, ApplySelections, ref lockBody);
            DrawSelector(ref faceIndex, "Face Materıal", System.Array.ConvertAll(faceMaterials, m => m.name), arrowStyle, ApplySelections, ref lockFace);
            DrawSelector(ref hatIndex, "Hat Selectıon", System.Array.ConvertAll(hatObjects, h => h.name), arrowStyle, ApplySelections, ref lockHat);


            // Blendshape GUI kısmı
            if (blendNames != null && blendValues != null)
            {
                GUILayout.Space(10);

                // Başlangıç rect
                Rect startRect = GUILayoutUtility.GetLastRect();

                EditorGUILayout.BeginVertical("box");

                // --- Header area ---
                Rect headerRect = EditorGUILayout.GetControlRect(false, 28);
                GUIStyle blendHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
                };
                GUI.Label(headerRect, "BlendShapes", blendHeaderStyle);

                GUILayout.Space(6);

                // --- Sliders ---
                blendScroll = EditorGUILayout.BeginScrollView(blendScroll, GUILayout.Height(140));
                for (int i = 0; i < blendNames.Length; i++)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Slider(blendNames[i], blendValues[i], -40, 100);
                    }
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.EndVertical();

                // Bitiş rect
                Rect endRect = GUILayoutUtility.GetLastRect();

                // Kutunun tamamını kapsayan görünmez buton
                Rect fullArea = new Rect(
                    0,
                    startRect.y + 10,                    // ilk spacing sonrası başlasın
                    position.width,                      // pencerenin tamamı genişlik
                    endRect.yMax - (startRect.y + 10)    // toplam yükseklik
                );

                if (GUI.Button(fullArea, GUIContent.none, GUIStyle.none))
                {

                    CharacterPromoWindow window = ScriptableObject.CreateInstance<CharacterPromoWindow>();
                    window.titleContent = new GUIContent("Upgrade");
                    window.position = new Rect(600, 300, 330, 420);
                    window.ShowUtility();  // küçük popup pencere
                    Application.OpenURL("https://assetstore.unity.com/packages/3d/characters/humanoids/party-game-character-pack-339036");
                    Debug.Log("These features and more character variations are available in the paid version!");

                }

            }

            GUILayout.Label("Character Name", EditorStyles.boldLabel);

            Rect nameRect = EditorGUILayout.GetControlRect(GUILayout.Height(22));
            EditorGUI.DrawRect(nameRect, new Color(0.25f, 0.25f, 0.25f));

            GUIStyle nameFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.cyan }
            };

            // TextField
            characterName = GUI.TextField(nameRect, characterName, nameFieldStyle);

            // If the field is empty, write a hint below it
            if (string.IsNullOrEmpty(characterName))
            {
                GUIStyle hintStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }, // gray, fixed color
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.UpperLeft
                };
                GUI.Label(new Rect(nameRect.x + 5, nameRect.y + nameRect.height, nameRect.width, 20), "Enter a name for the character...");
            }

            GUILayout.Space(20);

            // --- BUTTONS ---
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Randomize", GUILayout.Height(35)))
            {
                RandomizeCharacter();
            }
            if (GUILayout.Button("Reset", GUILayout.Height(35)))
            {
                ClearValues();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Generate Prefab", GUILayout.Height(40)))
            {
                GeneratePrefab();
            }

            // --- FOOTER ---
            GUILayout.FlexibleSpace();

            GUILayout.Space(5);
        }


        void DrawNoCharacterScreen()
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                wordWrap = true
            };

            GUILayout.FlexibleSpace();
            GUILayout.Label("Prefab not found or data is missing.\nEnsure 'character_default.prefab' is in a 'Resources/Prefabs' folder.", style);

            if (GUILayout.Button("Try Reload"))
            {
                Init();
            }
            GUILayout.FlexibleSpace();
        }


        void DrawSelector(ref int index, string label, string[] options, GUIStyle arrowStyle, System.Action onUpdate, ref bool lockFlag)
        {
            if (options == null || options.Length == 0)
                return;

            EditorGUILayout.BeginVertical("box");
            GUILayout.Space(4);

            // --- Header ---
            Rect headerRect = EditorGUILayout.GetControlRect(false, 28);
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
            GUI.Label(headerRect, label, labelStyle);

            // --- Lock Icon ---
            float iconSize = 22f;
            Rect lockRect = new Rect(headerRect.xMax - iconSize - 10, headerRect.y + 3, iconSize, iconSize);
            Texture2D lockTex = lockFlag ? iconLockOff : iconLockOn;
            GUIContent lockContent = new GUIContent("", "Lock to prevent random changes.");
            if (GUI.Button(lockRect, lockContent, GUIStyle.none))
                lockFlag = !lockFlag;
            if (lockTex != null)
                GUI.DrawTexture(lockRect, lockTex, ScaleMode.ScaleToFit, true);

            GUILayout.Space(6);

            // --- Selector ---
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("<", arrowStyle)) { index = (index - 1 + options.Length) % options.Length; onUpdate?.Invoke(); }
            EditorGUILayout.LabelField(options[index], new GUIStyle(EditorStyles.textField) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.cyan } });
            if (GUILayout.Button(">", arrowStyle)) { index = (index + 1) % options.Length; onUpdate?.Invoke(); }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }


        #endregion

        #region Core Logic

        /// <summary>
        /// Loads all customization data (materials, hats, blendshapes) from Resources.
        /// </summary>
        void LoadCharacterData()
        {
            if (spawnedCharacter == null)
            {
                Debug.LogWarning("⚠️ No character to load data for.");
                return;
            }

            bodyMaterials = Resources.LoadAll<Material>("Materials/Body")
                .OrderBy(m => m.name, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            faceMaterials = Resources.LoadAll<Material>("Materials/Face")
                .OrderBy(m =>
                {
                    string num = System.Text.RegularExpressions.Regex.Match(m.name, @"\d+").Value;
                    return string.IsNullOrEmpty(num) ? int.MaxValue : int.Parse(num);
                })
                .ToArray();


            hatObjects = Resources.LoadAll<GameObject>("Prefabs/Hats");
            instantiatedHats = new List<GameObject>();
            foreach (var hat in hatObjects)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(hat);
                Transform targetParent = spawnedCharacter.transform.Find("Armature/Hips/Spine/Spine1/Spine2/Head/customize_objects");
                if (targetParent == null)
                    continue;

                instance.transform.SetParent(targetParent, false);
                instance.SetActive(false);
                instantiatedHats.Add(instance);
            }

            lockBody = lockFace = lockHat = lockBlendShapes = false;
            isLoading = false;

            LoadIcons();
            ApplySelections();
            Repaint();
        }


        void LoadIcons()
        {
            iconLockOn = Resources.Load<Texture2D>("Icons/lock_on");
            iconLockOff = Resources.Load<Texture2D>("Icons/lock_off");
            iconArrowLeft = Resources.Load<Texture2D>("Icons/arrow_left");
            iconArrowRight = Resources.Load<Texture2D>("Icons/arrow_right");
            iconResetCam = Resources.Load<Texture2D>("Icons/reset_cam");
        }


        /// <summary>
        /// </summary>
        void ApplySelections()
        {
            // ✅ Safety check before accessing destroyed objects
            if (spawnedCharacter == null)
                return;

            // Avoid running if object was destroyed mid-frame
            if (spawnedCharacter.Equals(null))
                return;

            SkinnedMeshRenderer[] renderers = spawnedCharacter.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer smr = null;

            foreach (var r in renderers)
            {
                Transform t = r.transform;
                if (!t.IsChildOf(spawnedCharacter.transform.Find("Armature/Hips/Spine/Spine1/Spine2/Head/customize_objects")))
                {
                    smr = r;
                    break;
                }
            }

            if (smr == null) return;

            var mats = smr.sharedMaterials;

            // Apply body material
            if (bodyMaterials != null && mats.Length > 0 && bodyIndex < bodyMaterials.Length)
            {
                mats[0] = bodyMaterials[bodyIndex];
                smr.sharedMaterials = mats;
            }

            // Apply face material
            if (faceMaterials != null && mats.Length > 1 && faceIndex < faceMaterials.Length)
            {
                mats[1] = faceMaterials[faceIndex];
                smr.sharedMaterials = mats;
            }

            // Ensure hats exist
            if (instantiatedHats == null)
                instantiatedHats = new List<GameObject>();

            // Toggle active hat
            for (int i = 0; i < instantiatedHats.Count; i++)
            {
                if (instantiatedHats[i] == null) continue;
                instantiatedHats[i].SetActive(i == hatIndex);
            }

            EditorUtility.SetDirty(spawnedCharacter);
        }


        /// <summary>
        /// Resets all customization values to default.
        /// </summary>
        void ClearValues()
        {
            bodyIndex = faceIndex = hatIndex = 0;
            characterName = "";
            ApplySelections();
        }

        void RandomizeCharacter()
        {
            if (bodyMaterials == null || faceMaterials == null || hatObjects == null || spawnedCharacter == null)
                return;

            if (!lockBody)
                bodyIndex = Random.Range(0, bodyMaterials.Length);

            if (!lockFace)
                faceIndex = Random.Range(0, faceMaterials.Length);

            if (!lockHat)
                hatIndex = Random.Range(0, hatObjects.Length);

            ApplySelections();
            GUIUtility.ExitGUI();
        }


        #endregion

        #region Prefab Management

        /// <summary>
        /// Saves the customized character as a prefab with a unique name.
        /// </summary>
        void GeneratePrefab()
        {
            if (spawnedCharacter == null)
            {
                EditorUtility.DisplayDialog("Error", "Character not found!", "OK");
                return;
            }

            // Eğer karakter kaybolmuşsa tekrar oluştur
            if (spawnedCharacter == null)
            {
                SpawnCharacterFromResources();
                if (spawnedCharacter == null)
                {
                    EditorUtility.DisplayDialog("Error", "Character could not be generated.", "OK");
                    return;
                }
            }

            GameObject clone = Instantiate(spawnedCharacter);
            clone.name = string.IsNullOrEmpty(characterName) ? "New Character" : characterName;

            // --------------------------------------------------------
            string folderPath = "Assets/FREE/Pack_FREE_PartyCharacters/Prefabs/";

            string fileName = string.IsNullOrEmpty(characterName)
                ? "Character_" + System.Guid.NewGuid().ToString("N").Substring(0, 6)
                : characterName;

            string fullPath = folderPath + fileName + ".prefab";
            // --------------------------------------------------------

            PrefabUtility.SaveAsPrefabAsset(clone, fullPath);

            DestroyImmediate(clone);

            EditorUtility.DisplayDialog("Saved",
                "Prefab automatically saved to:\n" + fullPath,
                "OK");
        }

        #endregion

        #region Camera Controls

        void FocusCameraOnCharacter()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && spawnedCharacter != null)
            {
                if (!cameraChanged)
                {
                    originalCamPos = sceneView.pivot;
                    originalCamRot = sceneView.rotation;
                }

                // Calculate the center of the character
                Bounds bounds = new Bounds(spawnedCharacter.transform.position, Vector3.zero);
                Renderer[] renderers = spawnedCharacter.GetComponentsInChildren<Renderer>();
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                sceneView.LookAt(bounds.center, sceneView.rotation, 2.5f, sceneView.orthographic, false);
                cameraChanged = true;
            }
        }


        void ResetCameraPosition()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && cameraChanged)
            {
                sceneView.pivot = originalCamPos;
                sceneView.rotation = originalCamRot;
                sceneView.Repaint();
                cameraChanged = false;
            }
            else
            {
                FocusCameraOnCharacter();
            }
        }

        #endregion
    }
}
