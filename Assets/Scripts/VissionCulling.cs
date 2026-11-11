// Assets/Scripts (fuera de Editor). Menu: Tools/Perf/Crear VisionCulling. Single player y multiplayer.
// Congela (Animator + ChargerAI y detiene el NavMeshAgent) lo que NO ve ningun jugador. "Ve" = dentro del frustum de una camara de juego
// o a menos de safeRadius de CUALQUIER jugador (cubre jugadores remotos sin camara local y al servidor dedicado).
// Solo actua en single player / servidor / host; los clientes puros no tocan nada.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class VisionCulling : MonoBehaviour
{
    public string tag_ = "Enemy";
    public float safeRadius = 45f, size = 3f, interval = .25f;   // safeRadius debe superar detectRange del enemigo (30)

    class E { public Animator an; public NavMeshAgent ag; public ChargerAI ai; public bool on = true; }
    readonly Dictionary<GameObject, E> map = new Dictionary<GameObject, E>();
    readonly List<GameObject> dead = new List<GameObject>();
    readonly Plane[] pl = new Plane[6];
    readonly Camera[] cams = new Camera[8];
    GameObject[] players = System.Array.Empty<GameObject>();
    float next, scan;

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + interval;
        var nm = NetworkManager.Singleton;
        if (nm && nm.IsListening && !nm.IsServer) return;

        if (Time.time >= scan)
        {
            scan = Time.time + 2f; players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var g in GameObject.FindGameObjectsWithTag(tag_))
                if (!map.ContainsKey(g)) map[g] = new E { an = g.GetComponentInChildren<Animator>(), ag = g.GetComponent<NavMeshAgent>(), ai = g.GetComponent<ChargerAI>() };
        }

        int n = Camera.GetAllCameras(cams), r = 0;
        for (int i = 0; i < n; i++) if (cams[i].cameraType == CameraType.Game && cams[i].isActiveAndEnabled) cams[r++] = cams[i];

        dead.Clear();
        foreach (var kv in map)
        {
            if (!kv.Key) { dead.Add(kv.Key); continue; }
            bool v = Seen(kv.Key.transform.position, r);
            var e = kv.Value;
            if (v == e.on) continue;
            e.on = v;
            bool ok = e.ag && e.ag.isOnNavMesh;   // el agente NO se desactiva (re-activarlo fuera del NavMesh lanza avisos): solo se detiene
            if (v) { if (e.ai) e.ai.enabled = true; if (e.an) e.an.enabled = true; if (ok) e.ag.isStopped = false; }
            else { if (e.ai) e.ai.enabled = false; if (ok) { e.ag.isStopped = true; e.ag.velocity = Vector3.zero; } if (e.an) e.an.enabled = false; }
        }
        foreach (var d in dead) map.Remove(d);
    }

    bool Seen(Vector3 p, int camCount)
    {
        float sr = safeRadius * safeRadius;
        foreach (var g in players) if (g && g.activeInHierarchy && (g.transform.position - p).sqrMagnitude < sr) return true;
        for (int i = 0; i < camCount; i++)
        {
            GeometryUtility.CalculateFrustumPlanes(cams[i], pl);
            bool ok = true;
            for (int k = 0; k < 6 && ok; k++) ok = pl[k].GetDistanceToPoint(p) >= -size;
            if (ok) return true;
        }
        return false;
    }
}

#if UNITY_EDITOR
static class VisionCullingMenu
{
    [UnityEditor.MenuItem("Tools/Perf/Crear VisionCulling")]
    static void Create()
    {
        var v = Object.FindFirstObjectByType<VisionCulling>();
        if (!v) { v = new GameObject("VisionCulling").AddComponent<VisionCulling>(); UnityEditor.Undo.RegisterCreatedObjectUndo(v.gameObject, "VisionCulling"); }
        UnityEditor.Selection.activeObject = v.gameObject;
    }
}
#endif