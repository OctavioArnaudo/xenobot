#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SilenciarPlayChispas
{
    const string Evento = "PlayChispas";

    static bool Silenciar(AnimationEvent[] ev)
    {
        bool dirty = false;
        foreach (var e in ev.Where(e => e.functionName == Evento && e.messageOptions != SendMessageOptions.DontRequireReceiver))
        {
            e.messageOptions = SendMessageOptions.DontRequireReceiver;
            dirty = true;
        }
        return dirty;
    }

    [MenuItem("Tools/Silenciar evento PlayChispas")]
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
                var ev = c.events;
                if (!Silenciar(ev)) continue;
                c.events = ev;
                dirty = true;
            }
            if (!dirty) continue;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            Debug.Log("Silenciado en " + imp.assetPath);
        }

        foreach (var g in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (AssetImporter.GetAtPath(p) is ModelImporter) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
            var ev = AnimationUtility.GetAnimationEvents(clip);
            if (!Silenciar(ev)) continue;
            AnimationUtility.SetAnimationEvents(clip, ev);
            EditorUtility.SetDirty(clip);
            Debug.Log("Silenciado en " + p);
        }
        AssetDatabase.SaveAssets();
    }
}
#endif