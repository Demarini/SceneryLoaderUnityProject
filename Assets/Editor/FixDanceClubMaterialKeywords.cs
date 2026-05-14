using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FixDanceClubMaterialKeywords
{
    const string PrefabPath = "Assets/DanceClubFinal.prefab";

    // Feature keywords whose presence implies a texture or non-default color; if neither
    // is set, the keyword is contributing nothing but variant noise.
    static readonly (string keyword, string[] textureSlots, string colorProp, Color defaultColor)[] FeatureKeywords =
    {
        ("_NORMALMAP",                new[] { "_BumpMap", "_NormalMap" },               null,             default),
        ("_EMISSION",                 new[] { "_EmissionMap" },                         "_EmissionColor", Color.black),
        ("_METALLICSPECGLOSSMAP",     new[] { "_MetallicGlossMap", "_SpecGlossMap" },   null,             default),
        ("_OCCLUSIONMAP",             new[] { "_OcclusionMap" },                        null,             default),
        ("_PARALLAXMAP",              new[] { "_ParallaxMap" },                         null,             default),
        ("_DETAIL_MULX2",             new[] { "_DetailAlbedoMap", "_DetailNormalMap" }, null,             default),
        ("_DETAIL_SCALED",            new[] { "_DetailAlbedoMap", "_DetailNormalMap" }, null,             default),
        ("_CLEARCOATMAP",             new[] { "_ClearCoatMap" },                        null,             default),
    };

    // Legacy/duplicate keywords URP/Lit ignores. Stripping them only removes variant noise.
    static readonly string[] LegacyKeywordsToStrip =
    {
        "_METALLICGLOSSMAP",        // Standard-shader keyword; URP/Lit reads _METALLICSPECGLOSSMAP instead.
        "_SPECGLOSSMAP",            // Standard-shader keyword; same idea.
    };

    [MenuItem("Tools/Optimize/Fix DanceClub Material Keywords")]
    static void Fix()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError($"Prefab not found at {PrefabPath}"); return; }

        var allMats = new HashSet<Material>();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null) allMats.Add(m);

        int suspiciousFixed = 0, legacyFixed = 0, materialsTouched = 0;
        var changes = new List<string>();

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var mat in allMats)
            {
                // Unity 6: enabledKeywords is the authoritative source; shaderKeywords can be stale.
                var kws = (mat.enabledKeywords ?? new UnityEngine.Rendering.LocalKeyword[0])
                    .Select(k => k.name).ToList();
                bool touched = false;

                // Strip suspicious feature keywords (no texture, no non-default color)
                foreach (var fk in FeatureKeywords)
                {
                    if (!kws.Contains(fk.keyword)) continue;

                    bool hasTexture = false;
                    foreach (var slot in fk.textureSlots)
                        if (mat.HasProperty(slot) && mat.GetTexture(slot) != null) { hasTexture = true; break; }
                    if (hasTexture) continue;

                    bool hasNonDefaultColor = false;
                    if (fk.colorProp != null && mat.HasProperty(fk.colorProp))
                    {
                        var c = mat.GetColor(fk.colorProp);
                        if (c != fk.defaultColor && c.maxColorComponent > 0.01f) hasNonDefaultColor = true;
                    }
                    if (hasNonDefaultColor) continue;

                    mat.DisableKeyword(fk.keyword);
                    kws.Remove(fk.keyword);
                    // If we're stripping _EMISSION, also lock the GI flag so Unity's material
                    // validation doesn't re-enable the keyword on next load/build.
                    if (fk.keyword == "_EMISSION")
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    suspiciousFixed++;
                    touched = true;
                    changes.Add($"{mat.name}: -{fk.keyword}");
                }

                // Strip legacy keywords unconditionally (they're never read by URP/Lit)
                foreach (var legacy in LegacyKeywordsToStrip)
                {
                    if (!kws.Contains(legacy)) continue;
                    mat.DisableKeyword(legacy);
                    kws.Remove(legacy);
                    legacyFixed++;
                    touched = true;
                    changes.Add($"{mat.name}: -{legacy} (legacy)");
                }

                if (touched)
                {
                    EditorUtility.SetDirty(mat);
                    materialsTouched++;
                }
            }
            AssetDatabase.SaveAssets();
        }
        finally { AssetDatabase.StopAssetEditing(); }

        Debug.Log(
            $"[FixKeywords] Touched {materialsTouched} materials. " +
            $"Removed {suspiciousFixed} suspicious feature keywords + {legacyFixed} legacy keywords.\n" +
            (changes.Count > 0 ? "Changes:\n  " + string.Join("\n  ", changes) : "")
        );

        Debug.Log("[FixKeywords] Re-run 'Audit DanceClub Shader Variants' to see the new combo count.");
    }
}
