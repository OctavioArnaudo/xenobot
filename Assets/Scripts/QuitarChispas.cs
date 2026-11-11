#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class QuitarPlayChispas
{
    const string Evento = "PlayChispas";

    [MenuItem("Tools/Quitar evento PlayChispas")]
    static void Run()
    {
        foreach (var g in AssetDatabase.FindAssets("t:Model"))
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) as ModelImporter;
            if (imp == null) continue;
            var clips = imp.clipAnimations;
            bool dirty = false;
            foreach (var c in clips)
            {
                int n = c.events.Length;
                c.events = c.events.Where(e => e.functionName != Evento).ToArray();
                dirty |= c.events.Length != n;
            }
            if (!dirty) continue;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            Debug.Log("Evento quitado en " + imp.assetPath);
        }

        foreach (var g in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (AssetImporter.GetAtPath(p) is ModelImporter) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
            var ev = AnimationUtility.GetAnimationEvents(clip);
            var f = ev.Where(e => e.functionName != Evento).ToArray();
            if (f.Length == ev.Length) continue;
            AnimationUtility.SetAnimationEvents(clip, f);
            EditorUtility.SetDirty(clip);
            Debug.Log("Evento quitado en " + p);
        }
        AssetDatabase.SaveAssets();
    }
}
#endif