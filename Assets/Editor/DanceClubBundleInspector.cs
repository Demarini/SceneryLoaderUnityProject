using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DanceClubBundleInspector
{
    const string BundleName = "danceclub";
    const string ReportPath = "Assets/Editor/DanceClubBundleContents.csv";

    [MenuItem("Tools/Optimize/Dump DanceClub Bundle Contents")]
    static void Dump()
    {
        var rootAssets = AssetDatabase.GetAssetPathsFromAssetBundle(BundleName);
        if (rootAssets == null || rootAssets.Length == 0)
        {
            Debug.LogError($"No assets tagged with bundle '{BundleName}'.");
            return;
        }

        // Transitive deps of every tagged asset, plus the roots themselves.
        var all = new HashSet<string>(rootAssets, System.StringComparer.OrdinalIgnoreCase);
        foreach (var root in rootAssets)
            foreach (var dep in AssetDatabase.GetDependencies(root, true))
                all.Add(dep);

        var rows = new List<(string path, long size, string ext, string typeName, bool isRoot)>();
        foreach (var path in all)
        {
            if (path.EndsWith(".cs") || path.EndsWith(".shader") || path.EndsWith(".hlsl") || path.EndsWith(".cginc"))
                continue; // code/shader source is built-in/tiny — skip noise
            var full = Path.GetFullPath(path);
            long size = File.Exists(full) ? new FileInfo(full).Length : 0;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            var t = AssetDatabase.GetMainAssetTypeAtPath(path);
            string typeName = t != null ? t.Name : "?";
            bool isRoot = System.Array.IndexOf(rootAssets, path) >= 0;
            rows.Add((path, size, ext, typeName, isRoot));
        }

        // Write full CSV
        var csv = new StringBuilder();
        csv.AppendLine("sizeMB,sizeBytes,type,ext,isRoot,path");
        foreach (var r in rows.OrderByDescending(r => r.size))
            csv.AppendLine($"{(r.size/1048576.0):F3},{r.size},{r.typeName},{r.ext},{(r.isRoot?"yes":"")},{Esc(r.path)}");
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, csv.ToString(), Encoding.UTF8);
        AssetDatabase.ImportAsset(ReportPath);

        // Console summary
        long total = rows.Sum(r => r.size);
        Debug.Log($"[BundleInspector] {rootAssets.Length} root asset(s) tagged '{BundleName}'. " +
                  $"{rows.Count} total files in dep tree, {total/1048576.0:F2} MB on disk.\n" +
                  $"(Bundle on disk will be smaller — Unity strips source-only data + compresses.)");

        Debug.Log("[BundleInspector] Top 20 by size:\n" + string.Join("\n",
            rows.OrderByDescending(r => r.size).Take(20)
                .Select(r => $"  {r.size/1048576.0,7:F2} MB  {r.typeName,-22}  {r.path}")));

        // Group by type
        var byType = rows.GroupBy(r => r.typeName)
                         .Select(g => new { Type = g.Key, Count = g.Count(), MB = g.Sum(x => x.size) / 1048576.0 })
                         .OrderByDescending(x => x.MB);
        Debug.Log("[BundleInspector] By type:\n" + string.Join("\n",
            byType.Select(x => $"  {x.MB,7:F2} MB  {x.Count,4}x  {x.Type}")));

        // Group by top-level folder under Assets/
        var byFolder = rows.GroupBy(r =>
                       {
                           var parts = r.path.Split('/');
                           return parts.Length > 2 ? parts[1] : "(root)";
                       })
                       .Select(g => new { Folder = g.Key, Count = g.Count(), MB = g.Sum(x => x.size) / 1048576.0 })
                       .OrderByDescending(x => x.MB);
        Debug.Log("[BundleInspector] By top-level folder under Assets/:\n" + string.Join("\n",
            byFolder.Select(x => $"  {x.MB,7:F2} MB  {x.Count,4}x  Assets/{x.Folder}")));

        EditorUtility.RevealInFinder(ReportPath);
    }

    static string Esc(string s)
    {
        if (s == null) return "";
        if (s.Contains(",") || s.Contains("\"")) return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
