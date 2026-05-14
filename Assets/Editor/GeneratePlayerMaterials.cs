using UnityEngine;
using UnityEditor;
using System.IO;

public class GeneratePlayerMaterials
{
    static readonly string BasePath = "Assets/PuckAssets-main/Materials";

    static readonly (string name, Color color)[] SkinColors =
    {
        ("Skin_Pale",        new Color(0.96f, 0.87f, 0.80f)),
        ("Skin_Fair",        new Color(0.92f, 0.80f, 0.69f)),
        ("Skin_Light",       new Color(0.85f, 0.72f, 0.58f)),
        ("Skin_Medium",      new Color(0.72f, 0.56f, 0.40f)),
        ("Skin_Olive",       new Color(0.62f, 0.47f, 0.33f)),
        ("Skin_Tan",         new Color(0.55f, 0.38f, 0.26f)),
        ("Skin_Brown",       new Color(0.44f, 0.30f, 0.20f)),
        ("Skin_DarkBrown",   new Color(0.33f, 0.22f, 0.15f)),
        ("Skin_Deep",        new Color(0.24f, 0.15f, 0.10f)),
    };

    static readonly (string name, Color color)[] HairColors =
    {
        ("Hair_Black",       new Color(0.07f, 0.06f, 0.06f)),
        ("Hair_DarkBrown",   new Color(0.20f, 0.13f, 0.08f)),
        ("Hair_Brown",       new Color(0.35f, 0.22f, 0.12f)),
        ("Hair_Auburn",      new Color(0.55f, 0.20f, 0.10f)),
        ("Hair_Red",         new Color(0.62f, 0.15f, 0.07f)),
        ("Hair_Blonde",      new Color(0.76f, 0.60f, 0.30f)),
        ("Hair_LightBlonde", new Color(0.90f, 0.80f, 0.50f)),
        ("Hair_Gray",        new Color(0.55f, 0.55f, 0.55f)),
        ("Hair_White",       new Color(0.88f, 0.87f, 0.85f)),
    };

    static readonly (string name, Color color)[] EyeColors =
    {
        ("Eyes_Brown",       new Color(0.36f, 0.20f, 0.09f)),
        ("Eyes_DarkBrown",   new Color(0.22f, 0.12f, 0.06f)),
        ("Eyes_Hazel",       new Color(0.45f, 0.35f, 0.15f)),
        ("Eyes_Green",       new Color(0.27f, 0.50f, 0.28f)),
        ("Eyes_Blue",        new Color(0.25f, 0.45f, 0.65f)),
        ("Eyes_LightBlue",   new Color(0.45f, 0.65f, 0.82f)),
        ("Eyes_Gray",        new Color(0.50f, 0.53f, 0.56f)),
    };

    static readonly (string name, Color color)[] ShirtColors =
    {
        ("Shirt_Red",        new Color(0.80f, 0.12f, 0.12f)),
        ("Shirt_Blue",       new Color(0.12f, 0.25f, 0.75f)),
        ("Shirt_Green",      new Color(0.10f, 0.55f, 0.20f)),
        ("Shirt_White",      new Color(0.92f, 0.92f, 0.92f)),
        ("Shirt_Black",      new Color(0.08f, 0.08f, 0.08f)),
        ("Shirt_Yellow",     new Color(0.90f, 0.78f, 0.10f)),
        ("Shirt_Orange",     new Color(0.90f, 0.45f, 0.08f)),
        ("Shirt_Purple",     new Color(0.45f, 0.15f, 0.65f)),
    };

    static readonly (string name, Color color)[] PantsColors =
    {
        ("Pants_Black",      new Color(0.08f, 0.08f, 0.08f)),
        ("Pants_Navy",       new Color(0.10f, 0.12f, 0.28f)),
        ("Pants_Gray",       new Color(0.40f, 0.40f, 0.40f)),
        ("Pants_White",      new Color(0.90f, 0.90f, 0.90f)),
        ("Pants_Brown",      new Color(0.35f, 0.22f, 0.12f)),
        ("Pants_Khaki",      new Color(0.72f, 0.65f, 0.48f)),
        ("Pants_Red",        new Color(0.70f, 0.10f, 0.10f)),
        ("Pants_Blue",       new Color(0.15f, 0.30f, 0.65f)),
    };

    [MenuItem("Tools/Generate Player Materials")]
    static void Generate()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            EditorUtility.DisplayDialog("Error",
                "Could not find 'Universal Render Pipeline/Lit' shader.\n" +
                "Make sure URP is installed.", "OK");
            return;
        }

        int count = 0;
        count += CreateMaterials(urpLit, "Skin", SkinColors, 0.1f, 0.3f);
        count += CreateMaterials(urpLit, "Hair", HairColors, 0.05f, 0.35f);
        count += CreateMaterials(urpLit, "Eyes", EyeColors, 0.2f, 0.8f);
        count += CreateMaterials(urpLit, "Shirt", ShirtColors, 0f, 0.3f);
        count += CreateMaterials(urpLit, "Pants", PantsColors, 0f, 0.25f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[GeneratePlayerMats] Created {count} materials.");
        EditorUtility.DisplayDialog("Generate Player Materials",
            $"Created {count} materials in\n{BasePath}/", "OK");
    }

    static int CreateMaterials(Shader shader, string subfolder,
        (string name, Color color)[] entries, float metallic, float smoothness)
    {
        string folder = $"{BasePath}/{subfolder}";
        EnsureFolder(folder);

        int count = 0;
        foreach (var (name, color) in entries)
        {
            string path = $"{folder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            {
                Debug.Log($"[GeneratePlayerMats] Skipped (exists): {path}");
                continue;
            }

            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);

            AssetDatabase.CreateAsset(mat, path);
            count++;
            Debug.Log($"[GeneratePlayerMats] Created: {path}");
        }

        return count;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
        string leaf = Path.GetFileName(folder);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
