using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class RandomizePersons : EditorWindow
{
    static readonly string MaterialsPath = "Assets/PuckAssets-main/Materials";

    static readonly string[] MaleHairOptions = { "Hair Buzz Cut", "Hair Mohawk", "" };
    static readonly string[] Mustaches = { "Mustache Chevron", "Mustache Lampshade", "Mustache Walrus", "Mustache HQM" };
    static readonly string[] Beards = { "Beard Chin Curtain", "Beard Full" };

    GameObject rootObject;
    System.Random rng;

    [MenuItem("Tools/Randomize Persons")]
    static void ShowWindow()
    {
        GetWindow<RandomizePersons>("Randomize Persons");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Drag the root object that contains all Person objects.");
        rootObject = (GameObject)EditorGUILayout.ObjectField("Persons Root", rootObject, typeof(GameObject), true);

        EditorGUI.BeginDisabledGroup(rootObject == null);
        if (GUILayout.Button("Randomize All"))
            Run();
        EditorGUI.EndDisabledGroup();
    }

    void Run()
    {
        var skinMats = LoadMaterials("Skin");
        var hairMats = LoadMaterials("Hair");
        var eyeMats = LoadMaterials("Eyes");
        var shirtMats = LoadMaterials("Shirt");
        var pantsMats = LoadMaterials("Pants");

        if (skinMats.Length == 0 || hairMats.Length == 0 || eyeMats.Length == 0 ||
            shirtMats.Length == 0 || pantsMats.Length == 0)
        {
            EditorUtility.DisplayDialog("Error",
                "Missing materials. Run Tools > Generate Player Materials first.", "OK");
            return;
        }

        rng = new System.Random(System.Environment.TickCount);

        int count = 0;
        foreach (Transform person in rootObject.transform)
        {
            Undo.RegisterFullObjectHierarchyUndo(person.gameObject, "Randomize Persons");
            RandomizePerson(person, skinMats, hairMats, eyeMats, shirtMats, pantsMats);
            count++;
        }

        Debug.Log($"[RandomizePersons] Randomized {count} persons.");
        EditorUtility.DisplayDialog("Randomize Persons", $"Randomized {count} persons.", "OK");
    }

    T Pick<T>(T[] array) => array[rng.Next(array.Length)];

    void RandomizePerson(Transform person, Material[] skinMats, Material[] hairMats,
        Material[] eyeMats, Material[] shirtMats, Material[] pantsMats)
    {
        bool isFemale = rng.NextDouble() < 0.5;
        Material hairMat = Pick(hairMats);
        string gender = isFemale ? "F" : "M";

        // --- Disable EVERYTHING toggleable before making any choices ---
        // Walk by index so we find children even if they're already inactive
        DisableGroup(person, "Hair");
        DisableGroup(person, "Body");
        DisableGroup(person, "FacialHair");

        // --- Hair ---
        Transform hair = FindChildByName(person, "Hair");
        if (hair != null)
        {
            if (isFemale)
            {
                EnableChild(hair, "Hair Pony Tail");
            }
            else
            {
                string choice = Pick(MaleHairOptions);
                if (choice.Length > 0)
                    EnableChild(hair, choice);
            }

            ApplyMaterialToActiveChildren(hair, hairMat);
        }

        // --- Torso (shirt) ---
        Transform body = FindChildByName(person, "Body");
        if (body != null)
        {
            if (isFemale)
                EnableChild(body, "TorsoSmall");
            else
                EnableChild(body, rng.NextDouble() < 0.5 ? "TorsoWide" : "TorsoSmall");

            ApplyMaterialToActiveChildren(body, Pick(shirtMats));
        }

        // --- Facial Hair (men only, already disabled above) ---
        if (!isFemale)
        {
            Transform facialHair = FindChildByName(person, "FacialHair");
            if (facialHair != null)
            {
                // 0=none, 1=mustache only, 2=beard only, 3=both
                int facialChoice = rng.Next(4);

                if (facialChoice == 1 || facialChoice == 3)
                    EnableChild(facialHair, Pick(Mustaches));

                if (facialChoice == 2 || facialChoice == 3)
                    EnableChild(facialHair, Pick(Beards));

                ApplyMaterialToActiveChildren(facialHair, hairMat);
            }
        }

        // --- Skin (Head) ---
        SetMaterial(FindChildByName(person, "Head"), Pick(skinMats));

        // --- Eyes ---
        SetMaterial(FindChildByName(person, "Eyes"), Pick(eyeMats));

        // --- Pants (Groin mesh) ---
        Transform groin = FindRendererChild(person, "Groin");
        if (groin != null)
            SetMaterial(groin, Pick(pantsMats));

        Debug.Log($"[RandomizePersons] {person.name} -> {gender}");
    }

    Transform FindRendererChild(Transform parent, string name)
    {
        Transform found = FindChildByName(parent, name);
        if (found != null && FindRenderer(found) != null)
            return found;
        return null;
    }

    Transform FindChildByName(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).name == childName)
                return parent.GetChild(i);
        }
        for (int i = 0; i < parent.childCount; i++)
        {
            var result = FindChildByName(parent.GetChild(i), childName);
            if (result != null)
                return result;
        }
        return null;
    }

    void DisableGroup(Transform person, string groupName)
    {
        Transform group = FindChildByName(person, groupName);
        if (group == null) return;

        for (int i = 0; i < group.childCount; i++)
        {
            var child = group.GetChild(i);
            child.gameObject.SetActive(false);
            for (int j = 0; j < child.childCount; j++)
                child.GetChild(j).gameObject.SetActive(false);
        }
    }

    void EnableChild(Transform parent, string childName)
    {
        Transform child = FindChildByName(parent, childName);
        if (child == null)
        {
            Debug.LogWarning($"[RandomizePersons] '{childName}' not found under {parent.name}");
            return;
        }

        child.gameObject.SetActive(true);
        for (int i = 0; i < child.childCount; i++)
            child.GetChild(i).gameObject.SetActive(true);
    }

    void ApplyMaterialToActiveChildren(Transform parent, Material mat)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.gameObject.activeSelf)
                SetMaterial(child, mat);
        }
    }

    void SetMaterial(Transform obj, Material mat)
    {
        if (obj == null) return;
        var renderer = FindRenderer(obj);
        if (renderer != null)
            renderer.sharedMaterial = mat;
    }

    Renderer FindRenderer(Transform obj)
    {
        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
            return renderer;

        for (int i = 0; i < obj.childCount; i++)
        {
            renderer = obj.GetChild(i).GetComponent<Renderer>();
            if (renderer != null)
                return renderer;
        }

        return null;
    }

    Material[] LoadMaterials(string subfolder)
    {
        string folder = $"{MaterialsPath}/{subfolder}";
        var guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
        var mats = new List<Material>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                mats.Add(mat);
        }
        return mats.ToArray();
    }
}
