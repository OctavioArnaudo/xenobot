// Poner en una carpeta "Editor" (Assets/Editor/TerrainTools.cs). Menú: Tools/Terrain
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

static class TerrainTools
{
    static Terrain[] Selected() => Selection.GetFiltered<Terrain>(SelectionMode.Deep);

    // 1) Todos los terrenos: distancias de dibujo + informe de por qué los árboles pueden no verse.
    [MenuItem("Tools/Terrain/1 Fix Tree Distance + Report")]
    static void Fix()
    {
        var sb = new StringBuilder($"[Terrain] lodBias={QualitySettings.lodBias}\n");
        foreach (var cam in Camera.allCameras) sb.AppendLine($"Camara '{cam.name}' far={cam.farClipPlane}");

        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(t, "Fix Tree Distance");
            t.drawTreesAndFoliage = true;
            t.treeDistance = 5000f;
            t.treeBillboardDistance = 250f;
            t.treeCrossFadeLength = 20f;
            t.treeMaximumFullLODCount = 150;
            t.detailObjectDistance = 150f;
            t.detailObjectDensity = 1f;
            EditorUtility.SetDirty(t);

            var td = t.terrainData;
            sb.AppendLine($"{t.name}: {td.treeInstanceCount} arboles");
            var counts = td.treeInstances.GroupBy(i => i.prototypeIndex).ToDictionary(g => g.Key, g => g.Count());
            for (int i = 0; i < td.treePrototypes.Length; i++)
            {
                var go = td.treePrototypes[i].prefab;
                if (go == null) { sb.AppendLine($"  [{i}] PREFAB NULO (invisible)"); continue; }
                var shaders = go.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null).Select(m => m.shader.name).Distinct();
                sb.AppendLine($"  [{i}] {go.name} x{(counts.TryGetValue(i, out var c) ? c : 0)} Tree={go.GetComponent<Tree>() != null} LOD={go.GetComponent<LODGroup>() != null} shaders={string.Join(",", shaders)}");
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(sb.ToString());
    }

    // 2) Borra TODOS los árboles de los terrenos seleccionados (seleccionar TerrainsPrefab sirve).
    [MenuItem("Tools/Terrain/2 Clear Trees (seleccion)")]
    static void ClearTrees()
    {
        var ts = Selected();
        if (ts.Length == 0) { Debug.LogWarning("[Terrain] Selecciona uno o varios terrenos."); return; }
        foreach (var t in ts)
        {
            Undo.RegisterCompleteObjectUndo(t.terrainData, "Clear Trees");
            t.terrainData.SetTreeInstances(new TreeInstance[0], true);
        }
        Debug.Log($"[Terrain] Arboles borrados en {ts.Length} terrenos.");
    }

    // 3) Pasto automático por pendiente y ruido (textura de pasto generada una sola vez).
    [MenuItem("Tools/Terrain/3 Fill Grass (seleccion)")]
    static void Grass()
    {
        var ts = Selected();
        if (ts.Length == 0) { Debug.LogWarning("[Terrain] Selecciona uno o varios terrenos."); return; }

        var proto = new DetailPrototype
        {
            prototypeTexture = GrassTex(),
            renderMode = DetailRenderMode.GrassBillboard,
            healthyColor = new Color(0.45f, 0.62f, 0.25f),
            dryColor = new Color(0.62f, 0.5f, 0.25f),
            minWidth = 1f,
            maxWidth = 1.6f,
            minHeight = 0.8f,
            maxHeight = 1.4f,
            noiseSpread = 12f
        };

        for (int k = 0; k < ts.Length; k++)
        {
            var t = ts[k];
            var td = t.terrainData;
            EditorUtility.DisplayProgressBar("Pasto", t.name, (float)k / ts.Length);
            Undo.RegisterCompleteObjectUndo(td, "Fill Grass");
            td.detailPrototypes = new[] { proto };

            int n = td.detailWidth;
            var layer = new int[n, n];
            Vector3 p = t.transform.position;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    if (td.GetSteepness(u, v) > 35f) continue;
                    float noise = Mathf.PerlinNoise((p.x + u * td.size.x) * 0.02f, (p.z + v * td.size.z) * 0.02f);
                    layer[y, x] = Mathf.RoundToInt(Mathf.Clamp01((noise - 0.3f) * 3f) * 12f);
                }
            td.SetDetailLayer(0, 0, 0, layer);
        }
        EditorUtility.ClearProgressBar();
    }

    // 4) Une los bordes de TODOS los terrenos de la escena (no depende de la selección).
    //    Detecta vecinos por solapamiento (cualquier tamaño/resolución), alinea posiciones, iguala la altura
    //    mundial del borde y suaviza una franja. Escribe un informe completo en la Console.
    [MenuItem("Tools/Terrain/4 Stitch Edges (todos)")]
    static void Stitch()
    {
        const float MaxGap = 200f;       // hueco máximo entre terrenos que se sigue considerando vecino
        const int Band = 32, Passes = 3; // franja suavizada (muestras) y pasadas para esquinas

        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        int n = ts.Length;
        var sb = new StringBuilder($"[Terrain] Stitch: {n} terrenos\n");
        var right = Fill(n); var up = Fill(n); var left = Fill(n); var down = Fill(n);

        for (int i = 0; i < n; i++)
        {
            Vector3 a = ts[i].transform.position, s = ts[i].terrainData.size;
            sb.AppendLine($"  {Id(ts[i])}: pos=({a.x:F1},{a.y:F1},{a.z:F1}) size=({s.x:F0},{s.y:F0},{s.z:F0}) res={ts[i].terrainData.heightmapResolution}");
            float bestR = MaxGap, bestU = MaxGap;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                Vector3 b = ts[j].transform.position, t = ts[j].terrainData.size;
                float gx = b.x - (a.x + s.x), gz = b.z - (a.z + s.z);
                float ovZ = Mathf.Min(a.z + s.z, b.z + t.z) - Mathf.Max(a.z, b.z);
                float ovX = Mathf.Min(a.x + s.x, b.x + t.x) - Mathf.Max(a.x, b.x);
                if (ovZ > 0.5f * Mathf.Min(s.z, t.z) && Mathf.Abs(gx) <= bestR) { bestR = Mathf.Abs(gx); right[i] = j; }
                if (ovX > 0.5f * Mathf.Min(s.x, t.x) && Mathf.Abs(gz) <= bestU) { bestU = Mathf.Abs(gz); up[i] = j; }
            }
            if (right[i] >= 0) left[right[i]] = i;
            if (up[i] >= 0) down[up[i]] = i;
        }

        for (int i = 0; i < n; i++)
        {
            if (right[i] >= 0) sb.AppendLine($"  X: {Id(ts[i])} -> {Id(ts[right[i]])} hueco={ts[right[i]].transform.position.x - (ts[i].transform.position.x + ts[i].terrainData.size.x):F2}m");
            if (up[i] >= 0) sb.AppendLine($"  Z: {Id(ts[i])} -> {Id(ts[up[i]])} hueco={ts[up[i]].transform.position.z - (ts[i].transform.position.z + ts[i].terrainData.size.z):F2}m");
            if (right[i] < 0 && up[i] < 0 && left[i] < 0 && down[i] < 0) sb.AppendLine($"  SIN VECINOS: {Id(ts[i])} (a mas de {MaxGap}m o sin solape)");
        }

        // 1) Posiciones exactas (solo si los tamaños coinciden), en orden de grilla.
        foreach (int i in Enumerable.Range(0, n).OrderBy(k => ts[k].transform.position.z).ThenBy(k => ts[k].transform.position.x))
        {
            Vector3 a = ts[i].transform.position, s = ts[i].terrainData.size;
            if (right[i] >= 0 && SameSize(ts[i], ts[right[i]])) Snap(ts[right[i]], a.x + s.x, a.z);
            if (up[i] >= 0 && SameSize(ts[i], ts[up[i]])) Snap(ts[up[i]], a.x, a.z + s.z);
        }

        // 2) Alturas.
        var H = new float[n][,];
        for (int i = 0; i < n; i++)
        {
            var td = ts[i].terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Stitch Terrains");
            H[i] = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
        }
        for (int pass = 0; pass < Passes; pass++)
            for (int i = 0; i < n; i++)
            {
                if (right[i] >= 0) { int j = right[i]; Pull(ts[i], H[i], ts[j], H[j], 0, Band); Pull(ts[j], H[j], ts[i], H[i], 1, Band); }
                if (up[i] >= 0) { int j = up[i]; Pull(ts[i], H[i], ts[j], H[j], 2, Band); Pull(ts[j], H[j], ts[i], H[i], 3, Band); }
            }
        for (int i = 0; i < n; i++)
        {
            ts[i].terrainData.SetHeights(0, 0, H[i]);
            ts[i].groupingID = 1;
            ts[i].allowAutoConnect = true;
        }

        // 3) Vecinos.
        for (int i = 0; i < n; i++)
            ts[i].SetNeighbors(left[i] >= 0 ? ts[left[i]] : null, up[i] >= 0 ? ts[up[i]] : null,
                               right[i] >= 0 ? ts[right[i]] : null, down[i] >= 0 ? ts[down[i]] : null);

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(sb.ToString());
    }

    static int[] Fill(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = -1; return a; }
    static string Id(Terrain t) => $"Terrain#{t.transform.GetSiblingIndex()}";
    static bool SameSize(Terrain a, Terrain b) => (a.terrainData.size - b.terrainData.size).sqrMagnitude < 0.01f;

    static void Snap(Terrain t, float x, float z)
    {
        Undo.RecordObject(t.transform, "Stitch Terrains");
        t.transform.position = new Vector3(x, t.transform.position.y, z);
    }

    static float Sample(float[,] H, float u, float v)
    {
        int r = H.GetLength(0);
        float x = Mathf.Clamp01(u) * (r - 1), y = Mathf.Clamp01(v) * (r - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Mathf.Min(x0 + 1, r - 1), y1 = Mathf.Min(y0 + 1, r - 1);
        float fx = x - x0, fy = y - y0;
        return Mathf.Lerp(Mathf.Lerp(H[y0, x0], H[y0, x1], fx), Mathf.Lerp(H[y1, x0], H[y1, x1], fx), fy);
    }

    // Lleva el borde de 'a' (side 0=derecha 1=izquierda 2=arriba 3=abajo) a la altura mundial media con 'b',
    // y reparte el ajuste en una franja con caída suave.
    static void Pull(Terrain a, float[,] A, Terrain b, float[,] B, int side, int band)
    {
        int ra = A.GetLength(0);
        Vector3 pa = a.transform.position, sa = a.terrainData.size, pb = b.transform.position, sb = b.terrainData.size;
        bool alongZ = side < 2;
        for (int k = 0; k < ra; k++)
        {
            float f = k / (ra - 1f);
            float u = alongZ ? (side == 0 ? 1f : 0f) : f;
            float v = alongZ ? f : (side == 2 ? 1f : 0f);
            float bu = (pa.x + u * sa.x - pb.x) / sb.x, bv = (pa.z + v * sa.z - pb.z) / sb.z;
            float along = alongZ ? bv : bu;
            if (along < -0.001f || along > 1.001f) continue;

            float ha = pa.y + Sample(A, u, v) * sa.y;
            float hb = pb.y + Sample(B, bu, bv) * sb.y;
            float delta = (hb - ha) * 0.5f / sa.y;
            for (int i = 0; i < band; i++)
            {
                float t = 1f - (float)i / band, w = t * t * (3f - 2f * t);
                int y = side == 2 ? ra - 1 - i : side == 3 ? i : k;
                int x = side == 0 ? ra - 1 - i : side == 1 ? i : k;
                A[y, x] = Mathf.Clamp01(A[y, x] + delta * w);
            }
        }
    }

    // 5) Pinta TODOS los terrenos con 4 capas (pasto, barro oscuro, tierra, tierra seca clara) según altura,
    //    pendiente y curvatura: valles/bajos oscuros, crestas claras, laderas de tierra. Continuo entre planchas.
    //    Las texturas se generan una vez en Assets/Textures/TerrainPaint (puedes reemplazarlas por las tuyas).
    const float CavGain = 10f;                    // sensibilidad a valles/crestas
    const float SlopeMin = 18f, SlopeMax = 42f;   // grados: de pasto a tierra de ladera

    [MenuItem("Tools/Terrain/5 Paint Terrain (todos)")]
    static void Paint()
    {
        var layers = Layers();
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        int n = ts.Length;
        var H = new float[n][,];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var td = ts[i].terrainData;
            int r = td.heightmapResolution, st = Mathf.Max(1, r / 64);
            H[i] = td.GetHeights(0, 0, r, r);
            for (int y = 0; y < r; y += st)
                for (int x = 0; x < r; x += st)
                {
                    float wy = ts[i].transform.position.y + H[i][y, x] * td.size.y;
                    lo = Mathf.Min(lo, wy); hi = Mathf.Max(hi, wy);
                }
        }

        const float D = 2f;
        for (int i = 0; i < n; i++)
        {
            var t = ts[i]; var td = t.terrainData; var A = H[i];
            EditorUtility.DisplayProgressBar("Pintando terreno", t.name, (float)i / n);
            Undo.RegisterCompleteObjectUndo(td, "Paint Terrain");
            td.terrainLayers = layers;

            int res = td.alphamapResolution;
            var map = new float[res, res, 4];
            Vector3 p = t.transform.position, sz = td.size;
            float eu = D / sz.x, ev = D / sz.z;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
                    float h = Sample(A, u, v), hl = Sample(A, u - eu, v), hr = Sample(A, u + eu, v),
                          hd = Sample(A, u, v - ev), hu = Sample(A, u, v + ev);
                    float gx = (hr - hl) * sz.y / (2f * D), gz = (hu - hd) * sz.y / (2f * D);
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    float cav = Mathf.Clamp((hl + hr + hd + hu - 4f * h) * sz.y / (D * D) * CavGain, -1f, 1f);

                    float wx = p.x + u * sz.x, wz = p.z + v * sz.z;
                    float hn = Mathf.InverseLerp(lo, hi, p.y + h * sz.y);
                    float n1 = Mathf.PerlinNoise(wx * 0.012f + 17f, wz * 0.012f), n2 = Mathf.PerlinNoise(wx * 0.06f, wz * 0.06f + 31f);
                    float sl = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SlopeMin, SlopeMax, slope));
                    float valley = Mathf.Clamp01(cav), ridge = Mathf.Clamp01(-cav);

                    float grass = (1f - sl) * (0.25f + n1) * (1f - 0.8f * valley) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, hn)));
                    float mud = valley * 1.4f + Mathf.Pow(1f - hn, 3f) * n2 * 1.2f;
                    float dirt = 0.15f + sl * 1.5f + n2 * 0.2f;
                    float dry = ridge * 1.4f + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, hn)) * (0.5f + n1);

                    grass = Mathf.Pow(grass, 1.5f); mud = Mathf.Pow(mud, 1.5f); dirt = Mathf.Pow(dirt, 1.5f); dry = Mathf.Pow(dry, 1.5f);
                    float sum = grass + mud + dirt + dry + 1e-5f;
                    map[y, x, 0] = grass / sum; map[y, x, 1] = mud / sum; map[y, x, 2] = dirt / sum; map[y, x, 3] = dry / sum;
                }
            td.SetAlphamaps(0, 0, map);
        }
        EditorUtility.ClearProgressBar();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Terrain] Pintados {n} terrenos. Altura mundial {lo:F0}..{hi:F0}.");
    }

    static TerrainLayer[] Layers()
    {
        const string dir = "Assets/Textures/TerrainPaint";
        Directory.CreateDirectory(dir);
        var defs = new (string name, Color dark, Color light, float crack, float streak, float grain)[]
        {
            ("Grass", new Color(.07f, .17f, .04f), new Color(.42f, .58f, .17f), 0f, .6f, .35f),
            ("Mud",   new Color(.05f, .03f, .02f), new Color(.20f, .12f, .08f), .7f, 0f, .30f),
            ("Dirt",  new Color(.30f, .16f, .08f), new Color(.60f, .36f, .19f), 0f, 0f, .55f),
            ("Dry",   new Color(.58f, .45f, .30f), new Color(.88f, .74f, .54f), .2f, 0f, .35f),
        };
        var res = new TerrainLayer[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            string lp = $"{dir}/{d.name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp);
            if (layer == null)
            {
                Gen(dir, d.name, d.dark, d.light, d.crack, d.streak, d.grain);
                layer = new TerrainLayer
                {
                    diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{d.name}_D.png"),
                    normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{d.name}_N.png"),
                    tileSize = new Vector2(12f, 12f),
                    normalScale = 1.3f,
                    smoothness = 0f
                };
                AssetDatabase.CreateAsset(layer, lp);
            }
            res[i] = layer;
        }
        AssetDatabase.SaveAssets();
        return res;
    }

    // Textura tileable procedural (albedo + normal derivada del relieve).
    static void Gen(string dir, string name, Color c0, Color c1, float crack, float streak, float grain)
    {
        const int S = 512;
        var h = new float[S, S];
        var col = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float t = Fbm(u, v, 3, 3, 4);
                float st = streak > 0f ? Fbm(u, v, 32, 2, 3) : 0.5f;
                float g = Fbm(u, v, 96, 96, 2);
                float cr = crack > 0f ? Mathf.Pow(1f - Mathf.Abs(2f * Fbm(u, v, 5, 5, 3) - 1f), 14f) : 0f;
                Color c = Color.Lerp(c0, c1, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, t)));
                float shade = 1f + (g - 0.5f) * grain + (st - 0.5f) * streak - cr * crack;
                col[y * S + x] = new Color(c.r * shade, c.g * shade, c.b * shade, 0.08f);
                h[x, y] = t * 0.6f + g * 0.25f + (streak > 0f ? st * 0.15f : 0f) - cr * crack * 0.4f;
            }
        Save($"{dir}/{name}_D.png", col, S, false);

        var nm = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = h[(x + 1) % S, y] - h[(x - 1 + S) % S, y], dy = h[x, (y + 1) % S] - h[x, (y - 1 + S) % S];
                Vector3 nv = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                nm[y * S + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }
        Save($"{dir}/{name}_N.png", nm, S, true);
    }

    static void Save(string path, Color[] px, int S, bool normal)
    {
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.anisoLevel = 8;
        if (normal) imp.textureType = TextureImporterType.NormalMap;
        imp.SaveAndReimport();
    }

    // Ruido de valor periódico (tileable); px/py = período por eje.
    static int Mod(int a, int m) => ((a % m) + m) % m;
    static float Hash(int x, int y)
    {
        unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; }
    }
    static float VN(float x, float y, int px, int py)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
        return Mathf.Lerp(Mathf.Lerp(Hash(xa, ya), Hash(xb, ya), fx), Mathf.Lerp(Hash(xa, yb), Hash(xb, yb), fx), fy);
    }
    static float Fbm(float u, float v, int px, int py, int oct)
    {
        float sum = 0f, a = 0.5f, tot = 0f;
        for (int o = 0; o < oct; o++) { sum += a * VN(u * px, v * py, px, py); tot += a; a *= 0.5f; px *= 2; py *= 2; }
        return sum / tot;
    }

    static Texture2D GrassTex()
    {
        const string path = "Assets/Textures/Grass.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int S = 64;
        var px = new Color[S * S];
        for (int b = 0; b < 7; b++)
        {
            float cx = 6 + b * 8.5f, h = 36 + (b * 7 % 24), lean = (b % 2 == 0 ? 1 : -1) * (3 + b % 3);
            for (int y = 0; y < h; y++)
            {
                float f = y / h, w = (1f - f) * 2.6f + 0.4f, xc = cx + lean * f * f;
                var col = Color.Lerp(new Color(0.15f, 0.35f, 0.1f), new Color(0.75f, 0.95f, 0.4f), f);
                for (int x = Mathf.RoundToInt(xc - w); x <= Mathf.RoundToInt(xc + w); x++)
                    if (x >= 0 && x < S) px[y * S + x] = col;
            }
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        Directory.CreateDirectory("Assets/Textures");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}