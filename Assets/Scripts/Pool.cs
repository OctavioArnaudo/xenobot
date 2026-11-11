// Assets/Scripts (fuera de Editor). Pool generico: Pool.Warm(prefab, n) al iniciar; Pool.Get(prefab, pos, rot) en vez de Instantiate; Pool.Release(go) en vez de Destroy.
// Sin basura en juego: pilas por prefab, instancias inactivas bajo un contenedor persistente.
using System.Collections.Generic;
using UnityEngine;

public static class Pool
{
    static readonly Dictionary<int, Stack<GameObject>> free = new Dictionary<int, Stack<GameObject>>();
    static readonly Dictionary<int, int> owner = new Dictionary<int, int>();   // instancia -> prefab
    static Transform root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { free.Clear(); owner.Clear(); root = null; }

    static Transform Root()
    {
        if (!root) { var g = new GameObject("Pool"); Object.DontDestroyOnLoad(g); g.SetActive(false); root = g.transform; }   // raiz inactiva: lo de dentro no ejecuta nada
        return root;
    }

    public static void Warm(GameObject prefab, int n)
    {
        for (int i = 0; i < n; i++) Release(Make(prefab));
    }

    public static GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        GameObject g = null;
        if (free.TryGetValue(prefab.GetInstanceID(), out var st)) while (st.Count > 0 && !(g = st.Pop())) { }
        if (!g) g = Make(prefab);
        g.transform.SetParent(null, false);
        g.transform.SetPositionAndRotation(pos, rot);
        g.SetActive(true);   // primero activa: OnEnable puede dejar el Rigidbody no cinematico
        if (g.TryGetComponent(out Rigidbody rb))
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
        return g;
    }

    public static void Release(GameObject g)
    {
        if (!g) return;
        if (!owner.TryGetValue(g.GetInstanceID(), out int pid)) { Object.Destroy(g); return; }   // no viene del pool
        g.SetActive(false); g.transform.SetParent(Root(), false);
        if (!free.TryGetValue(pid, out var st)) free[pid] = st = new Stack<GameObject>();
        st.Push(g);
    }

    static GameObject Make(GameObject prefab)
    {
        var g = Object.Instantiate(prefab, Root());
        owner[g.GetInstanceID()] = prefab.GetInstanceID();
        return g;
    }
}