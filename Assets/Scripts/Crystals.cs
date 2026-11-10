// Poner FUERA de la carpeta Editor (ej. Assets/Scripts/Crystal.cs). Lo usan Tools/Terrain/16, MeleeController y los arboles.
using System.Collections.Generic;
using UnityEngine;

// Todo lo rompible (cristales, arboles). Recibe dano de: Damage(float) (SendMessage), MeleeController y ProjectileController.
public class Smashable : MonoBehaviour
{
    public float life = 6f;   // golpes/disparos de 2 de dano: 3
    bool dead;

    void Awake() { if (!TryGetComponent<Rigidbody>(out _)) gameObject.AddComponent<Rigidbody>().isKinematic = true; }   // asegura eventos de fisica

    public void Damage(float d)
    {
        if (dead || (life -= d) > 0f) return;
        dead = true; Die(); Destroy(gameObject);
    }

    protected virtual void Die() { }

    void OnCollisionEnter(Collision c) { if (c.relativeVelocity.sqrMagnitude > 100f) Damage(c.relativeVelocity.magnitude); }
}

// Racimo de cristal: al romperse suelta 1-3 gemas de cualquier color.
public class Crystal : Smashable
{
    public static readonly List<Crystal> All = new List<Crystal>();
    public static readonly Color[] Colors = { new Color(.6f, .1f, 1f), new Color(1f, .05f, .05f), new Color(1f, .85f, .05f) };
    public int color;               // 0 violeta, 1 rojo, 2 amarillo
    public GameObject[] drops;      // un pickup por color
    Collider col;

    void OnEnable() { col = GetComponent<Collider>(); All.Add(this); }
    void OnDisable() => All.Remove(this);
    public Bounds Box => col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 8f);

    protected override void Die()
    {
        if (drops == null || drops.Length < 3) return;
        for (int i = Random.Range(1, 4); i > 0; i--)
        {
            var g = Instantiate(drops[Random.Range(0, 3)], transform.position + Vector3.up * (1f + .3f * i) + Random.insideUnitSphere * .3f, Random.rotation);
            g.GetComponent<Rigidbody>().AddForce(Random.insideUnitSphere * 3f + Vector3.up * 3f, ForceMode.VelocityChange);
        }
    }
}

// Recolectable apilable (0-2 gemas, 3 madera): se acerca solo al jugador (tag Player, o Camera.main).
// Para usar tu inventario real, reemplaza "Count[color]++" por tu llamada de inventario.
public class CrystalPickup : MonoBehaviour
{
    public static readonly int[] Count = new int[4];
    public int color;
    float t;
    Transform pl;

    void Update()
    {
        if ((t += Time.deltaTime) < .8f) return;
        if (pl == null)
        {
            var p = GameObject.FindWithTag("Player");
            pl = p != null ? p.transform : Camera.main != null ? Camera.main.transform : null;
            if (pl == null) return;
        }
        Vector3 d = pl.position + Vector3.up - transform.position;
        if (d.sqrMagnitude > 64f) return;
        if (d.sqrMagnitude < 1.5f) { Count[color]++; Destroy(gameObject); return; }
        GetComponent<Rigidbody>().isKinematic = true;
        transform.position += d.normalized * 10f * Time.deltaTime;
    }
}

// Arboles del Terrain rompibles. Los TreeInstance no tienen colliders, asi que cerca del jugador (R metros) se
// crean colliders-proxy; al romperse se oculta el arbol, y cada 3 arboles se suelta 1 madera.
// En el Editor se clona el TerrainData para no borrar arboles del asset al jugar.
public class TreeBreaker : MonoBehaviour
{
    const float R = 40f;
    class E { public TreeInstance[] a; public TreePrototype[] p; public bool[] dead; }
    readonly Dictionary<Terrain, E> cache = new Dictionary<Terrain, E>();
    readonly Dictionary<(Terrain, int), GameObject> live = new Dictionary<(Terrain, int), GameObject>();
    readonly List<(Terrain, int)> gone = new List<(Terrain, int)>();
    static int broken;
    static Material wood;
    float next;
    Transform pl;

    [RuntimeInitializeOnLoadMethod]
    static void Init() => DontDestroyOnLoad(new GameObject("TreeBreaker", typeof(TreeBreaker)));

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + .4f;
        if (pl == null)
        {
            var p = GameObject.FindWithTag("Player");
            pl = p != null ? p.transform : Camera.main != null ? Camera.main.transform : null;
            if (pl == null) return;
        }
        Vector3 c = pl.position;

        gone.Clear();
        foreach (var kv in live)
            if (kv.Value == null || (kv.Value.transform.position - c).sqrMagnitude > (R + 10f) * (R + 10f)) gone.Add(kv.Key);
        foreach (var k in gone) { if (live[k] != null) Destroy(live[k]); live.Remove(k); }

        foreach (var t in Terrain.activeTerrains)
        {
            var td = t.terrainData; Vector3 tp = t.transform.position, sz = td.size;
            if (c.x < tp.x - R || c.x > tp.x + sz.x + R || c.z < tp.z - R || c.z > tp.z + sz.z + R) continue;
            if (!cache.TryGetValue(t, out var e))
            {
#if UNITY_EDITOR
                td = Instantiate(td); t.terrainData = td;
                if (t.TryGetComponent<TerrainCollider>(out var tc)) tc.terrainData = td;
#endif
                cache[t] = e = new E { a = td.treeInstances, p = td.treePrototypes };
                e.dead = new bool[e.a.Length];
            }
            for (int i = 0; i < e.a.Length; i++)
            {
                if (e.dead[i]) continue;
                var ti = e.a[i];
                float x = tp.x + ti.position.x * sz.x, z = tp.z + ti.position.z * sz.z, dx = x - c.x, dz = z - c.z;
                if (dx * dx + dz * dz > R * R || live.ContainsKey((t, i))) continue;
                live[(t, i)] = Proxy(t, e, i, new Vector3(x, tp.y + t.SampleHeight(new Vector3(x, 0f, z)), z));
            }
        }
    }

    GameObject Proxy(Terrain t, E e, int i, Vector3 pos)
    {
        var ti = e.a[i];
        float h = 8f, r = .4f;
        var pr = e.p[ti.prototypeIndex].prefab;
        var mf = pr != null ? pr.GetComponentInChildren<MeshFilter>() : null;
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            h = b.size.y; r = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * .15f, .3f, 1f);
        }
        var go = new GameObject("TreeProxy");
        go.transform.position = pos;
        var cap = go.AddComponent<CapsuleCollider>();
        cap.height = h * ti.heightScale; cap.radius = r * ti.widthScale; cap.center = Vector3.up * cap.height / 2f;
        var s = go.AddComponent<TreeBit>();
        s.owner = this; s.t = t; s.i = i;
        return go;
    }

    public void Fell(Terrain t, int i, Vector3 p)
    {
        var e = cache[t];
        e.dead[i] = true; live.Remove((t, i));
        var ti = e.a[i]; ti.heightScale = ti.widthScale = 1e-4f;
        e.a[i] = ti; t.terrainData.SetTreeInstance(i, ti);
        if (++broken % 3 == 0) DropWood(p);
    }

    static void DropWood(Vector3 p)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = "Wood";
        g.transform.SetPositionAndRotation(p + Vector3.up, Random.rotation);
        g.transform.localScale = new Vector3(.25f, .5f, .25f);
        if (wood == null) { wood = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); wood.color = new Color(.4f, .25f, .1f); }
        g.GetComponent<Renderer>().sharedMaterial = wood;
        g.AddComponent<Rigidbody>().mass = .5f;
        g.AddComponent<CrystalPickup>().color = 3;
    }
}

public class TreeBit : Smashable
{
    public TreeBreaker owner; public Terrain t; public int i;
    protected override void Die() => owner.Fell(t, i, transform.position);
}

// Los cristales EMITEN LUZ de su color: un pool de 6 luces puntuales (sin sombras) sigue a los racimos mas cercanos.
public class CrystalGlow : MonoBehaviour
{
    const int N = 6;
    const float Intensity = 8f, Radius = 300f;
    readonly Light[] ls = new Light[N];
    readonly Crystal[] best = new Crystal[N];
    readonly float[] bd = new float[N];
    float next;
    Transform pl;

    [RuntimeInitializeOnLoadMethod]
    static void Init() => DontDestroyOnLoad(new GameObject("CrystalGlow", typeof(CrystalGlow)));

    void Awake()
    {
        for (int i = 0; i < N; i++)
        {
            var l = new GameObject("Light" + i).AddComponent<Light>();
            l.transform.SetParent(transform);
            l.type = LightType.Point; l.shadows = LightShadows.None; l.intensity = Intensity; l.enabled = false;
            ls[i] = l;
        }
    }

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + .3f;
        if (pl == null)
        {
            var p = GameObject.FindWithTag("Player");
            pl = p != null ? p.transform : Camera.main != null ? Camera.main.transform : null;
            if (pl == null) return;
        }
        for (int i = 0; i < N; i++) { best[i] = null; bd[i] = float.MaxValue; }
        foreach (var c in Crystal.All)
        {
            float d = (c.transform.position - pl.position).sqrMagnitude;
            int k = N - 1;
            if (d > Radius * Radius || d >= bd[k]) continue;
            while (k > 0 && d < bd[k - 1]) { bd[k] = bd[k - 1]; best[k] = best[k - 1]; k--; }
            bd[k] = d; best[k] = c;
        }
        for (int i = 0; i < N; i++)
        {
            ls[i].enabled = best[i] != null;
            if (best[i] == null) continue;
            Bounds b = best[i].Box;
            ls[i].transform.position = b.center;
            ls[i].range = Mathf.Clamp(b.size.magnitude * 2f, 20f, 70f);
            ls[i].color = Crystal.Colors[best[i].color];
        }
    }
}

// Contador en pantalla (se crea solo).
public class CrystalHud : MonoBehaviour
{
    static readonly Color[] C = { new Color(.7f, .25f, 1f), Color.red, Color.yellow, new Color(.75f, .5f, .25f) };
    static readonly string[] N = { "Violeta", "Rojo", "Amarillo", "Madera" };

    [RuntimeInitializeOnLoadMethod]
    static void Init() => DontDestroyOnLoad(new GameObject("CrystalHud", typeof(CrystalHud)));

    void OnGUI()
    {
        for (int i = 0; i < 4; i++) { GUI.color = C[i]; GUI.Label(new Rect(20, 20 + i * 22, 160, 22), N[i] + ": " + CrystalPickup.Count[i]); }
    }
}