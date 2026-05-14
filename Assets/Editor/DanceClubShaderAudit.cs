using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DanceClubShaderAudit
{
    const string PrefabPath = "Assets/DanceClubFinal.prefab";
    const string ReportPath = "Assets/Editor/DanceClubShaderAudit.csv";

    // Material keywords that URP/Lit and similar shaders toggle based on which texture slots are filled.
    // If a keyword is enabled but its expected texture slot is null (and the related color is default),
    // it's a "suspicious" keyword that's compiling a variant for no visual benefit.
    static readonly (string keyword, string[] textureSlots, string colorProp, Color defaultColor)[] FeatureKeywords =
    {
        ("_NORMALMAP",                new[] { "_BumpMap", "_NormalMap" },           null,             default),
        ("_EMISSION",                 new[] { "_EmissionMap" },                     "_EmissionColor", Color.black),
        ("_METALLICSPECGLOSSMAP",     new[] { "_MetallicGlossMap", "_SpecGlossMap" }, null,           default),
        ("_OCCLUSIONMAP",             new[] { "_OcclusionMap" },                    null,             default),
        ("_PARALLAXMAP",              new[] { "_ParallaxMap" },                     null,             default),
        ("_DETAIL_MULX2",             new[] { "_DetailAlbedoMap", "_DetailNormalMap" }, null,         default),
        ("_DETAIL_SCALED",            new[] { "_DetailAlbedoMap", "_DetailNormalMap" }, null,         default),
        ("_CLEARCOATMAP",             new[] { "_ClearCoatMap" },                    null,             default),
    };

    [MenuItem("Tools/Optimize/Audit DanceClub Shader Variants")]
    static void Audit()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError($"Prefab not found at {PrefabPath}"); return; }

        var allMats = new HashSet<Material>();
        var particleMats = new HashSet<Material>();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            bool isParticle = r is ParticleSystemRenderer;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                allMats.Add(m);
                if (isParticle) particleMats.Add(m);
            }
        }

        // ----- Aggregations -----
        var byShader = new Dictionary<Shader, List<Material>>();
        var byCombo  = new Dictionary<string, (Shader shader, string keywords, List<Material> mats)>();
        var suspicious = new List<(Material mat, string keyword, string reason)>();

        foreach (var mat in allMats)
        {
            if (mat.shader == null) continue;

            if (!byShader.TryGetValue(mat.shader, out var matList))
                byShader[mat.shader] = matList = new List<Material>();
            matList.Add(mat);

            // Unity 6: prefer enabledKeywords (LocalKeyword[]) over the legacy shaderKeywords
            // string array, which can return stale data after programmatic DisableKeyword calls.
            var kws = (mat.enabledKeywords ?? new UnityEngine.Rendering.LocalKeyword[0])
                .Select(k => k.name)
                .Where(k => !string.IsNullOrEmpty(k))
                .OrderBy(k => k)
                .ToArray();
            string kwJoined = string.Join(" ", kws);
            string comboKey = mat.shader.name + "|" + kwJoined;

            if (!byCombo.TryGetValue(comboKey, out var entry))
                byCombo[comboKey] = entry = (mat.shader, kwJoined, new List<Material>());
            entry.mats.Add(mat);

            // Suspicious keyword detection
            foreach (var fk in FeatureKeywords)
            {
                if (System.Array.IndexOf(kws, fk.keyword) < 0) continue;

                bool hasTexture = false;
                foreach (var slot in fk.textureSlots)
                {
                    if (mat.HasProperty(slot) && mat.GetTexture(slot) != null) { hasTexture = true; break; }
                }
                if (hasTexture) continue;

                bool hasNonDefaultColor = false;
                if (fk.colorProp != null && mat.HasProperty(fk.colorProp))
                {
                    var c = mat.GetColor(fk.colorProp);
                    if (c != fk.defaultColor && c.maxColorComponent > 0.01f) hasNonDefaultColor = true;
                }
                if (hasNonDefaultColor) continue;

                suspicious.Add((mat, fk.keyword,
                    $"keyword on but slots {string.Join("/", fk.textureSlots)} are null" +
                    (fk.colorProp != null ? $" and {fk.colorProp} is default" : "")));
            }
        }

        // ----- Write CSV -----
        var sb = new StringBuilder();
        sb.AppendLine("# Section 1: Unique shaders, sorted by material count");
        sb.AppendLine("shader,materialCount,uniqueKeywordCombos,isParticleShader,path");
        var section1 = byShader
            .Select(kv => new {
                Shader = kv.Key,
                Mats = kv.Value,
                Combos = byCombo.Values.Count(c => c.shader == kv.Key),
                IsParticle = kv.Value.Any(m => particleMats.Contains(m)),
            })
            .OrderByDescending(x => x.Mats.Count);
        foreach (var s in section1)
            sb.AppendLine($"{Esc(s.Shader.name)},{s.Mats.Count},{s.Combos},{(s.IsParticle ? "yes" : "")},{Esc(AssetDatabase.GetAssetPath(s.Shader))}");

        sb.AppendLine();
        sb.AppendLine("# Section 2: Unique (shader, keyword-set) combos, sorted by material count");
        sb.AppendLine("shader,keywords,materialCount,exampleMaterial");
        var section2 = byCombo.Values.OrderByDescending(c => c.mats.Count);
        foreach (var c in section2)
            sb.AppendLine($"{Esc(c.shader.name)},{Esc(c.keywords)},{c.mats.Count},{Esc(c.mats[0].name)}");

        sb.AppendLine();
        sb.AppendLine("# Section 3: Suspicious keywords (enabled but no texture/color to back them up)");
        sb.AppendLine("material,keyword,reason,materialPath");
        foreach (var s in suspicious.OrderBy(s => s.mat.name))
            sb.AppendLine($"{Esc(s.mat.name)},{Esc(s.keyword)},{Esc(s.reason)},{Esc(AssetDatabase.GetAssetPath(s.mat))}");

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
        AssetDatabase.ImportAsset(ReportPath);

        // ----- Console summary -----
        Debug.Log(
            $"[ShaderAudit] Materials referenced by {PrefabPath}: {allMats.Count} ({particleMats.Count} on particle renderers).\n" +
            $"  Unique shaders: {byShader.Count}\n" +
            $"  Unique (shader, keyword-set) combos: {byCombo.Count}\n" +
            $"  Suspicious feature-keyword usages: {suspicious.Count}\n" +
            $"  Report: {ReportPath}\n\n" +
            $"Top 5 shaders by material count:\n" +
            string.Join("\n", section1.Take(5).Select(s => $"  {s.Mats.Count,3}x  {s.Shader.name}  ({s.Combos} combos)")) +
            $"\n\nTop 5 combos by material count:\n" +
            string.Join("\n", section2.Take(5).Select(c => $"  {c.mats.Count,3}x  {c.shader.name}  [{c.keywords}]"))
        );

        EditorUtility.RevealInFinder(ReportPath);
    }

    static string Esc(string s)
    {
        if (s == null) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
