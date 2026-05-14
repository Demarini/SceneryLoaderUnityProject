using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AudioClipReferenceScanner
{
    static readonly string[] Prefabs = { "Assets/DanceClubFinal.prefab", "Assets/DanceClub.prefab" };

    [MenuItem("Tools/Optimize/Scan AudioClip References on DanceClub prefabs")]
    static void Scan()
    {
        foreach (var path in Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.Log($"(missing) {path}"); continue; }

            var hits = new List<string>();

            foreach (var src in prefab.GetComponentsInChildren<AudioSource>(true))
            {
                if (src.clip != null)
                    hits.Add($"AudioSource on '{GetPath(src.transform)}' -> clip '{AssetDatabase.GetAssetPath(src.clip)}'");
            }

            // Catch references from arbitrary scripts that hold an AudioClip field
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference &&
                        it.objectReferenceValue is AudioClip clip)
                    {
                        hits.Add($"{mb.GetType().Name}.{it.propertyPath} on '{GetPath(mb.transform)}' -> '{AssetDatabase.GetAssetPath(clip)}'");
                    }
                }
            }

            if (hits.Count == 0)
                Debug.Log($"[AudioScan] {path}: no AudioClip references found. Good.");
            else
                Debug.LogWarning($"[AudioScan] {path}: {hits.Count} AudioClip reference(s) pulling audio into the bundle:\n  " +
                                 string.Join("\n  ", hits));
        }
    }

    static string GetPath(Transform t)
    {
        var stack = new Stack<string>();
        while (t != null) { stack.Push(t.name); t = t.parent; }
        return string.Join("/", stack);
    }
}
