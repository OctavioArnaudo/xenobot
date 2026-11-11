// Poner en Assets/Editor (junto a ChargerAI.cs en Assets/Scripts). Menu: Tools/Enemy
// 1) Setup: arma el Animator con los MISMOS parametros que usa ChargerAI, crea el prefab, crea el Agent Type "Charger"
//    (radio/altura del enemigo, pendiente 35) y deja el NavMeshSurface de la escena abierta configurado y horneando.
// 2) Fill: reparte los enemigos sobre el NavMesh.   3) Clear: los borra.   4) Bake: re-hornea (tras cambiar terreno/estructuras).
// Requiere la escena (BiomaScene) abierta con su NavMeshSurface.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runtime.Scripts.Health;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

static class ChargerTools
{
    const string Dir = "Assets/Prefabs/Enemies", FbxName = "Enemigo_Mele_animaciones", FbxPath = "Assets/Prefabs/Enemigo Mele animaciones.fbx";
    const int Count = 20;                 // enemigos que reparte "2 Fill"
    const float Gap = 45f, SafeZone = 60f; // separacion minima entre ellos y distancia minima al objeto "Respawn"

    // Estado del Animator <- nombres posibles del clip (sin mayusculas/espacios/simbolos; gana el primero que exista).
    // Si un clip no te convence, cambia su nombre aqui y vuelve a ejecutar el Setup.
    static readonly (string state, string[] clips)[] Map =
    {
        ("Idle", new[] { "idle" }),
        ("Walk", new[] { "walk", "walk15f" }),
        ("Windup", new[] { "mele18f", "jumpfront28f", "shield" }),   // preparar la carga
        ("Charge", new[] { "dash", "run" }),
        ("Impact", new[] { "smashdown", "meleehit" }),
        ("Stun", new[] { "stunxhit23f", "stun" }),
        ("Hurt", new[] { "takedmg", "takedamage" }),
        ("Die", new[] { "die001", "die" }),
    };

    static string Norm(string n) => new string(n.Substring(n.LastIndexOf('|') + 1).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    static (string, AnimatorConditionMode, float) If(string p) => (p, AnimatorConditionMode.If, 0f);
    static (string, AnimatorConditionMode, float) Not(string p) => (p, AnimatorConditionMode.IfNot, 0f);
    static (string, AnimatorConditionMode, float) Gt(string p, float v) => (p, AnimatorConditionMode.Greater, v);
    static (string, AnimatorConditionMode, float) Lt(string p, float v) => (p, AnimatorConditionMode.Less, v);

    static void Link(AnimatorStateTransition t, float exit, (string p, AnimatorConditionMode m, float v)[] c)
    {
        t.hasExitTime = exit >= 0f; t.exitTime = Mathf.Max(exit, 0f); t.hasFixedDuration = true; t.duration = .1f;
        foreach (var x in c) t.AddCondition(x.m, x.v, x.p);
    }

    static void To(AnimatorState a, AnimatorState b, float exit, params (string p, AnimatorConditionMode m, float v)[] c) => Link(a.AddTransition(b), exit, c);

    static void FromAny(AnimatorStateMachine sm, AnimatorState b, params (string p, AnimatorConditionMode m, float v)[] c)
    {
        var t = sm.AddAnyStateTransition(b); t.canTransitionToSelf = false; Link(t, -1f, c);
    }

    [MenuItem("Tools/Enemy/1 Setup Charger (animator + prefab)")]
    static void Setup()
    {
        // Compara sin espacios/guiones: "Enemigo Mele animaciones.fbx" y "Enemigo_Mele_animaciones.fbx" valen lo mismo.
        string fbx = File.Exists(FbxPath) ? FbxPath : AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => Norm(Path.GetFileNameWithoutExtension(p)) == Norm(FbxName) && p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase));
        if (fbx == null) { Debug.LogError($"[Charger] No encuentro {FbxName}.fbx en el proyecto."); return; }
        Directory.CreateDirectory(Dir);

        // Los clips de movimiento deben hacer loop (si no, Idle/Walk/Run/Stun se congelan al terminar).
        var loopKeys = Map.Where(m => m.state == "Idle" || m.state == "Walk" || m.state == "Charge").SelectMany(m => m.clips).ToArray();
        var im = (ModelImporter)AssetImporter.GetAtPath(fbx);
        var ca = im.clipAnimations.Length > 0 ? im.clipAnimations : im.defaultClipAnimations;
        bool dirty = false;
        foreach (var a in ca)
        {
            string k = Norm(a.name);
            bool want = loopKeys.Contains(k);
            if (want != a.loopTime && (want || Map.Any(m => m.state == "Stun" && m.clips.Contains(k)))) { a.loopTime = want; dirty = true; }   // Stun NO hace loop: suena una sola vez por golpe
        }
        if (dirty) { im.clipAnimations = ca; im.SaveAndReimport(); }

        var all = AssetDatabase.LoadAllAssetsAtPath(fbx);
        var clips = all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        var pick = Map.ToDictionary(m => m.state, m => m.clips.Select(k => clips.FirstOrDefault(c => Norm(c.name) == k)).FirstOrDefault(c => c != null));
        Debug.Log($"[Charger] Clips del FBX: {string.Join(", ", clips.Select(c => c.name))}");
        foreach (var m in Map)
            if (pick[m.state] != null) Debug.Log($"[Charger] {m.state} <- '{pick[m.state].name}' ({pick[m.state].length:0.00} s)");
            else Debug.LogWarning($"[Charger] {m.state}: sin clip. Disponibles: {string.Join(", ", clips.Select(c => c.name))}");

        // Animator: parametros = constantes de ChargerAI, asi codigo y controller no se pueden desincronizar.
        string cp = Dir + "/Charger.controller";
        AssetDatabase.DeleteAsset(cp);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(cp);
        ac.AddParameter(ChargerAI.PSpeed, AnimatorControllerParameterType.Float);
        foreach (var p in new[] { ChargerAI.PWindup, ChargerAI.PImpact, ChargerAI.PHurt }) ac.AddParameter(p, AnimatorControllerParameterType.Trigger);
        foreach (var p in new[] { ChargerAI.PCharging, ChargerAI.PStunned, ChargerAI.PDead }) ac.AddParameter(p, AnimatorControllerParameterType.Bool);
        var sm = ac.layers[0].stateMachine;
        var st = new Dictionary<string, AnimatorState>();
        foreach (var m in Map) { var s = sm.AddState(m.state); s.motion = pick[m.state]; st[m.state] = s; }
        sm.defaultState = st["Idle"];
        st["Charge"].speed = 1.2f;

        FromAny(sm, st["Die"], If(ChargerAI.PDead));
        FromAny(sm, st["Windup"], If(ChargerAI.PWindup), Not(ChargerAI.PDead));
        FromAny(sm, st["Impact"], If(ChargerAI.PImpact), Not(ChargerAI.PDead));
        FromAny(sm, st["Hurt"], If(ChargerAI.PHurt), Not(ChargerAI.PCharging), Not(ChargerAI.PDead));
        To(st["Idle"], st["Walk"], -1f, Gt(ChargerAI.PSpeed, .15f));
        To(st["Walk"], st["Idle"], -1f, Lt(ChargerAI.PSpeed, .1f));
        To(st["Windup"], st["Charge"], -1f, If(ChargerAI.PCharging));
        To(st["Charge"], st["Idle"], -1f, Not(ChargerAI.PCharging));
        To(st["Impact"], st["Stun"], .85f, If(ChargerAI.PStunned));
        To(st["Impact"], st["Idle"], .85f, Not(ChargerAI.PStunned));
        To(st["Stun"], st["Idle"], -1f, Not(ChargerAI.PStunned));
        To(st["Hurt"], st["Stun"], .9f, If(ChargerAI.PStunned));
        To(st["Hurt"], st["Idle"], .9f, Not(ChargerAI.PStunned));
        EditorUtility.SetDirty(ac);

        // Prefab: raiz (tag Enemy) + modelo + collider + NavMeshAgent + HealthController + ChargerAI + red.
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);   // el MISMO fbx da malla, rig y clips: siempre coinciden
        var root = new GameObject("Charger") { tag = "Enemy" };
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        root.layer = vis.layer;
        var an = vis.GetComponentInChildren<Animator>(true);
        if (!an) an = vis.AddComponent<Animator>();
        an.runtimeAnimatorController = ac; an.applyRootMotion = false;
        if (!an.avatar) an.avatar = all.OfType<Avatar>().FirstOrDefault();
        PrefabUtility.RecordPrefabInstancePropertyModifications(an);
        foreach (var mc in vis.GetComponentsInChildren<MeshCollider>(true)) { mc.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(mc); }

        var rs = vis.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.up, new Vector3(1f, 2f, 1f));
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var cap = root.AddComponent<CapsuleCollider>();
        cap.radius = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z) * .8f, .4f, 2f);
        cap.height = Mathf.Max(b.size.y, cap.radius * 2f);
        cap.center = b.center;
        root.AddComponent<Rigidbody>().isKinematic = true;   // collider que se mueve = necesita Rigidbody cinematico

        root.AddComponent<NetworkObject>();
        var ai = root.AddComponent<ChargerAI>();             // agrega solo NavMeshAgent y HealthController
        root.AddComponent<NetworkTransform>();
        root.AddComponent<NetworkAnimator>().Animator = an;
        if (pick["Windup"] != null) ai.windupTime = Mathf.Clamp(pick["Windup"].length, .4f, 1.5f);
        var ag = root.GetComponent<NavMeshAgent>();
        ag.radius = cap.radius; ag.height = cap.height; ag.speed = ai.walkSpeed; ag.acceleration = 8f; ag.angularSpeed = 360f; ag.stoppingDistance = 0f;
        ag.baseOffset = -b.min.y;   // los pies del modelo tocan el NavMesh, sin elevar a mano
        ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        root.GetComponent<HealthController>().team = Team.Enemy;
        int type = AgentType(cap.radius, cap.height);
        ag.agentTypeID = type;
        if (type == 0) ag.radius = Mathf.Min(ag.radius, .5f);   // Humanoid (0,5 m): el agente no puede ser mas ancho que el NavMesh

        var pf = PrefabUtility.SaveAsPrefabAsset(root, Dir + "/Charger.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Selection.activeObject = pf;
        EditorGUIUtility.PingObject(pf);
        Bake(type);
        Debug.Log($"[Charger] Listo: {Dir}/Charger.prefab (modelo: {fbx}). Cuando termine el horneado del NavMesh: Tools/Enemy/2 Fill Terrain with Chargers.");
    }

    // Agent Type "Charger" en ProjectSettings (se crea una vez y se actualiza). Si Unity cambia ese formato, usa Humanoid (0).
    static int AgentType(float r, float h)
    {
        var a = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
        var so = a ? new SerializedObject(a) : null;
        var names = so?.FindProperty("m_SettingNames"); var set = so?.FindProperty("m_Settings");
        if (names == null || set == null) { Debug.LogWarning("[Charger] No pude leer los Agent Types: uso Humanoid. Ajustalo en Window > AI > Navigation > Agents."); return 0; }
        int i = -1;
        for (int k = 0; k < names.arraySize; k++) if (names.GetArrayElementAtIndex(k).stringValue == "Charger") i = k;
        if (i < 0)
        {
            int id = NavMesh.CreateSettings().agentTypeID;
            so.Update();
            for (int k = 0; k < set.arraySize; k++) if (set.GetArrayElementAtIndex(k).FindPropertyRelative("agentTypeID")?.intValue == id) i = k;
            if (i < 0) { Debug.LogWarning("[Charger] No pude crear el Agent Type: uso Humanoid."); return 0; }
            while (names.arraySize <= i) names.InsertArrayElementAtIndex(names.arraySize);
            names.GetArrayElementAtIndex(i).stringValue = "Charger";
        }
        var e = set.GetArrayElementAtIndex(i);
        foreach (var kv in new[] { ("agentRadius", r), ("agentHeight", h), ("agentSlope", 35f), ("agentClimb", .4f) })
        {
            var pr = e.FindPropertyRelative(kv.Item1);
            if (pr != null) pr.floatValue = kv.Item2;
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        return e.FindPropertyRelative("agentTypeID")?.intValue ?? 0;
    }

    // Configura el NavMeshSurface de la escena (agent type + todos los objetos) y lo hornea como el boton Bake.
    // Por reflexion: asi compila igual con cualquier version del paquete AI Navigation.
    static void Bake(int type)
    {
        var sf = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(m => m && m.GetType().Name == "NavMeshSurface");
        if (!sf) { Debug.LogWarning("[Charger] No hay NavMeshSurface en la escena abierta: abre BiomaScene y usa Tools/Enemy/4 Bake NavMesh."); return; }
        var so = new SerializedObject(sf);
        var p = so.FindProperty("m_AgentTypeID"); if (p != null) p.intValue = type;
        p = so.FindProperty("m_CollectObjects"); if (p != null) p.intValue = 0;   // All Game Objects
        so.ApplyModifiedProperties();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sf.gameObject.scene);
        try
        {
            var mgr = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => { try { return x.GetTypes(); } catch { return new System.Type[0]; } }).FirstOrDefault(t => t.Name == "NavMeshAssetManager");
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
            mgr.GetMethod("StartBakingSurfaces", F).Invoke(mgr.GetProperty("instance", F).GetValue(null), new object[] { new Object[] { sf } });
            Debug.Log($"[Charger] Horneando el NavMesh (agent type {type})... espera a que termine y guarda la escena.");
        }
        catch (System.Exception)
        {
            try   // plan B: BuildNavMesh() del Surface (sincrono) y se guarda el resultado como asset
            {
                var t = sf.GetType(); var prop = t.GetProperty("navMeshData");
                var old = prop.GetValue(sf) as Object; string path = old ? AssetDatabase.GetAssetPath(old) : "";
                t.GetMethod("BuildNavMesh", System.Type.EmptyTypes).Invoke(sf, null);
                var nd = prop.GetValue(sf) as Object;
                if (nd && !AssetDatabase.Contains(nd)) AssetDatabase.CreateAsset(nd, path != "" ? path : Dir + "/NavMesh-Surface.asset");
                AssetDatabase.SaveAssets(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sf.gameObject.scene);
                Debug.Log($"[Charger] NavMesh horneado (agent type {type}). Guarda la escena.");
            }
            catch (System.Exception) { Debug.LogWarning("[Charger] No pude hornearlo solo: selecciona Surface y pulsa Bake."); }
        }
    }

    [MenuItem("Tools/Enemy/4 Bake NavMesh (agente Charger)")]
    static void Rebake()
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Charger.prefab");
        Bake(pf ? pf.GetComponent<NavMeshAgent>().agentTypeID : 0);
    }

    [MenuItem("Tools/Enemy/2 Fill Terrain with Chargers")]
    static void Fill()
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Charger.prefab");
        if (!pf) { Debug.LogError("[Charger] Falta el prefab: ejecuta primero Tools/Enemy/1 Setup."); return; }
        var ts = Terrain.activeTerrains;
        if (ts.Length == 0) { Debug.LogError("[Charger] No hay Terrain activo en la escena."); return; }
        if (NavMesh.CalculateTriangulation().vertices.Length == 0) { Debug.LogError("[Charger] No hay NavMesh horneado: ejecuta Tools/Enemy/4 Bake NavMesh y espera a que termine."); return; }

        var qf = new NavMeshQueryFilter { agentTypeID = pf.GetComponent<NavMeshAgent>().agentTypeID, areaMask = NavMesh.AllAreas };
        float off = pf.GetComponent<NavMeshAgent>().baseOffset;   // el agente espera el transform a baseOffset sobre el NavMesh
        var rnd = new System.Random();
        var rp = GameObject.Find("Respawn");
        var pts = new List<Vector3>();
        for (int i = 0; i < Count * 40 && pts.Count < Count; i++)
        {
            var t = ts[rnd.Next(ts.Length)]; Vector3 tp = t.transform.position, sz = t.terrainData.size;
            var p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
            p.y = tp.y + t.SampleHeight(p);
            if (!NavMesh.SamplePosition(p, out var h, 2f, qf)) continue;
            if (pts.Any(q => (q - h.position).sqrMagnitude < Gap * Gap) || (rp && (rp.transform.position - h.position).sqrMagnitude < SafeZone * SafeZone)) continue;
            pts.Add(h.position);
        }

        var grp = GameObject.Find("Chargers");
        if (!grp) { grp = new GameObject("Chargers"); Undo.RegisterCreatedObjectUndo(grp, "Fill Chargers"); }
        foreach (var p in pts)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, grp.transform);
            Undo.RegisterCreatedObjectUndo(g, "Fill Chargers");
            g.transform.SetPositionAndRotation(p + Vector3.up * off, Quaternion.Euler(0f, rnd.Next(360), 0f));
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grp.scene);
        Debug.Log($"[Charger] {pts.Count}/{Count} enemigos en el NavMesh (grupo 'Chargers'). Guarda la escena y dale Play.");
    }

    [MenuItem("Tools/Enemy/3 Clear Chargers")]
    static void Clear()
    {
        var g = GameObject.Find("Chargers");
        if (!g) return;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(g.scene);
        Undo.DestroyObjectImmediate(g);
    }
}