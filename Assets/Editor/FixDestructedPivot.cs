using UnityEditor;
using UnityEngine;

public static class FixDestructedPivot
{
    [MenuItem("Tools/Match Destructed Origin To Intact")]
    static void Run()
    {
        // Select the intact prefab first, then ctrl-click the destructed prefab
        var sel = Selection.objects;
        if (sel.Length != 2) { Debug.LogError("Select 2 prefabs: intact, then destructed."); return; }

        string intactPath = AssetDatabase.GetAssetPath(sel[0]);
        string destPath = AssetDatabase.GetAssetPath(sel[1]);

        var intact = PrefabUtility.LoadPrefabContents(intactPath);
        var dest = PrefabUtility.LoadPrefabContents(destPath);

        // New root at the intact building's origin
        var newRoot = new GameObject(dest.name);
        newRoot.transform.position = intact.transform.position;
        newRoot.transform.rotation = intact.transform.rotation;
        newRoot.transform.localScale = intact.transform.localScale;

        // Re-parent, keeping world position
        dest.transform.SetParent(newRoot.transform, true);

        PrefabUtility.SaveAsPrefabAsset(newRoot, destPath);

        PrefabUtility.UnloadPrefabContents(intact);
        Object.DestroyImmediate(newRoot);
        // dest is a child of newRoot, destroyed with it
    }
}