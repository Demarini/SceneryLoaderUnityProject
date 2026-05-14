using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class FixURPMaterialTextures : EditorWindow
{
    static readonly string MaterialsPath = "Assets/TirgamesAssets/PBRStageEquipment/Models/Materials";
    static readonly string TexturesPath = "Assets/TirgamesAssets/PBRStageEquipment/Textures";

    [MenuItem("Tools/Fix URP Material Textures (PBR Stage Equipment)")]
    static void Fix()
    {
        var textureMap = BuildTextureMap();
        var matGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsPath });
        int fixedBase = 0, fixedNormal = 0, fixedMetallic = 0, fixedAO = 0, fixedEmission = 0, skipped = 0;

        foreach (var guid in matGuids)
        {
            var matPath = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            string matName = mat.name;

            // Strip variant suffixes like " 1", "_1", "_2" to find the base texture name
            string baseName = matName;
            string[] variantSuffixes = { " 1", " 2", " 3", "_1", "_2", "_3" };
            foreach (var suffix in variantSuffixes)
            {
                if (baseName.EndsWith(suffix))
                {
                    baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                    break;
                }
            }

            bool changed = false;

            // Fix _BaseMap (albedo)
            if (!mat.HasProperty("_BaseMap"))
            {
                skipped++;
                continue;
            }

            var currentBase = mat.GetTexture("_BaseMap");
            if (currentBase == null)
            {
                Texture2D albedo = FindTexture(textureMap, baseName, "");
                if (albedo != null)
                {
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetTexture("_MainTex", albedo);
                    fixedBase++;
                    changed = true;
                    Debug.Log($"[FixURP] {matName}: Set base map to {albedo.name}");
                }
            }

            // Fix _BumpMap (normal)
            if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") == null)
            {
                Texture2D normal = FindTexture(textureMap, baseName, "_NM");
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                    fixedNormal++;
                    changed = true;
                    Debug.Log($"[FixURP] {matName}: Set normal map to {normal.name}");
                }
            }

            // Fix _MetallicGlossMap
            if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") == null)
            {
                Texture2D metallic = FindTexture(textureMap, baseName, "_Metallic");
                if (metallic != null)
                {
                    mat.SetTexture("_MetallicGlossMap", metallic);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    fixedMetallic++;
                    changed = true;
                    Debug.Log($"[FixURP] {matName}: Set metallic map to {metallic.name}");
                }
            }

            // Fix _OcclusionMap (AO)
            if (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") == null)
            {
                Texture2D ao = FindTexture(textureMap, baseName, "_AO");
                if (ao == null)
                    ao = FindTexture(textureMap, baseName, "_A0"); // typo variant (zero instead of O)
                if (ao != null)
                {
                    mat.SetTexture("_OcclusionMap", ao);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                    fixedAO++;
                    changed = true;
                    Debug.Log($"[FixURP] {matName}: Set occlusion map to {ao.name}");
                }
            }

            // Fix _EmissionMap
            if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") == null)
            {
                Texture2D emission = FindTexture(textureMap, baseName, "_Emission");
                if (emission != null)
                {
                    mat.SetTexture("_EmissionMap", emission);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    if (mat.GetColor("_EmissionColor") == Color.black)
                        mat.SetColor("_EmissionColor", Color.white);
                    fixedEmission++;
                    changed = true;
                    Debug.Log($"[FixURP] {matName}: Set emission map to {emission.name}");
                }
            }

            if (changed)
                EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[FixURP] Done! Fixed: {fixedBase} base maps, {fixedNormal} normal maps, " +
                  $"{fixedMetallic} metallic maps, {fixedAO} AO maps, {fixedEmission} emission maps. " +
                  $"Skipped {skipped} (no _BaseMap property).");

        EditorUtility.DisplayDialog("Fix URP Materials",
            $"Fixed:\n" +
            $"  {fixedBase} base/albedo maps\n" +
            $"  {fixedNormal} normal maps\n" +
            $"  {fixedMetallic} metallic maps\n" +
            $"  {fixedAO} AO maps\n" +
            $"  {fixedEmission} emission maps\n" +
            $"  {skipped} skipped (non-URP shader)\n\n" +
            $"Check the Console for details.", "OK");
    }

    static Dictionary<string, Texture2D> BuildTextureMap()
    {
        var map = new Dictionary<string, Texture2D>();
        var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesPath });
        foreach (var guid in texGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                map[tex.name] = tex;
        }
        return map;
    }

    static Texture2D FindTexture(Dictionary<string, Texture2D> map, string baseName, string suffix)
    {
        string key = baseName + suffix;
        if (map.TryGetValue(key, out var tex))
            return tex;
        return null;
    }
}
