using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ResizeDanceClubTextures
{
    const string PrefabPath = "Assets/DanceClubFinal.prefab";
    const string ReportPath = "Assets/Editor/DanceClubTextureAudit.csv";
    const string BackupPath = "Assets/Editor/DanceClubTextureBackup.json";

    enum Role { Albedo, Normal, Metallic, AO, Emission, Mask, Other }

    static Dictionary<Role, int> MakeTargets(int albedoEmission, int nonAlbedo) => new Dictionary<Role, int>
    {
        { Role.Albedo,   albedoEmission },
        { Role.Emission, albedoEmission },
        { Role.Normal,   nonAlbedo },
        { Role.Metallic, nonAlbedo },
        { Role.AO,       nonAlbedo },
        { Role.Mask,     nonAlbedo },
        { Role.Other,    albedoEmission }, // unknown property → treat like color, don't accidentally crush
    };

    // Presets — pick whichever you want from the menu
    static readonly Dictionary<Role, int> TargetsDefault    = MakeTargets(1024, 512);
    static readonly Dictionary<Role, int> TargetsAggressive = MakeTargets(512, 256);
    static readonly Dictionary<Role, int> TargetsExtreme    = MakeTargets(256, 128);

    static Dictionary<Role, int> currentTargets = TargetsDefault;

    [Serializable]
    class BackupEntry { public string path; public int original; }
    [Serializable]
    class BackupFile { public List<BackupEntry> entries = new List<BackupEntry>(); }

    [MenuItem("Tools/Optimize/Audit DanceClub Textures (report only)")]
    static void AuditOnly() { currentTargets = TargetsDefault; Run(apply: false); }

    [MenuItem("Tools/Optimize/Resize - Default (1024 / 512)")]
    static void ApplyDefault() { currentTargets = TargetsDefault; Run(apply: true); }

    [MenuItem("Tools/Optimize/Resize - Aggressive (512 / 256)")]
    static void ApplyAggressive() { currentTargets = TargetsAggressive; Run(apply: true); }

    [MenuItem("Tools/Optimize/Resize - Extreme (256 / 128)")]
    static void ApplyExtreme() { currentTargets = TargetsExtreme; Run(apply: true); }

    [MenuItem("Tools/Optimize/Revert DanceClub Texture Sizes")]
    static void Revert()
    {
        if (!File.Exists(BackupPath))
        {
            EditorUtility.DisplayDialog("Revert", "No backup file found at " + BackupPath, "OK");
            return;
        }
        var backup = JsonUtility.FromJson<BackupFile>(File.ReadAllText(BackupPath));
        int restored = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var e in backup.entries)
            {
                var ti = AssetImporter.GetAtPath(e.path) as TextureImporter;
                if (ti == null) continue;
                if (ti.maxTextureSize != e.original)
                {
                    ti.maxTextureSize = e.original;
                    ti.SaveAndReimport();
                    restored++;
                }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log($"[ResizeDanceClubTextures] Reverted {restored} textures from {backup.entries.Count} backup entries.");
    }

    static void Run(bool apply)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[ResizeDanceClubTextures] Prefab not found at {PrefabPath}");
            return;
        }

        // Collect all materials from every Renderer in the prefab
        var materials = new HashSet<Material>();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
                if (m != null) materials.Add(m);
        }

        // For each texture, aggregate the *largest* target among all roles it appears as
        // (so a texture used as both Albedo and Normal is treated as the larger Albedo target).
        var perTextureRole = new Dictionary<Texture, Role>();
        var perTextureMats = new Dictionary<Texture, HashSet<string>>();

        foreach (var mat in materials)
        {
            var propNames = mat.GetTexturePropertyNames();
            foreach (var prop in propNames)
            {
                var tex = mat.GetTexture(prop);
                if (tex == null) continue;
                var role = ClassifyByProperty(prop);
                if (role == Role.Other) role = ClassifyByName(tex.name);

                if (!perTextureRole.TryGetValue(tex, out var existing) || currentTargets[role] > currentTargets[existing])
                    perTextureRole[tex] = role;

                if (!perTextureMats.TryGetValue(tex, out var matSet))
                    perTextureMats[tex] = matSet = new HashSet<string>();
                matSet.Add(mat.name);
            }
        }

        // Build report rows
        var rows = new List<string>
        {
            "path,name,role,currentMax,targetMax,willChange,materials"
        };
        var toChange = new List<(string path, int original, int target)>();
        long savedPx2 = 0;

        foreach (var kvp in perTextureRole.OrderBy(k => k.Key.name))
        {
            var tex = kvp.Key;
            var role = kvp.Value;
            var path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) continue;
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;

            int current = ti.maxTextureSize;
            int target = currentTargets[role];
            bool willChange = target < current;

            rows.Add(string.Join(",",
                Escape(path), Escape(tex.name), role.ToString(),
                current.ToString(), target.ToString(),
                willChange ? "yes" : "no",
                Escape(string.Join("|", perTextureMats[tex]))));

            if (willChange)
            {
                toChange.Add((path, current, target));
                savedPx2 += (long)(current * current) - (long)(target * target);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllLines(ReportPath, rows, Encoding.UTF8);
        AssetDatabase.ImportAsset(ReportPath);

        Debug.Log($"[ResizeDanceClubTextures] Audited {perTextureRole.Count} textures referenced by {PrefabPath}. " +
                  $"{toChange.Count} would be reduced. Report: {ReportPath}");

        if (!apply)
        {
            EditorUtility.RevealInFinder(ReportPath);
            return;
        }

        if (toChange.Count == 0)
        {
            Debug.Log("[ResizeDanceClubTextures] Nothing to do — every texture is already at or below target.");
            return;
        }

        int albedoCap = currentTargets[Role.Albedo];
        int otherCap = currentTargets[Role.Normal];
        if (!EditorUtility.DisplayDialog(
                "Resize textures?",
                $"About to reduce maxTextureSize on {toChange.Count} textures referenced by {Path.GetFileName(PrefabPath)}.\n\n" +
                $"Targets:\n  Albedo/Emission ≤ {albedoCap}\n  Normal/Metallic/AO/Mask ≤ {otherCap}\n\n" +
                $"Originals are saved to {BackupPath} (run 'Revert' menu to undo).",
                "Apply", "Cancel"))
            return;

        // Save backup (only entries we are actually changing)
        var backup = new BackupFile();
        foreach (var c in toChange)
            backup.entries.Add(new BackupEntry { path = c.path, original = c.original });
        File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
        AssetDatabase.ImportAsset(BackupPath);

        int changed = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < toChange.Count; i++)
            {
                var c = toChange[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Resizing textures", c.path, (float)i / toChange.Count))
                    break;

                var ti = AssetImporter.GetAtPath(c.path) as TextureImporter;
                if (ti == null) continue;
                ti.maxTextureSize = c.target;
                ti.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
        }

        Debug.Log($"[ResizeDanceClubTextures] Resized {changed} textures. Backup at {BackupPath}.");
    }

    static Role ClassifyByProperty(string prop)
    {
        switch (prop)
        {
            case "_BaseMap":
            case "_MainTex":
            case "_BaseColorMap":
                return Role.Albedo;
            case "_BumpMap":
            case "_NormalMap":
            case "_DetailNormalMap":
                return Role.Normal;
            case "_MetallicGlossMap":
            case "_MetallicMap":
            case "_SpecGlossMap":
                return Role.Metallic;
            case "_OcclusionMap":
                return Role.AO;
            case "_EmissionMap":
                return Role.Emission;
            case "_MaskMap":
                return Role.Mask;
            default:
                return Role.Other;
        }
    }

    static Role ClassifyByName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.EndsWith("_nm") || n.EndsWith("_normal") || n.EndsWith("_n")) return Role.Normal;
        if (n.EndsWith("_ao") || n.EndsWith("_occlusion")) return Role.AO;
        if (n.EndsWith("_metallic") || n.EndsWith("_spec") || n.EndsWith("_mg")) return Role.Metallic;
        if (n.EndsWith("_emission") || n.EndsWith("_emissive") || n.EndsWith("_e")) return Role.Emission;
        if (n.EndsWith("_mask") || n.EndsWith("_msk")) return Role.Mask;
        return Role.Albedo; // sensible default for a "color-looking" texture in this project
    }

    static string Escape(string s)
    {
        if (s == null) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
