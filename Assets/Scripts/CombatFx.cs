// Assets/Scripts (fuera de Editor). Efectos de combate DIBUJADOS POR CODIGO y precargados en pool al iniciar la escena:
//   CombatFx.Slash(pos, dir, size, color)  -> sablazo de viento (3 estelas en media luna, malla procedural)
//   CombatFx.Flash(gameObject)             -> parpadeo rojo (MaterialPropertyBlock, sin crear materiales ni basura)
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class CombatFx
{
    internal static readonly Stack<FxSlash> pool = new Stack<FxSlash>();
    internal static Mesh mesh;
    static FxFlash flash;
    static readonly Dictionary<int, Renderer[]> cache = new Dictionary<int, Renderer[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pool.Clear(); cache.Clear(); mesh = null; flash = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var root = new GameObject("CombatFx"); Object.DontDestroyOnLoad(root);
        flash = root.AddComponent<FxFlash>();
        mesh = BuildMesh();
        for (int i = 0; i < 8; i++) { var s = FxSlash.Create(root.transform); s.gameObject.SetActive(false); pool.Push(s); }
    }

    public static void Slash(Vector3 pos, Vector3 dir, float size, Color col)
    {
        FxSlash s = null;
        while (pool.Count > 0 && !(s = pool.Pop())) { }
        if (!s) s = FxSlash.Create(flash ? flash.transform : null);
        s.Play(pos, dir, size, col);
    }

    public static void Flash(GameObject g)
    {
        if (!g || !flash) return;
        int id = g.GetInstanceID();
        if (!cache.TryGetValue(id, out var rs))
            cache[id] = rs = g.GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)).ToArray();
        flash.Add(id, rs);
    }

    // 3 estelas concentricas; ancho maximo al centro, puntas finas; alfa por vertice.
    static Mesh BuildMesh()
    {
        float[] r = { 1f, .78f, .58f }, span = { 120f, 100f, 80f }, w = { .55f, .40f, .28f };
        const int N = 14;
        var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
        for (int k = 0; k < 3; k++)
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N, s = Mathf.Sin(Mathf.PI * u), a = Mathf.Deg2Rad * Mathf.Lerp(-span[k] * .5f, span[k] * .5f, u);
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 y = Vector3.up * k * .05f, o = d * (w[k] * s * .5f);
                v.Add(d * r[k] + o + y); v.Add(d * r[k] + y); v.Add(d * r[k] - o + y);   // borde, centro, borde: el alfa se difumina a lo ancho
                c.Add(new Color32(255, 255, 255, 0)); c.Add(new Color32(255, 255, 255, (byte)(s * s * 255f))); c.Add(new Color32(255, 255, 255, 0));
                if (i < N) { int b = (k * (N + 1) + i) * 3; t.AddRange(new[] { b, b + 1, b + 4, b, b + 4, b + 3, b + 1, b + 2, b + 5, b + 1, b + 5, b + 4 }); }
            }
        var m = new Mesh { name = "WindSlash" };
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
        return m;
    }
}

public class FxSlash : MonoBehaviour
{
    const float Life = .28f;
    float t, size; Color col; Quaternion rot; Material mat;

    internal static FxSlash Create(Transform parent)
    {
        var g = new GameObject("WindSlash");
        if (parent) g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = CombatFx.mesh;
        var mr = g.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        var s = g.AddComponent<FxSlash>();
        mr.sharedMaterial = s.mat = new Material(Shader.Find("Sprites/Default"));
        return s;
    }

    internal void Play(Vector3 p, Vector3 dir, float sz, Color c)
    {
        dir.y = 0f; if (dir.sqrMagnitude < .01f) dir = Vector3.forward;
        transform.position = p; size = sz; col = c; t = 0f;
        rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 20f);   // inclinado: sablazo en diagonal
        gameObject.SetActive(true);
    }

    void Update()
    {
        if ((t += Time.deltaTime / Life) >= 1f) { gameObject.SetActive(false); CombatFx.pool.Push(this); return; }
        float e = 1f - (1f - t) * (1f - t);
        transform.rotation = rot * Quaternion.Euler(0f, Mathf.Lerp(-40f, 40f, e), 0f);   // barrido
        transform.localScale = Vector3.one * (size * (.6f + .8f * e));
        mat.color = new Color(col.r, col.g, col.b, col.a * (1f - t) * (1f - t));
    }
}

public class FxFlash : MonoBehaviour
{
    const int Slots = 8; const float Life = .6f;
    readonly int[] id = new int[Slots]; readonly Renderer[][] rs = new Renderer[Slots][]; readonly float[] t = new float[Slots];
    static MaterialPropertyBlock mpb;   // creado en Awake/Update, nunca en inicializador de campo (Unity lo prohibe)
    static readonly int Base = Shader.PropertyToID("_BaseColor"), Col = Shader.PropertyToID("_Color");

    void Awake() { mpb ??= new MaterialPropertyBlock(); for (int i = 0; i < Slots; i++) t[i] = Life; }

    public void Add(int key, Renderer[] r)
    {
        int s = -1;
        for (int i = 0; i < Slots; i++) { if (id[i] == key && t[i] < Life) { s = i; break; } if (s < 0 && t[i] >= Life) s = i; }
        if (s < 0) s = 0;
        id[s] = key; rs[s] = r; t[s] = 0f;
    }

    void Update()
    {
        for (int i = 0; i < Slots; i++)
        {
            if (t[i] >= Life) continue;
            t[i] += Time.deltaTime;
            bool end = t[i] >= Life;
            var c = end ? Color.white : Color.Lerp(Color.white, new Color(1f, .1f, .1f), Mathf.Abs(Mathf.Sin(t[i] / Life * Mathf.PI * 3f)));   // 3 parpadeos
            mpb.SetColor(Base, c); mpb.SetColor(Col, c);
            foreach (var r in rs[i]) if (r) { if (end) r.SetPropertyBlock(null); else r.SetPropertyBlock(mpb); }
        }
    }
}