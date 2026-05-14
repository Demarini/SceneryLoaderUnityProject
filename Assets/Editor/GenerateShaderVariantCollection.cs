using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class GenerateShaderVariantCollection
{
    const string PrefabPath = "Assets/DanceClubFinal.prefab";
    const string OutputPath = "Assets/DanceClubShaderWarmup.shadervariants";

    [MenuItem("Tools/Optimize/Generate DanceClub Shader Variant Collection")]
    static void Generate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found at {PrefabPath}");
            return;
        }

        // Collect every Material referenced by any Renderer (incl. inactive) and ParticleSystemRenderer in the prefab.
        var materials = new HashSet<Material>();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
                if (m != null) materials.Add(m);
        }

        // Group keyword sets per shader. Unity's ShaderVariantCollection wants
        // (shader, passType, keywords[]) tuples. We can't know which exact pass each variant
        // needs (Forward vs Deferred etc.) without play-mode capture, so we add the common
        // URP passes for each (shader, keywords) combo. The cost of a few extra compiles is
        // tiny vs missing a needed variant.
        var perShader = new Dictionary<Shader, HashSet<string>>();
        foreach (var mat in materials)
        {
            if (mat.shader == null) continue;

            // Combine shader's globally-enabled keywords with material's enabled local keywords.
            var keywords = new List<string>();
            foreach (var lk in mat.shaderKeywords)
                if (!string.IsNullOrEmpty(lk)) keywords.Add(lk);
            keywords.Sort();
            string key = string.Join(" ", keywords);

            if (!perShader.TryGetValue(mat.shader, out var set))
                perShader[mat.shader] = set = new HashSet<string>();
            set.Add(key);
        }

        // Also add the common URP keyword combinations the scene's lighting setup will require.
        // These are typically enabled globally by URP based on the Renderer asset and scene
        // lights, NOT by the material itself, so they won't appear in material.shaderKeywords.
        // We add them to ALL shaders so each shader's variants get compiled with the relevant
        // lighting permutations.
        var urpLightingKeywords = new[]
        {
            "",
            "_MAIN_LIGHT_SHADOWS",
            "_MAIN_LIGHT_SHADOWS _SHADOWS_SOFT",
            "_ADDITIONAL_LIGHTS",
            "_ADDITIONAL_LIGHTS _ADDITIONAL_LIGHT_SHADOWS",
            "_ADDITIONAL_LIGHTS _ADDITIONAL_LIGHT_SHADOWS _SHADOWS_SOFT",
            "LIGHTMAP_ON",
            "LIGHTMAP_ON DIRLIGHTMAP_COMBINED",
        };

        var svc = new ShaderVariantCollection { name = System.IO.Path.GetFileNameWithoutExtension(OutputPath) };
        var passTypes = new[] { PassType.ForwardBase, PassType.ForwardAdd, PassType.Deferred, PassType.ScriptableRenderPipeline, PassType.Meta };

        int added = 0;
        foreach (var kvp in perShader)
        {
            var shader = kvp.Key;
            foreach (var materialKW in kvp.Value)
            {
                foreach (var lightingKW in urpLightingKeywords)
                {
                    var combined = string.IsNullOrEmpty(materialKW)
                        ? lightingKW
                        : (string.IsNullOrEmpty(lightingKW) ? materialKW : materialKW + " " + lightingKW);
                    var kwArray = string.IsNullOrEmpty(combined) ? new string[0] : combined.Split(' ');

                    foreach (var passType in passTypes)
                    {
                        var v = new ShaderVariantCollection.ShaderVariant();
                        try { v = new ShaderVariantCollection.ShaderVariant(shader, passType, kwArray); }
                        catch { continue; } // Not every (shader, pass, keywords) combo is valid; skip those.
                        if (svc.Add(v)) added++;
                    }
                }
            }
        }

        AssetDatabase.CreateAsset(svc, OutputPath);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(svc);

        Debug.Log($"[ShaderVariantCollection] {materials.Count} materials -> {perShader.Count} unique shaders -> {added} variants added.\n" +
                  $"Saved to {OutputPath}\n" +
                  $"Next: attach ShaderWarmupOnAwake to DanceClubFinal root, assign this SVC, and ensure both are in the 'danceclub' bundle.");
    }
}
