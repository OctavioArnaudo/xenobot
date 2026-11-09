// Poner en Assets/Editor/StructureTools.cs (requiere NightFx.cs FUERA de Editor). Menu: Tools/Terrain/10-13
// Mallas y materiales se generan UNA vez en Assets/Structures y se reutilizan (borra la carpeta para regenerar).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

static class StructureTools
{
    const string Dir = "Assets/Structures";
    const float PR = 12f, ShipH = 160f, AntH = 100f, Tilt = 12f, BeamLen = 2500f, BeamR = 160f;

    [MenuItem("Tools/Terrain/10 Spaceship + Launch Tower (en vista)")]
    static void Ship() => Spawn("Spaceship", ShipMesh, Hull, Dark);

    [MenuItem("Tools/Terrain/11 Radio Antenna (en vista)")]
    static void Antenna() { Fx(); Spawn("RadioAntenna", AntMesh, Hull, Red, LampW, LampR); }

    [MenuItem("Tools/Terrain/12 Searchlights (centro del terreno)")]
    static void Lights()
    {
        var ts = Terrain.activeTerrains;
        if (ts.Length == 0) { Debug.LogWarning("[Terrain] No hay terrenos."); return; }
        var b = new Bounds(ts[0].transform.position + ts[0].terrainData.size * .5f, ts[0].terrainData.size);
        foreach (var t in ts) b.Encapsulate(new Bounds(t.transform.position + t.terrainData.size * .5f, t.terrainData.size));

        var fx = Fx();
        for (int i = fx.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(fx.transform.GetChild(i).gameObject);
        var head = Get("SearchlightHead", Head); var beam = Get("Beam", BeamMesh);
        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject("Searchlight" + i);
            go.transform.SetParent(fx.transform);
            go.transform.SetPositionAndRotation(Ground(b.center + Vector3.right * (i == 0 ? -15f : 15f)), Quaternion.Euler(0f, i == 0 ? 180f : 0f, 0f));
            Rend(go, head, false, Dark, Lens);
            var bm = new GameObject("Beam");
            bm.transform.SetParent(go.transform, false);
            bm.transform.localPosition = new Vector3(0f, 6f, 0f);
            bm.transform.localRotation = Quaternion.Euler(0f, 0f, -Tilt);
            Rend(bm, beam, false, Beam);
            Undo.RegisterCreatedObjectUndo(go, "Searchlights");
        }
        fx.Collect();
        EditorUtility.SetDirty(fx);
    }

    [MenuItem("Tools/Terrain/13 Portal Platform (en vista)")]
    static void Portal()
    {
        var go = Spawn("PortalPlatform", PortalMesh, Dark, Steel, PGlow, POrange);
        go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;   // escaleras y suelo transitables
        var bm = new GameObject("PortalBeam");                                                    // haz atravesable: sin collider solido
        bm.transform.SetParent(go.transform, false);
        bm.transform.localPosition = new Vector3(0f, PR * .1f, 0f);
        Rend(bm, Get("PortalBeam", PortalBeamMesh), false, PBeam);
        var c = bm.AddComponent<CapsuleCollider>();                                               // trigger para tu teletransporte
        c.isTrigger = true; c.radius = PR * .55f; c.height = PR * 1.1f; c.center = new Vector3(0f, PR * .55f, 0f);
    }

    // ---------- escena ----------
    static GameObject Spawn(string name, System.Func<Mesh> mesh, params Material[] mats)
    {
        var go = new GameObject(name);
        go.transform.position = Ground(View());
        Rend(go, Get(name, mesh), true, mats);
        Undo.RegisterCreatedObjectUndo(go, name);
        Selection.activeGameObject = go;
        return go;
    }

    static void Rend(GameObject go, Mesh m, bool shadow, params Material[] mats)
    {
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = shadow;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    static NightFx Fx()
    {
        var fx = Object.FindFirstObjectByType<NightFx>();
        if (!fx) { fx = new GameObject("NightFx").AddComponent<NightFx>(); Undo.RegisterCreatedObjectUndo(fx.gameObject, "NightFx"); }
        fx.white = LampW; fx.red = LampR; fx.lens = Lens; fx.beam = Beam;
        EditorUtility.SetDirty(fx);
        return fx;
    }

    static Vector3 View() { var sv = SceneView.lastActiveSceneView; return sv ? sv.pivot : Vector3.zero; }

    static Vector3 Ground(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) { p.y = o.y + t.SampleHeight(p); break; }
        }
        return p;
    }

    // ---------- assets compartidos ----------
    static readonly string[] LitSh = { "Universal Render Pipeline/Simple Lit", "Standard" },
                             UnlitSh = { "Universal Render Pipeline/Unlit", "Unlit/Color" },
                             SpriteSh = { "Sprites/Default" };
    static Material Hull => Mat("Hull", new Color(.88f, .9f, .92f), LitSh);
    static Material Dark => Mat("Dark", new Color(.14f, .15f, .17f), LitSh);
    static Material Red => Mat("Red", new Color(.75f, .08f, .06f), LitSh);
    static Material LampW => Mat("LampWhite", Color.white, UnlitSh);
    static Material LampR => Mat("LampRed", new Color(1f, .1f, .1f), UnlitSh);
    static Material Lens => Mat("Lens", new Color(1f, .95f, .8f), UnlitSh);
    static Material Steel => Mat("Steel", new Color(.45f, .48f, .52f), LitSh);
    static Material PGlow => Mat("PortalGlow", new Color(.3f, 1.1f, 1.5f), UnlitSh);
    static Material POrange => Mat("PortalOrange", new Color(1.6f, .55f, .1f), UnlitSh);
    static Material PBeam => Mat("PortalBeamMat", Color.white, SpriteSh);
    static Material Beam => Mat("Beam", new Color(1f, 1f, 1f, 0f), SpriteSh);

    static Material Mat(string n, Color c, string[] sh)
    {
        string p = $"{Dir}/{n}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m) return m;
        Directory.CreateDirectory(Dir);
        Shader s = null;
        foreach (var x in sh) if ((s = Shader.Find(x)) != null) break;
        m = new Material(s) { color = c, enableInstancing = true };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .2f);
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    static Mesh Get(string n, System.Func<Mesh> build)
    {
        string p = $"{Dir}/{n}.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (m) return m;
        Directory.CreateDirectory(Dir);
        m = build(); m.name = n; m.Optimize();
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    // ---------- modelos (submesh 0 = casco/blanco, 1 = oscuro/rojo, 2-3 = lamparas) ----------
    static readonly MB B = new MB();   // listas reutilizadas entre construcciones

    static Mesh ShipMesh()
    {
        float H = ShipH, r = H * .05f, y = 3f;
        B.Clear();
        B.Box(new Vector3(0, -4, 0), new Vector3(H * .5f, 8, H * .5f), Quaternion.identity, 1);          // plataforma enterrada
        B.Cyl(Vector3.zero, 3f, r * 1.4f, r * 1.1f, 16, 1);                                             // soporte
        B.Cyl(new Vector3(0, y, 0), H * .55f, r, r, 20, 0, false); y += H * .55f;                       // etapa 1
        B.Cyl(new Vector3(0, y, 0), H * .03f, r * 1.02f, r * 1.02f, 20, 1); y += H * .03f;              // banda
        B.Cyl(new Vector3(0, y, 0), H * .2f, r * .9f, r * .9f, 20, 0, false); y += H * .2f;             // etapa 2
        B.Cyl(new Vector3(0, y, 0), H * .08f, r * .9f, r * .6f, 20, 0, false); y += H * .08f;           // ojiva
        B.Cyl(new Vector3(0, y, 0), H * .14f, r * .6f, r * .03f, 20, 0);
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.PI * .5f * k;
            var dir = new Vector3(Mathf.Cos(a + .785f), 0, Mathf.Sin(a + .785f));
            B.Cyl(dir * r * 1.45f + Vector3.up * 3f, H * .35f, r * .4f, r * .4f, 10, 1, false);          // booster
            B.Cyl(dir * r * 1.45f + Vector3.up * (3f + H * .35f), H * .08f, r * .4f, r * .03f, 10, 0);
            B.Box(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r * 1.7f + Vector3.up * (3f + H * .07f),
                  new Vector3(r * 1.4f, H * .14f, r * .12f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), 1); // aleta
        }

        // torre de lanzamiento: celosia + brazos
        float s = H * .03f, d = r + H * .12f, th = H * .8f, t = H * .012f; const int n = 10;
        Vector3 P(int i, float h) => new Vector3(-d + (i == 1 || i == 2 ? s : -s), h, i > 1 ? s : -s);
        for (int k = 0; k < n; k++)
        {
            float h0 = th * k / n, h1 = th * (k + 1) / n;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                B.Bar(P(i, h0), P(i, h1), t, 1);
                B.Bar(P(i, h1), P(j, h1), t * .6f, 1);
                B.Bar(P(i, h0), P(j, h1), t * .6f, 1);
            }
        }
        foreach (float f in new[] { .3f, .45f, .6f, .75f })
        {
            float x0 = -d + s, x1 = -r;
            B.Box(new Vector3((x0 + x1) * .5f, H * f, 0), new Vector3(x1 - x0, t * 1.2f, H * .02f), Quaternion.identity, 1);
        }
        B.Bar(new Vector3(-d, th, 0), new Vector3(-d, th + H * .07f, 0), t * .5f, 1);
        return B.Build(2);
    }

    static Mesh AntMesh()
    {
        float A = AntH, b = A * .07f, tp = A * .014f, t = A * .012f; const int n = 9;
        B.Clear();
        Vector3 P(int i, float f) { float w = Mathf.Lerp(b, tp, f); return new Vector3(i == 1 || i == 2 ? w : -w, f * A * .9f, i > 1 ? w : -w); }
        for (int k = 0; k < n; k++)
        {
            float f0 = (float)k / n, f1 = (k + 1f) / n; int sb = k % 2;   // tramos alternos blanco/rojo
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                B.Bar(P(i, f0), P(i, f1), t, sb);
                B.Bar(P(i, f1), P(j, f1), t * .6f, sb);
                B.Bar(P(i, f0), P(j, f1), t * .6f, sb);
            }
        }
        B.Box(new Vector3(0, A * .9f, 0), new Vector3(tp * 2.6f, t, tp * 2.6f), Quaternion.identity, 0);
        B.Bar(new Vector3(0, A * .9f, 0), new Vector3(0, A * .99f, 0), t * .7f, 1);
        B.Cyl(new Vector3(0, A * .93f, 0), A * .02f, A * .014f, A * .01f, 6, 3);   // luz roja
        B.Cyl(new Vector3(0, A * .99f, 0), A * .02f, A * .014f, A * .01f, 6, 2);   // luz blanca (punta)
        return B.Build(4);
    }

    static Mesh Head()
    {
        B.Clear();
        B.Box(new Vector3(0, 1, 0), new Vector3(7, 10, 7), Quaternion.identity, 0);                    // pedestal
        B.M = Matrix4x4.TRS(new Vector3(0, 6, 0), Quaternion.Euler(0, 0, -Tilt), Vector3.one);          // cabeza inclinada
        B.Cyl(new Vector3(0, -1, 0), 6f, 2.6f, 4.5f, 16, 0, false);                                      // reflector
        B.Cyl(new Vector3(0, 4.9f, 0), .1f, 4.3f, 4.3f, 16, 1);                                          // lente
        return B.Build(2);
    }

    // subs: 0 oscuro, 1 acero, 2 brillo cian, 3 brillo naranja. Todo en 12 lados.
    static Mesh PortalMesh()
    {
        float R = PR; const int N = 12;
        B.Clear();
        B.Cyl(new Vector3(0, -3, 0), 3f + R * .15f, R, R * .93f, N, 0);                         // base (enterrada)
        B.Cyl(new Vector3(0, R * .15f, 0), R * .17f, R * .85f, R * .8f, N, 1, false);           // anillo
        B.Ring(R * .32f, R * .6f, R * .8f, N, 0);                                                // cubierta con hueco
        B.Ring(R * .325f, R * .57f, R * .6f, N, 3);                                              // borde naranja
        B.Cyl(new Vector3(0, R * .1f, 0), R * .22f, R * .6f, R * .6f, N, 1, false, true);       // pared interior
        B.Cyl(new Vector3(0, R * .1f, 0), R * .02f, R * .6f, R * .6f, N, 2);                     // suelo luminoso
        B.Cyl(new Vector3(0, R * .07f, 0), R * .03f, R * .975f, R * .965f, N, 3, false);         // franja naranja
        B.Cyl(new Vector3(0, R * .2f, 0), R * .03f, R * .842f, R * .833f, N, 2, false);          // franja cian
        for (int i = 0; i < 5; i++)                                                              // escalera (+Z)
        {
            float top = R * .32f * (5 - i) / 5f;
            B.Box(new Vector3(0, top * .5f, R * (.85f + .1f * i)), new Vector3(R * .24f, top, R * .1f), Quaternion.identity, 1);
        }
        for (int sx = -1; sx <= 1; sx += 2)
            B.Bar(new Vector3(sx * R * .14f, R * .4f, R * .78f), new Vector3(sx * R * .14f, R * .02f, R * 1.32f), R * .03f, 0);
        var path = new[] { new Vector2(-.85f, .1f), new Vector2(-.92f, .6f), new Vector2(-.9f, 1.1f), new Vector2(-.75f, 1.5f), new Vector2(-.45f, 1.72f), new Vector2(-.12f, 1.6f) };
        for (int k = 0; k < path.Length - 1; k++)                                                // arco trasero
        {
            Vector3 p = new Vector3(0, path[k].y, path[k].x) * R, q = new Vector3(0, path[k + 1].y, path[k + 1].x) * R, d = q - p;
            B.Box((p + q) * .5f, new Vector3(R * .3f, d.magnitude * 1.1f, R * .16f), Quaternion.FromToRotation(Vector3.up, d), 1);
        }
        B.Ring(R * 1.2f, 0, R * .65f, N, 1, false);                                              // platillo: cara inferior
        B.Ring(R * 1.19f, 0, R * .4f, N, 2, false);                                              // emisor inferior
        B.Cyl(new Vector3(0, R * 1.2f, 0), R * .18f, R * .65f, R * .8f, N, 1, false);
        B.Cyl(new Vector3(0, R * 1.29f, 0), R * .05f, R * .73f, R * .772f, N, 3, false);
        B.Cyl(new Vector3(0, R * 1.38f, 0), R * .1f, R * .8f, R * .5f, N, 1, false);
        B.Cyl(new Vector3(0, R * 1.48f, 0), R * .08f, R * .5f, R * .2f, N, 1);
        return B.Build(4);
    }

    // Cilindro con franjas de alfa alternas y desvanecido arriba/abajo: 1 malla, 1 draw call, sin textura.
    static Mesh PortalBeamMesh()
    {
        const int S = 12;
        float[] f = { 0f, .15f, .85f, 1f }, a = { 0f, 1f, 1f, 0f };
        var v = new List<Vector3>(); var c = new List<Color>(); var tr = new List<int>();
        for (int i = 0; i < 4; i++)
            for (int s = 0; s < S; s++)
            {
                float ang = Mathf.PI * 2f * s / S;
                v.Add(new Vector3(Mathf.Cos(ang) * PR * .58f, f[i] * PR * 1.1f, Mathf.Sin(ang) * PR * .58f));
                c.Add(new Color(.3f, .85f, 1f, a[i] * (s % 2 == 0 ? .45f : .12f)));
            }
        for (int i = 0; i < 3; i++)
            for (int s = 0; s < S; s++)
            {
                int b = i * S, n = (s + 1) % S;
                tr.AddRange(new[] { b + s, b + n, b + S + s, b + n, b + S + n, b + S + s });
            }
        var m = new Mesh();
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(tr, 0);
        m.RecalculateBounds();
        return m;
    }

    // Dos conos anidados (exterior suave + nucleo) con alfa en vertices: 1 malla, 1 draw call por haz.
    static Mesh BeamMesh()
    {
        const int S = 14;
        float[] f = { 0f, .3f, 1f }, a = { 1f, .55f, 0f };
        var v = new List<Vector3>(); var c = new List<Color>(); var tr = new List<int>();
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < 3; i++)
                for (int s = 0; s < S; s++)
                {
                    float ang = Mathf.PI * 2f * s / S, r = Mathf.Lerp(4f, BeamR, f[i]) * (k == 0 ? 1f : .4f);
                    v.Add(new Vector3(Mathf.Cos(ang) * r, 5f + f[i] * BeamLen, Mathf.Sin(ang) * r));
                    c.Add(new Color(.8f, .9f, 1f, a[i] * (k == 0 ? .14f : .3f)));
                }
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < 2; i++)
                for (int s = 0; s < S; s++)
                {
                    int b = (k * 3 + i) * S, n = (s + 1) % S;
                    tr.AddRange(new[] { b + s, b + n, b + S + s, b + n, b + S + n, b + S + s });
                }
        var m = new Mesh();
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(tr, 0);
        m.RecalculateBounds();
        return m;
    }

    // Constructor minimo de mallas low-poly con caras planas (vertices propios por triangulo).
    sealed class MB
    {
        public Matrix4x4 M = Matrix4x4.identity;
        static readonly Vector3[] A = { Vector3.right, Vector3.up, Vector3.forward };
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<int>[] t = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

        public void Clear() { v.Clear(); foreach (var l in t) l.Clear(); M = Matrix4x4.identity; }

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 o, int s)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); c = M.MultiplyPoint3x4(c); o = M.MultiplyVector(o);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), o) < 0f) { var x = b; b = c; c = x; }   // orienta hacia afuera
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c);
            t[s].Add(i); t[s].Add(i + 1); t[s].Add(i + 2);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 o, int s) { Tri(a, b, c, o, s); Tri(a, c, d, o, s); }

        public void Box(Vector3 c, Vector3 sz, Quaternion q, int s)
        {
            for (int i = 0; i < 3; i++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 n = A[i] * sg, m = n * (sz[i] * .5f);
                    Vector3 u = A[(i + 1) % 3] * (sz[(i + 1) % 3] * .5f), w = A[(i + 2) % 3] * (sz[(i + 2) % 3] * .5f);
                    Quad(c + q * (m - u - w), c + q * (m + u - w), c + q * (m + u + w), c + q * (m - u + w), q * n, s);
                }
        }

        public void Bar(Vector3 a, Vector3 b, float th, int s)
        {
            Vector3 d = b - a;
            Box((a + b) * .5f, new Vector3(th, d.magnitude, th), Quaternion.FromToRotation(Vector3.up, d), s);
        }

        public void Ring(float y, float rIn, float rOut, int n, int s, bool up = true)
        {
            var c = new Vector3(0, y, 0);
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * 2f * i / n, a1 = Mathf.PI * 2f * (i + 1) / n;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Quad(c + d0 * rIn, c + d1 * rIn, c + d1 * rOut, c + d0 * rOut, up ? Vector3.up : Vector3.down, s);
            }
        }

        public void Cyl(Vector3 c, float h, float r0, float r1, int n, int s, bool cap = true, bool inv = false)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * 2f * i / n, a1 = Mathf.PI * 2f * (i + 1) / n;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 top = c + Vector3.up * h, t0 = top + d0 * r1, t1 = top + d1 * r1;
                Quad(c + d0 * r0, c + d1 * r0, t1, t0, inv ? -(d0 + d1) : d0 + d1, s);
                if (cap) Tri(top, t0, t1, Vector3.up, s);
            }
        }

        public Mesh Build(int subs)
        {
            var m = new Mesh { indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(v);
            m.subMeshCount = subs;
            for (int i = 0; i < subs; i++) m.SetTriangles(t[i], i);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}