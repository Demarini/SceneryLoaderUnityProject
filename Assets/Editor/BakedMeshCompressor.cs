using UnityEditor;
using UnityEngine;

public static class BakedMeshCompressor
{
    const string ObjectsFolder = "Assets/BakedMeshes/Objects";
    const string PersonsFolder = "Assets/BakedMeshes/PlacedPersons";

    [MenuItem("Tools/Optimize/Compress BakedMeshes Objects (High, keep tangents)")]
    static void CompressObjects() =>
        Run(new[] { ObjectsFolder }, ModelImporterMeshCompression.High, stripTangents: false);

    [MenuItem("Tools/Optimize/Compress BakedMeshes PlacedPersons (High + strip tangents)")]
    static void CompressPersons() =>
        Run(new[] { PersonsFolder }, ModelImporterMeshCompression.High, stripTangents: true);

    [MenuItem("Tools/Optimize/Compress BakedMeshes ALL (Off / restore)")]
    static void RestoreAll() =>
        Run(new[] { ObjectsFolder, PersonsFolder }, ModelImporterMeshCompression.Off, stripTangents: false);

    static void Run(string[] folders, ModelImporterMeshCompression compression, bool stripTangents)
    {
        var guids = AssetDatabase.FindAssets("t:Mesh", folders);
        int changed = 0, stripped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".asset")) continue;
                if (EditorUtility.DisplayCancelableProgressBar("Compressing baked meshes", path, (float)i / guids.Length))
                    break;

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) continue;

                // CRITICAL SAFETY: if the mesh is non-readable, its CPU-side vertex/index data is
                // empty and any SetDirty + SaveAssets path will overwrite the .asset on disk with
                // an empty mesh, destroying the data. Skip these — they cannot be safely modified
                // in place from script. (Lesson learned the hard way on the PlacedPersons wipe.)
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[BakedMeshCompressor] Skipping non-readable mesh: {path}");
                    continue;
                }

                bool dirty = false;

                if (mesh.vertexCount < 65535 &&
                    mesh.indexFormat != UnityEngine.Rendering.IndexFormat.UInt16)
                {
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
                    dirty = true;
                }

                var so = new SerializedObject(mesh);
                var prop = so.FindProperty("m_MeshCompression");
                if (prop != null && prop.intValue != (int)compression)
                {
                    prop.intValue = (int)compression;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    dirty = true;
                }

                if (stripTangents && mesh.tangents != null && mesh.tangents.Length > 0)
                {
                    mesh.tangents = new Vector4[0];
                    stripped++;
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(mesh);
                    changed++;
                }
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
        }
        Debug.Log($"[BakedMeshCompressor] Folders=[{string.Join(", ", folders)}] " +
                  $"Updated {changed} meshes. Tangents stripped on {stripped}. Compression={compression}");
    }
}
