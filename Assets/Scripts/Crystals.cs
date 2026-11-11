// DEBE llamarse Crystal.cs (Unity exige que el archivo se llame igual que el MonoBehaviour 'Crystal'). FUERA de la carpeta Editor. Lo usan Tools/Terrain/16, MeleeController y los arboles.
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Items;
using Runtime.Scripts.Pickup;

// Todo lo rompible (cristales, arboles). Recibe dano de: Damage(float) (SendMessage), MeleeController y ProjectileController.
public class Smashable : MonoBehaviour
{
    public float life = 6f;   // golpes/disparos de 2 de dano: 3
    bool dead; float life0;

    void Awake() { life0 = life; if (!TryGetComponent<Rigidbody>(out _)) gameObject.AddComponent<Rigidbody>().isKinematic = true; }   // asegura eventos de fisica
    protected virtual void OnEnable() { life = life0; dead = false; }   // al reutilizarse desde un pool queda como nuevo

    public void Damage(float d)
    {
        if (dead || Forward(d) || (life -= d) > 0f) return;
        dead = true; Die(); Remove();
    }

    protected virtual bool Forward(float d) => false;   // true = el dano se envio al servidor y aqui no se aplica
    protected virtual void Die() { }
    protected virtual void Remove() => Destroy(gameObject);

    void OnCollisionEnter(Collision c) { if (c.relativeVelocity.sqrMagnitude > 100f) Damage(c.relativeVelocity.magnitude); }
}

// Racimo de cristal: al romperse suelta 1-3 gemas de cualquier color.
public class Crystal : Smashable
{
    public static readonly List<Crystal> All = new List<Crystal>();
    public static readonly Color[] Colors = { new Color(.6f, .1f, 1f), new Color(1f, .05f, .05f), new Color(1f, .85f, .05f) };
    public int color;               // 0 violeta, 1 rojo, 2 amarillo
    [System.NonSerialized] public int id = -1;   // lo asigna CrystalNet (orden de los hijos de "Crystals", igual en todas las maquinas)
    Collider col;

    protected override void OnEnable() { base.OnEnable(); col = GetComponent<Collider>(); All.Add(this); }
    void OnDisable() => All.Remove(this);
    public Bounds Box => col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 8f);

    // Solo corre en el servidor (o sin red): los clientes reenvian su dano por CrystalNet. Cada fragmento aparece en TODAS las maquinas.
    protected override bool Forward(float d)
    {
        if (id < 0 || !CrystalNet.Active || CrystalNet.I.IsServer) return false;
        CrystalNet.I.DamageRpc(id, d); return true;
    }

    protected override void Die()
    {
        for (int i = Random.Range(1, 4); i > 0; i--)
            CrystalPickup.Drop(Random.Range(0, 3), transform.position + Vector3.up * (2f + 1.4f * i) + Random.insideUnitSphere * 1f, Random.rotation, Random.insideUnitSphere * 3f + Vector3.up * 3f);
    }

    protected override void Remove()
    {
        if (id >= 0 && CrystalNet.Active) CrystalNet.I.BreakRpc(id);   // los clientes quitan su copia
        base.Remove();
    }
}

// Fragmento recolectable (0-2 = gemas): se acerca solo al jugador local. Con red, la recogida la arbitra el servidor (CrystalNet.ClaimRpc):
// gana el primero en reclamar y el resto ve desaparecer su copia. Sin red, se recoge directo.
public class CrystalPickup : MonoBehaviour
{
    public static readonly Dictionary<int, CrystalPickup> ById = new Dictionary<int, CrystalPickup>();
    public int color;
    [System.NonSerialized] public int fid;   // id de red del fragmento (0 = sin red)
    bool claiming;
    float t, nudgeT;
    Transform pl;
    Rigidbody rb;
    InventoryController inv;
    bool rolled;

    void Awake() => rb = GetComponent<Rigidbody>();
    void OnEnable() { t = nudgeT = 0f; rolled = false; claiming = false; if (rb) rb.isKinematic = false; }   // reutilizado desde el pool

    // Primer contacto con el suelo: empujon cuesta abajo, para que ruede aunque la pendiente sea suave.
    void OnCollisionEnter(Collision c)
    {
        if (rolled || rb == null || rb.isKinematic) return;
        rolled = true;
        Vector3 down = Vector3.ProjectOnPlane(Vector3.down, c.GetContact(0).normal);
        if (down.sqrMagnitude > 0.0004f) rb.AddForce(down.normalized * (1f + 3f * down.magnitude), ForceMode.VelocityChange);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ById.Clear();

    // Crea un fragmento con impulso. Con red lo invoca el servidor y CrystalNet lo replica a todas las maquinas.
    public static void Drop(int color, Vector3 p, Quaternion r, Vector3 force)
    {
        if (CrystalNet.Active) CrystalNet.I.DropRpc(CrystalNet.I.NextId(), color, p, r, force);
        else Spawn(0, color, p, r, force);
    }

    public static void Spawn(int fid, int color, Vector3 p, Quaternion r, Vector3 force)
    {
        var pk = CrystalFragments.Spawn(color, p, r).GetComponent<CrystalPickup>();
        pk.fid = fid; if (fid > 0) ById[fid] = pk;
        pk.rb.AddForce(force, ForceMode.VelocityChange);
    }

    public void Free() { if (fid > 0) ById.Remove(fid); fid = 0; Pool.Release(gameObject); }

    void Update()
    {
        if (claiming) return;   // esperando al servidor
        // Si se queda parado sobre una pendiente, se le vuelve a dar un empujon cuesta abajo.
        if (rb != null && !rb.isKinematic && (nudgeT -= Time.deltaTime) <= 0f)
        {
            nudgeT = 0.5f;
            if (rb.linearVelocity.sqrMagnitude < 0.04f &&
                Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out var h, 1f, ~0, QueryTriggerInteraction.Ignore) && h.normal.y < 0.97f)
                rb.AddForce(Vector3.ProjectOnPlane(Vector3.down, h.normal).normalized * 2f, ForceMode.VelocityChange);
        }

        if ((t += Time.deltaTime) < .8f) return;
        if (pl == null)
        {
            var li = InventoryController.LocalInstance;
            if (li != null) pl = li.transform;
            else
            {
                var p = GameObject.FindWithTag("Player");
                pl = p != null ? p.transform : Camera.main != null ? Camera.main.transform : null;
            }
            if (pl == null) return;
        }
        Vector3 d = pl.position + Vector3.up - transform.position;
        if (d.sqrMagnitude > 64f) return;
        if (d.sqrMagnitude < 6f) { Collect(); return; }
        if (rb != null) rb.isKinematic = true;
        transform.position += d.normalized * 10f * Time.deltaTime;
    }

    void Collect()
    {
        if (fid > 0 && CrystalNet.Active) { claiming = true; if (rb) rb.isKinematic = true; CrystalNet.I.ClaimRpc(fid); return; }
        Grant(); Free();
    }

    // Entrega la gema al jugador local.
    public void Grant()
    {
        if (inv == null) inv = (pl != null ? pl.GetComponentInChildren<InventoryController>() : null) ?? InventoryController.LocalInstance;
        if (inv != null) PickupController.Give(inv, CrystalFragments.Item(color), NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
    }
}

// Arboles del Terrain rompibles. Los TreeInstance no tienen colliders, asi que cerca del jugador (R metros) se
// crean colliders-proxy (reciclados); al romperse se oculta el arbol.
// En el Editor se clona el TerrainData para no borrar arboles del asset al jugar.
public class TreeBreaker : MonoBehaviour
{
    const float R = 40f;
    class E { public TreeInstance[] a; public TreePrototype[] p; public bool[] dead; }
    readonly Dictionary<Terrain, E> cache = new Dictionary<Terrain, E>();
    readonly Dictionary<(Terrain, int), TreeBit> live = new Dictionary<(Terrain, int), TreeBit>();
    readonly Stack<TreeBit> idle = new Stack<TreeBit>();   // proxies reciclados: sin Instantiate/Destroy al caminar por el bosque
    Vector3 lastScan = new Vector3(1e9f, 0f, 0f);
    readonly List<(Terrain, int)> gone = new List<(Terrain, int)>();
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
        if ((c - lastScan).sqrMagnitude < 25f) return;   // los proxies solo cambian al moverse el jugador ~5 m
        lastScan = c;

        gone.Clear();
        foreach (var kv in live)
            if (kv.Value == null || (kv.Value.transform.position - c).sqrMagnitude > (R + 10f) * (R + 10f)) gone.Add(kv.Key);
        foreach (var k in gone) { if (live[k] != null) Recycle(live[k]); live.Remove(k); }

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

    TreeBit Proxy(Terrain t, E e, int i, Vector3 pos)
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
        TreeBit s;
        if (idle.Count > 0) s = idle.Pop();
        else { var go = new GameObject("TreeProxy"); go.SetActive(false); s = go.AddComponent<TreeBit>(); s.cap = go.AddComponent<CapsuleCollider>(); }
        s.transform.position = pos;
        var cap = s.cap;
        cap.height = h * ti.heightScale; cap.radius = r * ti.widthScale; cap.center = Vector3.up * cap.height / 2f;
        s.owner = this; s.t = t; s.i = i;
        s.gameObject.SetActive(true);
        return s;
    }

    public void Recycle(TreeBit b) { b.gameObject.SetActive(false); idle.Push(b); }

    public void Fell(Terrain t, int i, Vector3 p)
    {
        var e = cache[t];
        e.dead[i] = true; live.Remove((t, i));
        var ti = e.a[i]; ti.heightScale = ti.widthScale = 1e-4f;
        e.a[i] = ti; t.terrainData.SetTreeInstance(i, ti);
    }
}

public class TreeBit : Smashable
{
    public TreeBreaker owner; public Terrain t; public int i; public CapsuleCollider cap;
    protected override void Die() => owner.Fell(t, i, transform.position);
    protected override void Remove() => owner.Recycle(this);
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

// Fragmentos de cristal: un ItemsData por color (creados por codigo, iguales en todas las maquinas: el hash sale del nombre)
// y un "prefab" procedural: rombo irregular facetado, vidrioso y con emision. Es lo que se ve en el mundo y en el inventario.
public static class CrystalFragments
{
    const float Size = 4.5f;   // escala del fragmento respecto al modelo base (0.56 de alto): ~2.5 m
    public static readonly string[] Names = { "Fragmento de cristal violeta", "Fragmento de cristal rojo", "Fragmento de cristal amarillo" };
    static ItemsData[] items;
    static GameObject[] tpl;
    static PhysicsMaterial phys;

    public static ItemsData[] Items { get { Build(); return items; } }
    public static ItemsData Item(int color) => Items[Mathf.Clamp(color, 0, 2)];

    /// <summary>Crea un fragmento fisico en el mundo (rueda por las pendientes).</summary>
    public static GameObject Spawn(int color, Vector3 pos, Quaternion rot)
    {
        Build();
        return Pool.Get(tpl[Mathf.Clamp(color, 0, 2)], pos, rot);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { Build(); for (int c = 0; c < 3; c++) Pool.Warm(tpl[c], 6); }   // plantillas y pool listos antes de romper el primer cristal

    static void Build()
    {
        if (items != null && items[0] != null && tpl[0] != null) return;
        items = new ItemsData[3];
        tpl = new GameObject[3];
        var root = new GameObject("CrystalFragmentTemplates") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(root);
        root.SetActive(false);   // las plantillas no viven; solo se clonan

        phys = new PhysicsMaterial("CrystalFragment") { dynamicFriction = 0.2f, staticFriction = 0.2f, bounciness = 0.35f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum };
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        for (int c = 0; c < 3; c++)
        {
            var t = new GameObject("CrystalFragment");
            t.transform.SetParent(root.transform, false);
            t.AddComponent<MeshFilter>().sharedMesh = MakeMesh(7 + c * 13);
            var mr = t.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeMaterial(shader, Crystal.Colors[c]);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var sc = t.AddComponent<SphereCollider>();   // esfera: rueda cuesta abajo
            sc.radius = 0.2f * Size * 0.85f;
            sc.sharedMaterial = phys;
            var rb = t.AddComponent<Rigidbody>();
            rb.mass = 1.5f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.02f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            t.AddComponent<CrystalPickup>().color = c;
            tpl[c] = t;

            items[c] = ItemsData.Create(Names[c], t);
        }
    }

    static Material MakeMaterial(Shader sh, Color c)
    {
        var m = new Material(sh) { color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.95f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.25f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 1.3f);
        return m;
    }

    // Bipiramide pentagonal irregular, caras planas (rombo vidrioso).
    static Mesh MakeMesh(int seed)
    {
        var rnd = new System.Random(seed);
        float J(float a) => ((float)rnd.NextDouble() * 2f - 1f) * a;
        var top = new Vector3(J(.05f), .30f + J(.04f), J(.05f));
        var bot = new Vector3(J(.05f), -.26f + J(.04f), J(.05f));
        var eq = new Vector3[5];
        for (int i = 0; i < 5; i++)
        {
            float a = (i + J(.2f)) / 5f * Mathf.PI * 2f, r = .14f + J(.05f);
            eq[i] = new Vector3(Mathf.Cos(a) * r, J(.07f), Mathf.Sin(a) * r);
        }
        var v = new List<Vector3>(30);
        var tri = new List<int>(30);
        void T(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) < 0f) { var x = b; b = c; c = x; }   // normal hacia fuera
            tri.Add(v.Count); tri.Add(v.Count + 1); tri.Add(v.Count + 2);
            v.Add(a * Size); v.Add(b * Size); v.Add(c * Size);
        }
        for (int i = 0; i < 5; i++) { T(top, eq[i], eq[(i + 1) % 5]); T(bot, eq[i], eq[(i + 1) % 5]); }
        var m = new Mesh { name = "CrystalFragment" };
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}