using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class PlacePersons : EditorWindow
{
    [System.Serializable]
    public class PlacementZone
    {
        public GameObject plane;
        public float yCoordinate;
    }

    GameObject personPrefab;
    float spacing = 2f;
    List<PlacementZone> zones = new List<PlacementZone>();
    Vector2 scrollPos;
    const string ContainerName = "PlacedPersons";

    [MenuItem("Tools/Place Persons")]
    static void ShowWindow()
    {
        GetWindow<PlacePersons>("Place Persons");
    }

    void OnGUI()
    {
        personPrefab = (GameObject)EditorGUILayout.ObjectField("Person Prefab", personPrefab, typeof(GameObject), false);
        spacing = EditorGUILayout.FloatField("Spacing", spacing);
        if (spacing < 0.5f) spacing = 0.5f;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement Zones", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        for (int i = 0; i < zones.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            zones[i].plane = (GameObject)EditorGUILayout.ObjectField(zones[i].plane, typeof(GameObject), true);
            zones[i].yCoordinate = EditorGUILayout.FloatField("Y", zones[i].yCoordinate, GUILayout.Width(100));
            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                zones.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Add Zone"))
            zones.Add(new PlacementZone());

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(personPrefab == null || zones.Count == 0);
        if (GUILayout.Button("Place Persons"))
            Run();
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Clear Placed Persons"))
            ClearPlaced();
    }

    void Run()
    {
        var container = GameObject.Find(ContainerName);
        if (container == null)
            container = new GameObject(ContainerName);

        Undo.RegisterFullObjectHierarchyUndo(container, "Place Persons");

        var rng = new System.Random(System.Environment.TickCount);
        int count = 0;

        foreach (var zone in zones)
        {
            if (zone.plane == null) continue;

            var t = zone.plane.transform;
            // Unity default Plane is 10x10 units in local space
            float halfX = 5f * t.lossyScale.x;
            float halfZ = 5f * t.lossyScale.z;
            Vector3 center = t.position;

            float startX = center.x - halfX;
            float endX = center.x + halfX;
            float startZ = center.z - halfZ;
            float endZ = center.z + halfZ;

            for (float x = startX + spacing * 0.5f; x < endX; x += spacing)
            {
                for (float z = startZ + spacing * 0.5f; z < endZ; z += spacing)
                {
                    float jitterX = (float)(rng.NextDouble() - 0.5) * spacing * 0.5f;
                    float jitterZ = (float)(rng.NextDouble() - 0.5) * spacing * 0.5f;

                    var pos = new Vector3(x + jitterX, zone.yCoordinate, z + jitterZ);
                    float yRot = (float)(rng.NextDouble() * 360.0);

                    var person = (GameObject)PrefabUtility.InstantiatePrefab(personPrefab);
                    person.transform.position = pos;
                    person.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
                    person.transform.SetParent(container.transform);
                    Undo.RegisterCreatedObjectUndo(person, "Place Person");
                    count++;
                }
            }
        }

        Debug.Log($"[PlacePersons] Placed {count} persons across {zones.Count} zones.");
        EditorUtility.DisplayDialog("Place Persons", $"Placed {count} persons.", "OK");
    }

    void ClearPlaced()
    {
        var container = GameObject.Find(ContainerName);
        if (container == null)
        {
            EditorUtility.DisplayDialog("Place Persons", "Nothing to clear.", "OK");
            return;
        }

        Undo.DestroyObjectImmediate(container);
        Debug.Log("[PlacePersons] Cleared all placed persons.");
    }
}
