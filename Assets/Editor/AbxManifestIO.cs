using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
[Serializable]
public class AbxManifest { public List<AbxEntry> items = new List<AbxEntry>(); }

[Serializable]
public class AbxEntry
{
    public string bundle;        // e.g., "outdoorhockey"
    public string abxFile;       // e.g., "outdoorhockey.abx"
    public string abxSha256;     // SHA256 of the .abx file
    public string contentKeyB64; // Base64 of 32-byte key
}

public static class AbxManifestIO
{
    public static void Upsert(string outputDir, string bundleName, string abxPath, byte[] key32)
    {
        string manifestPath = Path.Combine(outputDir, "abx_manifest.json");
        AbxManifest mf = Load(manifestPath);

        // compute SHA-256 of the produced .abx
        string shaHex;
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(abxPath))
            shaHex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();

        var entry = mf.items.FirstOrDefault(i => i.bundle == bundleName);
        if (entry == null)
        {
            entry = new AbxEntry();
            mf.items.Add(entry);
        }
        entry.bundle = bundleName;
        entry.abxFile = Path.GetFileName(abxPath);
        entry.abxSha256 = shaHex;
        entry.contentKeyB64 = Convert.ToBase64String(key32);

        File.WriteAllText(manifestPath, PrettyJson(mf));
        AssetDatabase.Refresh();
        Debug.Log($"[Manifest] Updated {manifestPath} for '{bundleName}'");
    }

    static AbxManifest Load(string path)
    {
        if (!File.Exists(path)) return new AbxManifest();
        try { return JsonUtility.FromJson<AbxManifest>(File.ReadAllText(path)) ?? new AbxManifest(); }
        catch { return new AbxManifest(); }
    }

    static string PrettyJson(object o) => JsonUtility.ToJson(o, true);
}
#endif
