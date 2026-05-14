#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public struct AbxEncryptResult
{
    public string abxPath;
    public byte[] contentKey32;   // keep server-side
}
//https://steamcommunity.com/sharedfiles/filedetails/?id=3566481557
public static class EditorAbx
{
    static byte[] DeriveKey32FromString(string s)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
    }
    public static AbxEncryptResult EncryptBundleToAbx(string builtPath, string keyInput = null)
    {
        if (!File.Exists(builtPath))
            throw new FileNotFoundException($"Bundle not found: {builtPath}");

        byte[] raw = File.ReadAllBytes(builtPath);

        // New random per-build key
        var contentKey = new byte[32];

        if (string.IsNullOrWhiteSpace(keyInput))
        {
            // Random per-build key
            contentKey = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(contentKey);
        }
        else
        {
            // Try Base64 first (manifest re-encrypt path)
            try
            {
                contentKey = Convert.FromBase64String(keyInput);

                if (contentKey.Length != 32)
                    throw new InvalidOperationException("Base64 key must decode to exactly 32 bytes.");
            }
            catch
            {
                // Fallback: treat input as passphrase
                contentKey = DeriveKey32FromString(keyInput);
            }
        }
        DeriveKeys(contentKey, out var encKey, out var macKey);

        byte[] iv, cipher;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            aes.Key = encKey; aes.GenerateIV(); iv = aes.IV;
            using (var enc = aes.CreateEncryptor())
                cipher = enc.TransformFinalBlock(raw, 0, raw.Length);
        }

        // ABX: magic+ver+iv+len+cipher+mac
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

        var dir = Path.GetDirectoryName(builtPath);
        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException("builtPath had no directory.");

        var nameNoExt = Path.GetFileNameWithoutExtension(builtPath);
        if (string.IsNullOrEmpty(nameNoExt))
            throw new InvalidOperationException("builtPath had no filename.");

        string outPath = Path.Combine(dir, nameNoExt + ".abx");
        using (var fs = File.Create(outPath))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(headerBytes);
            bw.Write(cipher);
            bw.Write(mac);
        }

        return new AbxEncryptResult { abxPath = outPath, contentKey32 = contentKey };
    }

    static void DeriveKeys(byte[] contentKey32, out byte[] encKey32, out byte[] macKey32)
    {
        using (var sha = SHA256.Create())
        {
            encKey32 = sha.ComputeHash(Concat(contentKey32, Encoding.ASCII.GetBytes("enc")));
            macKey32 = sha.ComputeHash(Concat(contentKey32, Encoding.ASCII.GetBytes("mac")));
        }
    }
    static byte[] Concat(params byte[][] arrs) { var len = arrs.Sum(a => a.Length); var buf = new byte[len]; int o = 0; foreach (var a in arrs) { Buffer.BlockCopy(a, 0, buf, o, a.Length); o += a.Length; } return buf; }
}
#endif