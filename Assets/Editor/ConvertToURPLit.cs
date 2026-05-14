using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class ConvertToURPLit
{
    static readonly string[] SearchRoots = { "Assets/TirgamesAssets" };

    static readonly string[] ColorSuffixes =
    {
        "Red", "Purple", "Biege", "Black", "Glass", "White", "Blue", "Green",
        "Yellow", "Orange", "Pink", "Grey", "Gray", "Brown", "Gold", "Silver",
        "CheckIn", "Mat", "Emissive"
    };

    [MenuItem("Tools/Convert Standard Shaders to URP Lit")]
    static void ConvertAll()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            EditorUtility.DisplayDialog("Error",
                "Could not find 'Universal Render Pipeline/Lit' shader.\n" +
                "Make sure URP is installed.", "OK");
            return;
        }

        int converted = 0, texturesFixed = 0, skipped = 0, alreadyURP = 0;

        foreach (var root in SearchRoots)
        {
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning($"[ConvertURP] Folder not found: {root}");
                continue;
            }

            var packDirs = AssetDatabase.GetSubFolders(root);

            foreach (var packDir in packDirs)
            {
                var textureMap = BuildTextureMap(packDir);
                var matGuids = AssetDatabase.FindAssets("t:Material", new[] { packDir });

                foreach (var guid in matGuids)
                {
                    var matPath = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null) continue;

                    string shaderName = mat.shader.name;
                    bool needsConvert = shaderName == "Standard" ||
                                        shaderName == "Tirgames/TirgamesStandard" ||
                                        shaderName == "Hidden/InternalErrorShader";
                    bool isURP = shaderName.Contains("Universal Render Pipeline");

                    if (!needsConvert && !isURP)
                    {
                        skipped++;
                        continue;
                    }

                    if (needsConvert)
                    {
                        ConvertShader(mat, urpLit, shaderName);
                        converted++;
                    }
                    else
                    {
                        alreadyURP++;
                    }

                    if (mat.HasProperty("_BaseMap"))
                    {
                        int filled = FillMissingTextures(mat, textureMap);
                        texturesFixed += filled;
                    }

                    EditorUtility.SetDirty(mat);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary =
            $"Converted {converted} materials to URP Lit\n" +
            $"Fixed {texturesFixed} missing texture slots\n" +
            $"Already URP: {alreadyURP}\n" +
            $"Skipped (other shaders): {skipped}\n\n" +
            "Check Console for per-material details.";

        Debug.Log($"[ConvertURP] Done! {summary}");
        EditorUtility.DisplayDialog("Convert to URP Lit", summary, "OK");
    }

    static void ConvertShader(Material mat, Shader urpLit, string oldShaderName)
    {
        Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
        Texture metallicMap = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
        Texture aoMap = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
        Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;

        Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
        float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
        float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
        float smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
        float aoStrength = mat.HasProperty("_OcclusionStrength") ? mat.GetFloat("_OcclusionStrength") : 1f;
        float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;
        bool hadEmission = mat.IsKeywordEnabled("_EMISSION");

        mat.shader = urpLit;

        if (mainTex != null)
        {
            mat.SetTexture("_BaseMap", mainTex);
            mat.SetTexture("_MainTex", mainTex);
        }

        mat.SetColor("_BaseColor", color);

        if (bumpMap != null)
        {
            mat.SetTexture("_BumpMap", bumpMap);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_BumpScale", bumpScale);

        if (metallicMap != null)
        {
            mat.SetTexture("_MetallicGlossMap", metallicMap);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);

        if (aoMap != null)
        {
            mat.SetTexture("_OcclusionMap", aoMap);
            mat.EnableKeyword("_OCCLUSIONMAP");
        }
        mat.SetFloat("_OcclusionStrength", aoStrength);

        if (emissionMap != null)
        {
            mat.SetTexture("_EmissionMap", emissionMap);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        mat.SetColor("_EmissionColor", emissionColor);

        if (hadEmission || emissionColor != Color.black)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        // Transparency
        if (mode > 0)
        {
            mat.SetFloat("_Surface", 1);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
        }

        Debug.Log($"[ConvertURP] Converted: {mat.name} (was {oldShaderName})");
    }

    static int FillMissingTextures(Material mat, Dictionary<string, Texture2D> textureMap)
    {
        int count = 0;
        string baseName = ResolveBaseName(mat.name, textureMap);

        if (mat.GetTexture("_BaseMap") == null)
        {
            var tex = FindTexture(textureMap, baseName, "");
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTexture("_MainTex", tex);
                count++;
                Debug.Log($"[ConvertURP]   {mat.name}: base map -> {tex.name}");
            }
        }

        if (mat.GetTexture("_BumpMap") == null)
        {
            var tex = FindTexture(textureMap, baseName, "_NM");
            if (tex != null)
            {
                mat.SetTexture("_BumpMap", tex);
                mat.EnableKeyword("_NORMALMAP");
                count++;
                Debug.Log($"[ConvertURP]   {mat.name}: normal -> {tex.name}");
            }
        }

        if (mat.GetTexture("_MetallicGlossMap") == null)
        {
            var tex = FindTexture(textureMap, baseName, "_Metallic");
            if (tex != null)
            {
                mat.SetTexture("_MetallicGlossMap", tex);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                count++;
                Debug.Log($"[ConvertURP]   {mat.name}: metallic -> {tex.name}");
            }
        }

        if (mat.GetTexture("_OcclusionMap") == null)
        {
            var tex = FindTexture(textureMap, baseName, "_AO");
            if (tex == null)
                tex = FindTexture(textureMap, baseName, "_A0"); // zero instead of O
            if (tex != null)
            {
                mat.SetTexture("_OcclusionMap", tex);
                mat.EnableKeyword("_OCCLUSIONMAP");
                count++;
                Debug.Log($"[ConvertURP]   {mat.name}: AO -> {tex.name}");
            }
        }

        if (mat.GetTexture("_EmissionMap") == null)
        {
            var tex = FindTexture(textureMap, baseName, "_Emission");
            if (tex == null)
                tex = FindTexture(textureMap, baseName, "_Emmision"); // typo variant
            if (tex != null)
            {
                mat.SetTexture("_EmissionMap", tex);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                if (mat.GetColor("_EmissionColor") == Color.black)
                    mat.SetColor("_EmissionColor", Color.white);
                count++;
                Debug.Log($"[ConvertURP]   {mat.name}: emission -> {tex.name}");
            }
        }

        return count;
    }

    static string ResolveBaseName(string matName, Dictionary<string, Texture2D> textureMap)
    {
        if (textureMap.ContainsKey(matName))
            return matName;

        // Strip numeric suffixes first (" 1", "_1", etc.)
        string stripped = matName;
        string[] numSuffixes = { " 1", " 2", " 3", "_1", "_2", "_3" };
        foreach (var s in numSuffixes)
        {
            if (stripped.EndsWith(s))
            {
                stripped = stripped.Substring(0, stripped.Length - s.Length);
                if (textureMap.ContainsKey(stripped))
                    return stripped;
                break;
            }
        }

        // Strip color/variant suffixes (try with and without underscore)
        foreach (var s in ColorSuffixes)
        {
            // "Bar01RackRed" -> try "Bar01Rack"
            if (matName.EndsWith(s))
            {
                string candidate = matName.Substring(0, matName.Length - s.Length);
                if (textureMap.ContainsKey(candidate))
                    return candidate;

                // "Bar01_Biege" -> "Bar01_" -> try "Bar01"
                if (candidate.EndsWith("_"))
                {
                    candidate = candidate.Substring(0, candidate.Length - 1);
                    if (textureMap.ContainsKey(candidate))
                        return candidate;
                }
            }
        }

        return matName;
    }

    static Dictionary<string, Texture2D> BuildTextureMap(string packDir)
    {
        var map = new Dictionary<string, Texture2D>();
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { packDir });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null && !map.ContainsKey(tex.name))
                map[tex.name] = tex;
        }
        return map;
    }

    static Texture2D FindTexture(Dictionary<string, Texture2D> map, string baseName, string suffix)
    {
        string key = baseName + suffix;
        map.TryGetValue(key, out var tex);
        return tex;
    }
}
