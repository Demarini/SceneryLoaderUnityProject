#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.IO;
using System.Collections.Generic;

public sealed class SceneryBundleBuilder : EditorWindow
{
    // UI state
    string[] _bundleNames = new string[0];
    int _bundleIdx = 0;

    string[] _prefabDisplay = new string[0];
    string[] _prefabAssetPaths = new string[0];
    int _prefabIdx = 0;
    string _encryptionKeyInput = "";
    bool _useGlass = true;

    // Overall Puck Lighting (ambient)
    bool _useCustomAmbient = false;
    float _ambientMultiplier = 1f;

    // Game Directional Light (the URP main light — gives the stick its ice shadow)
    bool _useGameDirectional = true;
    bool _overrideGameDirIntensity = false;
    float _gameDirIntensity = 0.1f;
    bool _overrideGameDirShadowStrength = false;
    float _gameDirShadowStrength = 1f;

    // Bundle prop shadows (perf knob)
    bool _propsCastShadows = false;

    // Reflections — keep all probes (game + bundle) alive vs. only bundle probes
    bool _keepReflections = false;

    // Keep the game's non-directional point/spot lights alive — gives back the vanilla ice dots
    bool _keepGameLights = false;

    // Music
    bool _musicEnabled = false;
    string _musicFilePath = "";
    float _musicVolume = 0.5f;

    // Ambient Audio
    bool _ambientAudioEnabled = false;
    string _ambientAudioFilePath = "";
    float _ambientAudioVolume = 0.3f;

    // Goal Crowd Noise
    float _goalCrowdNoiseVolume = 0.37f;

    // Output folder (relative to project)
    const string OutputDir = "Build/AssetBundles";
    const BuildTarget Target = BuildTarget.StandaloneWindows64;

    Vector2 _scrollPos;

    [MenuItem("Tools/Bundles/Build Selected Bundle…")]
    public static void Open()
    {
        var w = GetWindow<SceneryBundleBuilder>("Scenery Bundle");
        w.minSize = new Vector2(420, 480);
        w.RefreshBundles();
        w.Show();
    }

    void OnEnable() => RefreshBundles();

    void RefreshBundles()
    {
        _bundleNames = AssetDatabase.GetAllAssetBundleNames();
        if (_bundleNames == null || _bundleNames.Length == 0)
        {
            _bundleNames = new[] { "<no bundles assigned – set assetBundleName on your assets>" };
            _bundleIdx = 0;
            _prefabDisplay = new string[0];
            _prefabAssetPaths = new string[0];
            return;
        }

        _bundleIdx = Mathf.Clamp(_bundleIdx, 0, _bundleNames.Length - 1);
        RefreshPrefabsForSelectedBundle();
    }

    void RefreshPrefabsForSelectedBundle()
    {
        if (_bundleNames.Length == 0) return;
        var bundle = _bundleNames[_bundleIdx];

        var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundle);
        _prefabAssetPaths = assetPaths.Where(p => p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)).ToArray();
        _prefabDisplay = _prefabAssetPaths.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray();
        _prefabIdx = Mathf.Clamp(_prefabIdx, 0, Mathf.Max(0, _prefabDisplay.Length - 1));
    }

    void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Select Bundle & Root Prefab", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(_bundleNames.Length == 0 || _bundleNames[0].StartsWith("<no bundles")))
        {
            int newIdx = EditorGUILayout.Popup(new GUIContent("Bundle"), _bundleIdx, _bundleNames);
            if (newIdx != _bundleIdx) { _bundleIdx = newIdx; RefreshPrefabsForSelectedBundle(); }

            if (_prefabDisplay.Length == 0)
            {
                EditorGUILayout.HelpBox("This bundle has no prefabs. Assign some assets to this bundle, or pick another bundle.", MessageType.Warning);
            }
            else
            {
                _prefabIdx = EditorGUILayout.Popup(new GUIContent("Root Prefab"), _prefabIdx, _prefabDisplay);
            }

            _useGlass = EditorGUILayout.Toggle(new GUIContent("Use Glass"), _useGlass);

            // --- Overall Puck Lighting (ambient) ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Overall Puck Lighting", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Multiplies the game's ambient light — affects how brightly everything in the scene is lit from all directions.", EditorStyles.wordWrappedMiniLabel);
            _useCustomAmbient = EditorGUILayout.Toggle(
                new GUIContent("Override Ambient", "Enable to multiply the game's ambient light. Leave off for default behavior."),
                _useCustomAmbient);
            using (new EditorGUI.DisabledScope(!_useCustomAmbient))
            {
                _ambientMultiplier = EditorGUILayout.Slider(
                    new GUIContent("Ambient Multiplier", "1.0 = unchanged, 0.5 = half brightness, 0.0 = black"),
                    _ambientMultiplier, 0f, 2f);
            }

            // --- Shadows & Game Directional Light ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Shadows & Game Directional", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The game's default directional is what puts a real shadow under the stick on the ice. URP only allows one shadow-casting main light, so this is it.", EditorStyles.wordWrappedMiniLabel);

            _useGameDirectional = EditorGUILayout.Toggle(
                new GUIContent("Use Game Directional", "Keep the game's default directional alive as the main shadow caster. Turn off only if your bundle has its own Directional Light named 'MainSun' to use instead."),
                _useGameDirectional);

            using (new EditorGUI.DisabledScope(!_useGameDirectional))
            {
                _overrideGameDirIntensity = EditorGUILayout.Toggle(
                    new GUIContent("Override Intensity", "Dim or brighten the game directional. Useful for moody scenes — set very low (0.05–0.15) so it barely lights but still casts a shadow."),
                    _overrideGameDirIntensity);
                using (new EditorGUI.DisabledScope(!_overrideGameDirIntensity))
                {
                    _gameDirIntensity = EditorGUILayout.Slider(
                        new GUIContent("Intensity", "Game default is ~0.5. Lower = less direct light."),
                        _gameDirIntensity, 0f, 2f);
                }

                _overrideGameDirShadowStrength = EditorGUILayout.Toggle(
                    new GUIContent("Override Shadow Strength", "How dark the cast shadow is. 0 = no shadow, 1 = fully dark."),
                    _overrideGameDirShadowStrength);
                using (new EditorGUI.DisabledScope(!_overrideGameDirShadowStrength))
                {
                    _gameDirShadowStrength = EditorGUILayout.Slider(
                        new GUIContent("Shadow Strength"),
                        _gameDirShadowStrength, 0f, 1f);
                }
            }

            EditorGUILayout.Space(4);
            _propsCastShadows = EditorGUILayout.Toggle(
                new GUIContent("Bundle Props Cast Shadows", "When OFF (recommended), bundle meshes don't render into the shadow map — huge perf win for dense scenes. The ice still receives the stick's shadow. Turn ON only if a specific prop needs to throw a real cast shadow."),
                _propsCastShadows);

            EditorGUILayout.Space(4);
            _keepGameLights = EditorGUILayout.Toggle(
                new GUIContent("Keep Game Point/Spot Lights", "When ON, the game's non-directional point/spot lights stay alive — restoring the small bright specular dots on the ice from the vanilla arena. Off for moody bundles; on for arena-style replacements."),
                _keepGameLights);

            // --- Reflections ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Reflections", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("By default the game's hangar reflection probe is disabled — its baked cubemap shows the original ceiling lights/rafters which look out of place in most custom scenes. Bundle-authored probes always stay alive. Turn on if you want the vanilla glossy-ice look without baking your own probe.", EditorStyles.wordWrappedMiniLabel);
            _keepReflections = EditorGUILayout.Toggle(
                new GUIContent("Keep All Reflection Probes", "When ON, all reflection probes — game's and bundle's — stay alive. The vanilla hangar reflection (ceiling lights, rafters) shows up on the ice. Useful for arena-style bundles."),
                _keepReflections);

            // --- Music ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Music", EditorStyles.boldLabel);
            _musicEnabled = EditorGUILayout.Toggle(new GUIContent("Enable Music"), _musicEnabled);
            using (new EditorGUI.DisabledScope(!_musicEnabled))
            {
                DrawFileField("Music File", ref _musicFilePath);
                _musicVolume = EditorGUILayout.Slider(new GUIContent("Music Volume"), _musicVolume, 0f, 1f);
            }

            // --- Ambient Audio ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Ambient Audio", EditorStyles.boldLabel);
            _ambientAudioEnabled = EditorGUILayout.Toggle(new GUIContent("Enable Ambient Audio"), _ambientAudioEnabled);
            using (new EditorGUI.DisabledScope(!_ambientAudioEnabled))
            {
                DrawFileField("Ambient Audio File", ref _ambientAudioFilePath);
                _ambientAudioVolume = EditorGUILayout.Slider(new GUIContent("Ambient Audio Volume"), _ambientAudioVolume, 0f, 1f);
            }

            // --- Goal Crowd Noise ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Game Audio", EditorStyles.boldLabel);
            _goalCrowdNoiseVolume = EditorGUILayout.Slider(
                new GUIContent("Goal Crowd Noise", "Volume multiplier for goal crowd noise (default 0.37)"),
                _goalCrowdNoiseVolume, 0f, 1f);

            // --- Output & Encryption ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Folder", Path.GetFullPath(OutputDir));
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Encryption", EditorStyles.boldLabel);

            _encryptionKeyInput = EditorGUILayout.TextField(
                new GUIContent("Key (optional)", "Leave blank to generate a random key. Otherwise supply a key string."),
                _encryptionKeyInput);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_prefabDisplay.Length == 0))
            {
                if (GUILayout.Button("Build Selected Bundle", GUILayout.Height(32)))
                {
                    BuildBundle();
                }
            }
        }

        if (GUILayout.Button("Refresh Lists")) RefreshBundles();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Builds the selected asset bundle, encrypts it to .abx, writes AssetInformation.json, " +
            "and copies audio files next to the bundle.\n\n" +
            "Players can adjust volumes in-game with /sl help.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    void DrawFileField(string label, ref string path)
    {
        EditorGUILayout.BeginHorizontal();
        path = EditorGUILayout.TextField(new GUIContent(label), path);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            var picked = EditorUtility.OpenFilePanel("Select Audio File", "", "mp3");
            if (!string.IsNullOrEmpty(picked)) path = picked;
        }
        EditorGUILayout.EndHorizontal();
    }

    void BuildBundle()
    {
        var bundleName = _bundleNames[_bundleIdx];
        var rootPrefabName = _prefabDisplay[_prefabIdx];

        try
        {
            Directory.CreateDirectory(OutputDir);

            var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            var build = new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetNames = assetPaths
            };

            var results = BuildPipeline.BuildAssetBundles(
                OutputDir, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                Target);

            if (results == null)
                throw new System.InvalidOperationException("BuildPipeline.BuildAssetBundles returned null.");

            var builtPath = Path.Combine(OutputDir, bundleName);
            if (!File.Exists(builtPath))
                throw new FileNotFoundException($"Built bundle not found at {builtPath}");

            var enc = EditorAbx.EncryptBundleToAbx(builtPath, _encryptionKeyInput);
            AbxManifestIO.Upsert(OutputDir, bundleName, enc.abxPath, enc.contentKey32);

            string musicFileName = CopyAudioFile(_musicEnabled, _musicFilePath, "music");
            string ambientAudioFileName = CopyAudioFile(_ambientAudioEnabled, _ambientAudioFilePath, "ambient audio");

            var info = new AssetInformation
            {
                useGlass = _useGlass,
                useCustomAmbient = _useCustomAmbient,
                ambientMultiplier = _ambientMultiplier,
                useGameDirectional = _useGameDirectional,
                gameDirectionalIntensity = _overrideGameDirIntensity ? _gameDirIntensity : -1f,
                gameDirectionalShadowStrength = _overrideGameDirShadowStrength ? _gameDirShadowStrength : -1f,
                propsCastShadows = _propsCastShadows,
                keepReflections = _keepReflections,
                keepGameLights = _keepGameLights,
                musicEnabled = _musicEnabled,
                musicPath = musicFileName,
                musicVolume = _musicVolume,
                ambientAudioEnabled = _ambientAudioEnabled,
                ambientAudioPath = ambientAudioFileName,
                ambientAudioVolume = _ambientAudioVolume,
                goalCrowdNoiseVolume = _goalCrowdNoiseVolume
            };
            var json = JsonUtility.ToJson(info, true);
            var infoPath = Path.Combine(OutputDir, "AssetInformation.json");
            File.WriteAllText(infoPath, json);

            Debug.Log($"[SceneryBundle] Built '{bundleName}'\n" +
                      $"  Info: {infoPath}\n" +
                      $"  Prefab: {rootPrefabName}, useGlass={_useGlass}\n" +
                      $"  Ambient: custom={_useCustomAmbient} mult={_ambientMultiplier}\n" +
                      $"  GameDir: use={_useGameDirectional} intensity={(_overrideGameDirIntensity ? _gameDirIntensity.ToString("F2") : "(default)")} strength={(_overrideGameDirShadowStrength ? _gameDirShadowStrength.ToString("F2") : "(default)")}\n" +
                      $"  PropsCastShadows: {_propsCastShadows}\n" +
                      $"  KeepReflections: {_keepReflections}\n" +
                      $"  KeepGameLights: {_keepGameLights}\n" +
                      $"  Music: enabled={_musicEnabled} file='{musicFileName}' vol={_musicVolume}\n" +
                      $"  AmbientAudio: enabled={_ambientAudioEnabled} file='{ambientAudioFileName}' vol={_ambientAudioVolume}\n" +
                      $"  GoalCrowdNoise: vol={_goalCrowdNoiseVolume}");

            EditorUtility.RevealInFinder(OutputDir);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Build failed", ex.Message, "OK");
        }
    }

    static string CopyAudioFile(bool enabled, string sourcePath, string label)
    {
        if (!enabled || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return "";
        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(OutputDir, fileName);
        File.Copy(sourcePath, destPath, true);
        Debug.Log($"[SceneryBundle] Copied {label}: {sourcePath} -> {destPath}");
        return fileName;
    }
}
#endif
