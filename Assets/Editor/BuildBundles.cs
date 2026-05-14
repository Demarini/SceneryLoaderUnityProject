#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildAndEncryptAllBundles
{
    const string OutputDir = "Build/AssetBundles";
    const BuildTarget Target = BuildTarget.StandaloneWindows64;
    const BuildAssetBundleOptions ABO =
        BuildAssetBundleOptions.ChunkBasedCompression |
        BuildAssetBundleOptions.StrictMode;

    [MenuItem("Tools/Bundles/Build & Encrypt ALL Bundles (no UI)")]
    public static void BuildAndEncryptAll_Menu()
    {
        BuildAndEncryptAll_ReturnOutputDir();
    }

    // Called by the window; returns the folder where bundles + .abx live
    public static string BuildAndEncryptAll_ReturnOutputDir()
    {
        Directory.CreateDirectory(OutputDir);

        var manifest = BuildPipeline.BuildAssetBundles(OutputDir, ABO, Target);
        if (manifest == null)
        {
            throw new InvalidOperationException("BuildPipeline.BuildAssetBundles returned null.");
        }

        var all = manifest.GetAllAssetBundles() ?? Array.Empty<string>();
        if (all.Length == 0)
        {
            Debug.LogWarning("[Bundles] No asset bundles found to encrypt.");
            return Path.GetFullPath(OutputDir);
        }

        foreach (var bundleName in all)
        {
            var bundlePath = Path.Combine(OutputDir, bundleName);
            if (!File.Exists(bundlePath))
            {
                Debug.LogWarning($"[Encrypt] Missing bundle file: {bundlePath}");
                continue;
            }

            try
            {
                var raw = File.ReadAllBytes(bundlePath);

                // Generate a per-bundle key (you can also pull this from a server later)
                var contentKey = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(contentKey);

                DeriveKeys(contentKey, out var encKey, out var macKey);

                // AES-CBC encrypt raw -> .abx
                byte[] iv, cipher;
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey;
                    aes.GenerateIV(); iv = aes.IV;
                    using (var enc = aes.CreateEncryptor())
                        cipher = enc.TransformFinalBlock(raw, 0, raw.Length);
                }

                // ABX container: magic + version + iv + len + cipher + mac
                var header = new MemoryStream();
                using (var bw = new BinaryWriter(header, Encoding.UTF8, true))
                {
                    bw.Write(Encoding.ASCII.GetBytes("ABX1"));
                    bw.Write((byte)1);
                    bw.Write(iv);
                    bw.Write(cipher.Length);
                }
                var headerBytes = header.ToArray();

                byte[] mac;
                using (var h = new HMACSHA256(macKey))
                {
                    h.TransformBlock(headerBytes, 0, headerBytes.Length, null, 0);
                    h.TransformFinalBlock(cipher, 0, cipher.Length);
                    mac = h.Hash;
                }

                var abxName = Path.GetFileNameWithoutExtension(bundleName) + ".abx";
                var abxPath = Path.Combine(OutputDir, abxName);
                using (var fs = File.Create(abxPath))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(headerBytes);
                    bw.Write(cipher);
                    bw.Write(mac);
                }

                Debug.Log($"[Encrypt] {bundleName} -> {abxName} ({new FileInfo(abxPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        return Path.GetFullPath(OutputDir);
    }

    static void DeriveKeys(byte[] contentKey32, out byte[] encKey32, out byte[] macKey32)
    {
        if (contentKey32 == null || contentKey32.Length != 32)
            throw new ArgumentException("ContentKey must be 32 bytes");
        using (var sha = SHA256.Create())
        {
            encKey32 = sha.ComputeHash(Concat(contentKey32, Encoding.ASCII.GetBytes("enc")));
            macKey32 = sha.ComputeHash(Concat(contentKey32, Encoding.ASCII.GetBytes("mac")));
        }
    }

    static byte[] Concat(params byte[][] arrs)
    {
        int len = arrs.Sum(a => a.Length);
        var buf = new byte[len]; int o = 0;
        foreach (var a in arrs) { Buffer.BlockCopy(a, 0, buf, o, a.Length); o += a.Length; }
        return buf;
    }
}
#endif
