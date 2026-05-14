using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class BakeCrowd : EditorWindow
{
    GameObject sourceContainer;
    int groupCount = 1;
    bool markStatic = true;
    const string BakedPrefix = "Baked_";
    const string MeshFolder = "Assets/BakedMeshes";
    const int VertexLimit = 60000;

    [MenuItem("Tools/Bake Crowd")]
    static void ShowWindow()
    {
        GetWindow<BakeCrowd>("Bake Crowd");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Combines all meshes by material into optimized meshes.");
        EditorGUILayout.Space();

        sourceContainer = (GameObject)EditorGUILayout.ObjectField("Source Container", sourceContainer, typeof(GameObject), true);
        groupCount = EditorGUILayout.IntSlider("Groups", groupCount, 1, 16);
        markStatic = EditorGUILayout.Toggle("Mark Static", markStatic);

        EditorGUI.BeginDisabledGroup(sourceContainer == null);
        if (GUILayout.Button("Bake"))
            Run();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        if (GUILayout.Button("Clear Baked"))
            ClearBaked();
    }

    void Run()
    {
        var sourceTransform = sourceContainer.transform;
        int childCount = sourceTransform.childCount;
        if (childCount == 0)
        {
            EditorUtility.DisplayDialog("Bake Crowd", "Source container has no children.", "OK");
            return;
        }

        var persons = new List<Transform>();
        for (int i = 0; i < childCount; i++)
            persons.Add(sourceTransform.GetChild(i));

        var rng = new System.Random(System.Environment.TickCount);
        for (int i = persons.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var tmp = persons[i];
            persons[i] = persons[j];
            persons[j] = tmp;
        }

        int actualGroups = Mathf.Min(groupCount, persons.Count);
        var personGroups = new List<Transform>[actualGroups];
        for (int i = 0; i < actualGroups; i++)
            personGroups[i] = new List<Transform>();
        for (int i = 0; i < persons.Count; i++)
            personGroups[i % actualGroups].Add(persons[i]);

        var existing = GameObject.Find(BakedPrefix + sourceContainer.name);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        string subfolder = $"{MeshFolder}/{sourceContainer.name}";
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder("Assets", "BakedMeshes");
        if (AssetDatabase.IsValidFolder(subfolder))
        {
            AssetDatabase.DeleteAsset(subfolder);
            AssetDatabase.Refresh();
        }
        AssetDatabase.CreateFolder(MeshFolder, sourceContainer.name);

        var container = new GameObject(BakedPrefix + sourceContainer.name);
        container.transform.position = sourceTransform.position;
        container.transform.rotation = sourceTransform.rotation;
        container.transform.localScale = sourceTransform.localScale;
        Undo.RegisterCreatedObjectUndo(container, "Bake Crowd");

        Matrix4x4 rootInverse = sourceTransform.worldToLocalMatrix;
        int totalMeshes = 0;
        int totalVerts = 0;
        int totalRenderers = 0;

        for (int g = 0; g < actualGroups; g++)
        {
            var matGroups = new Dictionary<Material, List<CombineInstance>>();

            foreach (var person in personGroups[g])
            {
                foreach (var r in person.GetComponentsInChildren<MeshRenderer>(false))
                {
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var mat = r.sharedMaterial;
                    if (mat == null) continue;

                    if (!matGroups.ContainsKey(mat))
                        matGroups[mat] = new List<CombineInstance>();

                    matGroups[mat].Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        transform = rootInverse * r.transform.localToWorldMatrix
                    });
                    totalRenderers++;
                }

                foreach (var r in person.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (r.sharedMesh == null) continue;
                    var mat = r.sharedMaterial;
                    if (mat == null) continue;

                    if (!matGroups.ContainsKey(mat))
                        matGroups[mat] = new List<CombineInstance>();

                    var baked = new Mesh();
                    r.BakeMesh(baked);

                    matGroups[mat].Add(new CombineInstance
                    {
                        mesh = baked,
                        transform = rootInverse * r.transform.localToWorldMatrix
                    });
                    totalRenderers++;
                }
            }

            GameObject groupParent = container;
            if (actualGroups > 1)
            {
                groupParent = new GameObject($"Group_{g}");
                groupParent.transform.SetParent(container.transform, false);
            }

            foreach (var kvp in matGroups)
            {
                var chunks = SplitByVertexLimit(kvp.Value);

                for (int c = 0; c < chunks.Count; c++)
                {
                    var mesh = new Mesh();
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.CombineMeshes(chunks[c].ToArray(), true, true);
                    mesh.RecalculateBounds();

                    string label = kvp.Key.name + (chunks.Count > 1 ? $"_{c}" : "");
                    mesh.name = $"g{g}_{label}";

                    string assetPath = $"{subfolder}/{mesh.name}.asset";
                    AssetDatabase.CreateAsset(mesh, assetPath);

                    var go = new GameObject(label);
                    go.transform.SetParent(groupParent.transform, false);
                    if (markStatic)
                        GameObjectUtility.SetStaticEditorFlags(go,
                            StaticEditorFlags.BatchingStatic |
                            StaticEditorFlags.OccludeeStatic |
                            StaticEditorFlags.OccluderStatic);

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kvp.Key;

                    totalMeshes++;
                    totalVerts += mesh.vertexCount;
                }
            }
        }

        AssetDatabase.SaveAssets();
        sourceContainer.SetActive(false);

        Debug.Log($"[BakeCrowd] {totalRenderers} renderers -> {totalMeshes} meshes across {actualGroups} groups ({totalVerts} verts).");
        EditorUtility.DisplayDialog("Bake Crowd",
            $"Baked {totalRenderers} renderers into {totalMeshes} meshes across {actualGroups} groups ({totalVerts:N0} verts).\n\nMeshes saved to {subfolder}\nSource container has been deactivated.", "OK");
    }

    List<List<CombineInstance>> SplitByVertexLimit(List<CombineInstance> combines)
    {
        var chunks = new List<List<CombineInstance>>();
        var current = new List<CombineInstance>();
        int currentVerts = 0;

        foreach (var ci in combines)
        {
            int verts = ci.mesh.vertexCount;
            if (currentVerts + verts > VertexLimit && current.Count > 0)
            {
                chunks.Add(current);
                current = new List<CombineInstance>();
                currentVerts = 0;
            }
            current.Add(ci);
            currentVerts += verts;
        }

        if (current.Count > 0)
            chunks.Add(current);

        return chunks;
    }

    void ClearBaked()
    {
        if (sourceContainer == null)
        {
            EditorUtility.DisplayDialog("Bake Crowd", "Assign a source container first so the baked output can be found.", "OK");
            return;
        }

        var existing = GameObject.Find(BakedPrefix + sourceContainer.name);
        if (existing == null)
        {
            EditorUtility.DisplayDialog("Bake Crowd", "Nothing to clear.", "OK");
            return;
        }

        Undo.DestroyObjectImmediate(existing);
        Debug.Log($"[BakeCrowd] Cleared {BakedPrefix}{sourceContainer.name}.");
    }
}
