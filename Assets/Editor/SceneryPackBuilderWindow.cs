#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using System.Linq;

public sealed class SceneryPackBuilderWindow : EditorWindow
{
    // Defaults for convenience
    bool useGlass = true;

    [MenuItem("Tools/Bundles/Build, Encrypt + Write Info�")]
    public static void Open()
    {
        var w = GetWindow<SceneryPackBuilderWindow>("Scenery Pack");
        w.minSize = new Vector2(360, 140);
        w.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("Scenery Pack Options", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        useGlass = EditorGUILayout.Toggle(new GUIContent("Use Glass", "Keep rink glass in the hangar"), useGlass);

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Build & Encrypt ALL Bundles + Write AssetInformation.json", GUILayout.Height(32)))
            {
                BuildAllWithInfo(useGlass);
            }
        }

        EditorGUILayout.HelpBox(
            "Writes AssetInformation.json next to the built bundles (in the same AssetBundles folder). " +
            "If you also encrypt, both raw and .abx will sit next to AssetInformation.json.", MessageType.Info);
    }

    static void BuildAllWithInfo(bool useGlass)
    {
        try
        {
            // Reuse your existing builder; expose a method that returns the output folder
            string outputDir = BuildAndEncryptAllBundles.BuildAndEncryptAll_ReturnOutputDir();

            // Write the info file next to the bundles
            var info = new AssetInformation { useGlass = useGlass };
            string json = JsonUtility.ToJson(info, prettyPrint: true);
            string infoPath = Path.Combine(outputDir, "AssetInformation.json");
            File.WriteAllText(infoPath, json);

            Debug.Log($"[SceneryPack] Wrote {infoPath}\n{json}");
            EditorUtility.RevealInFinder(outputDir);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Build failed", ex.Message, "OK");
        }
    }
}
[Serializable]
public class AssetInformation
{
    public bool useGlass = true;

    // Overall Puck lighting — multiplies the game's ambient light to dim/brighten the whole scene.
    public bool useCustomAmbient = false;
    public float ambientMultiplier = 1f;

    // Use the game's default directional light. It already has the right culling/bias to put
    // a shadow under the player stick. When false, the game directional is disabled (the bundle
    // is then responsible for providing its own shadow caster named "MainSun" if it wants one).
    public bool useGameDirectional = true;

    // -1 = leave the game's intensity alone (typically 0.5). Set low (e.g. 0.05–0.15) for moody
    // bundles like a dance club so the directional barely lights but still casts a shadow.
    public float gameDirectionalIntensity = -1f;

    // -1 = leave the game's shadow strength alone. 0 = no shadow, 1 = fully dark.
    public float gameDirectionalShadowStrength = -1f;

    // When false (default), every Renderer inside the bundle gets shadowCastingMode=Off. The
    // ice still RECEIVES the stick's shadow, but bundle props aren't drawn into the shadow map
    // — big perf win for dense bundles. Set true if a prop should cast a real shadow.
    public bool propsCastShadows = false;

    // When false (default), the game's hangar reflection probe is disabled — its baked cubemap
    // shows the original ceiling lights/rafters which look out of place in most custom scenes.
    // Bundle-authored probes inside the staged root stay alive either way. Set true if you want
    // the vanilla glossy-ice look without baking your own probe (arena-style replacements).
    public bool keepReflections = false;

    // When false (default), the game's non-directional point/spot lights are disabled. When
    // true, they stay alive — restoring the small bright specular dots on the ice from the
    // vanilla arena. Leave off for moody bundles; turn on for arena-style replacements.
    public bool keepGameLights = false;

    public bool musicEnabled = false;
    public string musicPath = "";
    public float musicVolume = 0.5f;

    public bool ambientAudioEnabled = false;
    public string ambientAudioPath = "";
    public float ambientAudioVolume = 0.3f;

    public float goalCrowdNoiseVolume = 0.37f;
}
#endif
