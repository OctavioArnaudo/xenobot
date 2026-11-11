// Poner en Assets/Scripts (FUERA de Editor). Lo deja listo Tools/Enemy/1 Setup Charger: no hay que configurar nada a mano.
// Ciclo: camina al azar por el NavMesh -> detecta al jugador -> prepara (windup) -> carga en linea recta -> al chocar o frenar
// suelta una onda expansiva (dana al jugador, rompe arboles y cristales) -> queda aturdido -> vuelve a patrullar.
// Rendimiento: sin asignaciones en juego (buffers estaticos, NavMeshPath reutilizado, hashes del Animator, anillo con pool).
using System.Collections.Generic;
using Runtime.Scripts.Health;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(NavMeshAgent), typeof(HealthController))]
public class ChargerAI : NetworkBehaviour
{
    // Parametros del Animator: ChargerTools crea el controller con ESTOS nombres (cambiarlos aqui basta).
    public const string PSpeed = "speed", PWindup = "windup", PCharging = "charging", PImpact = "impact", PStunned = "stunned", PHurt = "takeDamage", PDead = "dead";
    static readonly int HSpeed = Animator.StringToHash(PSpeed), HWindup = Animator.StringToHash(PWindup), HCharging = Animator.StringToHash(PCharging),
        HImpact = Animator.StringToHash(PImpact), HStunned = Animator.StringToHash(PStunned), HHurt = Animator.StringToHash(PHurt), HDead = Animator.StringToHash(PDead);

    [Header("Deteccion y patrulla")]
    public float detectRange = 40f;
    public float walkSpeed = 2.5f, wanderRadius = 40f, idleMin = 1f, idleMax = 4f;
    [Header("Carga")]
    public float windupTime = 0.8f;       // lo fija el Setup con la duracion del clip
    public float chargeSpeed = 24f, chargeAccel = 30f, chargeMax = 60f, chargeTimeout = 3.5f, cooldown = 5f;
    [Header("Impacto")]
    public float shockRadius = 18f, hitRadius = 4.5f, stunTime = .9f;
    public int impactDamage = 100;        // al jugador, en el centro (cae hasta 25% en el borde)
    public float plowRadius = 5f;        // durante la carga rompe arboles/cristales a su paso
    public float smashDamage = 16f;       // a Smashable (arboles/cristales viven 6): el borde del radio todavia los rompe

    enum S { Wander, Windup, Charge, Stun }
    S s;
    float next, until, readyAt, idleUntil, chargeStart, plowAt, cv; Vector3 cdir, cfrom;
    NavMeshQueryFilter qf;   // el agente Charger tiene su PROPIO NavMesh: toda consulta debe usar su agentTypeID
    NavMeshAgent ag; Animator an; HealthController hp; Transform target;
    UnityAction<int> onHurt; UnityAction onDie;

    static GameObject[] pl = System.Array.Empty<GameObject>();
    static float scanAt;
    static readonly Collider[] buf = new Collider[512];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pl = System.Array.Empty<GameObject>(); scanAt = 0f; }

    void Awake()
    {
        ag = GetComponent<NavMeshAgent>(); hp = GetComponent<HealthController>(); an = GetComponentInChildren<Animator>();
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        onHurt = _ => { if (s == S.Windup || s == S.Charge) an.ResetTrigger(HHurt); };   // el golpe no interrumpe la coreografia
        onDie = () => an.SetBool(HDead, true);
    }

    void OnEnable()
    {
        hp.OnTakeDamage.AddListener(onHurt); hp.OnDeath.AddListener(onDie);
        qf = new NavMeshQueryFilter { agentTypeID = ag.agentTypeID, areaMask = NavMesh.AllAreas };
        s = S.Wander; next = Time.time + Random.value; readyAt = Time.time + Random.Range(0f, 3f); idleUntil = 0f;
        an.SetBool(HDead, false); an.SetBool(HCharging, false); an.SetBool(HStunned, false);
        if (ag.isOnNavMesh) { ag.isStopped = false; ag.speed = walkSpeed; }
    }

    void OnDisable() { hp.OnTakeDamage.RemoveListener(onHurt); hp.OnDeath.RemoveListener(onDie); }

    // Solo el servidor decide; los clientes reciben posicion y animacion replicadas.
    public override void OnNetworkSpawn() { if (!IsServer) { ag.enabled = false; enabled = false; } }

    void Update()
    {
        float t = Time.time;
        if (!ag.isOnNavMesh)   // fuera del NavMesh: se recoloca en el mas cercano (1 intento/s) en vez de lanzar errores
        {
            if (t >= next) { next = t + 1f; if (NavMesh.SamplePosition(transform.position, out var ph, 50f, qf)) ag.Warp(ph.position); }
            return;
        }
        an.SetFloat(HSpeed, ag.velocity.magnitude, .1f, Time.deltaTime);
        if (s == S.Windup) { Face(target.position); if (t >= until) Charge(); return; }
        if (s == S.Charge) { Run(t); return; }
        if (t < next) return;
        next = t + .2f;
        if (s == S.Wander) Wander(t);
        else if (t >= until) { an.SetBool(HStunned, false); ag.isStopped = false; ag.speed = walkSpeed; ag.acceleration = 8f; s = S.Wander; }
    }

    void Wander(float t)
    {
        var p = Nearest();
        if (p && t >= readyAt) { Windup(p.transform, t); return; }
        if (ag.pathPending || (ag.hasPath && ag.remainingDistance > .8f)) { idleUntil = 0f; return; }   // sigue caminando
        if (idleUntil == 0f) idleUntil = t + Random.Range(idleMin, idleMax);
        if (t < idleUntil) return;
        idleUntil = 0f;
        Vector2 r = Random.insideUnitCircle * wanderRadius;
        Vector3 from = transform.position;
        if (NavMesh.SamplePosition(from + new Vector3(r.x, 0f, r.y), out var h, 20f, qf)) ag.SetDestination(h.position);
    }

    GameObject Nearest()
    {
        if (Time.time >= scanAt) { scanAt = Time.time + 1f; pl = GameObject.FindGameObjectsWithTag("Player"); }   // 1 vez/s para TODOS los enemigos
        GameObject b = null; float m = detectRange * detectRange;
        foreach (var g in pl)
        {
            if (!g || !g.activeInHierarchy) continue;
            float d = (g.transform.position - transform.position).sqrMagnitude;
            if (d < m) { m = d; b = g; }
        }
        return b;
    }

    void Windup(Transform p, float t)
    {
        target = p; s = S.Windup; until = t + windupTime;
        ag.velocity = Vector3.zero; ag.ResetPath(); ag.isStopped = true;
        an.ResetTrigger(HHurt); an.SetTrigger(HWindup);
    }

    void Face(Vector3 w)
    {
        Vector3 d = w - transform.position; d.y = 0f;
        if (d.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
    }

    // La carga es en LINEA RECTA: direccion fijada al terminar el windup, aceleracion progresiva (envion) y ag.Move,
    // que desliza por el NavMesh sin depender de rutas. Termina al tocar al jugador, chocar (se frena), por distancia o tiempo.
    void Charge()
    {
        Vector3 d = target.position - transform.position; d.y = 0f;
        cdir = d.sqrMagnitude > .01f ? d.normalized : transform.forward;
        cv = 0f; cfrom = transform.position; chargeStart = Time.time; s = S.Charge;
        ag.ResetPath(); ag.isStopped = false;
        an.SetBool(HCharging, true);
    }

    void Run(float t)
    {
        cv = Mathf.MoveTowards(cv, chargeSpeed, chargeAccel * Time.deltaTime);
        Face(transform.position + cdir);
        Vector3 before = transform.position;
        ag.Move(cdir * (cv * Time.deltaTime));
        Plow(t);
        float el = t - chargeStart, step = cv * Time.deltaTime;
        bool blocked = el > .5f && (transform.position - before).magnitude < step * .5f;
        if (blocked || el > chargeTimeout || (transform.position - cfrom).sqrMagnitude > chargeMax * chargeMax || NearPlayer())
            Impact(transform.position + cdir * 1.5f);
    }

    bool NearPlayer()
    {
        Vector3 p = transform.position + Vector3.up;
        foreach (var g in pl) if (g && g.activeInHierarchy && (g.transform.position + Vector3.up - p).sqrMagnitude < hitRadius * hitRadius) return true;
        return false;
    }

    // Mientras carga: rompe lo Smashable que tenga delante (10 veces/s).
    void Plow(float t)
    {
        if (t < plowAt) return;
        plowAt = t + .1f;
        int n = Physics.OverlapSphereNonAlloc(transform.position + transform.forward * 1.5f + Vector3.up, plowRadius, buf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (buf[i].TryGetComponent(out Smashable sm)) sm.Damage(smashDamage);
    }

    void Impact(Vector3 c)
    {
        s = S.Stun; until = Time.time + stunTime; readyAt = until + cooldown;
        ag.velocity = Vector3.zero; ag.ResetPath(); ag.isStopped = true;
        an.SetBool(HCharging, false); an.SetBool(HStunned, true); an.SetTrigger(HImpact);
        ShockRing.Play(c, shockRadius);

        // Jugadores: se recorre la lista directamente (la fisica con arboles densos llena el buffer y podia dejar fuera al jugador).
        foreach (var g in pl)
        {
            if (!g || !g.activeInHierarchy) continue;
            float d = Vector3.Distance(g.transform.position + Vector3.up, c);
            if (d > shockRadius) continue;
            var h = g.GetComponentInParent<HealthController>();
            if (h && h.EffectiveTeam == Team.Player) h.TakeDamage(Mathf.RoundToInt(impactDamage * Mathf.Lerp(.25f, 1f, 1f - d / shockRadius)));
        }

        // Arboles y cristales en el radio de la onda.
        int n = Physics.OverlapSphereNonAlloc(c, shockRadius, buf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (buf[i].TryGetComponent(out Smashable sm))
                sm.Damage(smashDamage * (.4f + .6f * (1f - Mathf.Min(Vector3.Distance(buf[i].bounds.center, c) / shockRadius, 1f))));
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, shockRadius);
    }
#endif
}

// Anillo de la onda expansiva con pool (se crean al primer uso y se reciclan, sin Instantiate/Destroy en juego).
public class ShockRing : MonoBehaviour
{
    static readonly Stack<ShockRing> pool = new Stack<ShockRing>();
    float t, r; Material mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => pool.Clear();

    public static void Play(Vector3 p, float radius)
    {
        var s = pool.Count > 0 ? pool.Pop() : Create();
        s.transform.position = p; s.r = radius; s.t = 0f;
        s.gameObject.SetActive(true);
    }

    static ShockRing Create()
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<Collider>());
        var rd = g.GetComponent<Renderer>();
        rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rd.receiveShadows = false;
        var s = g.AddComponent<ShockRing>();
        rd.sharedMaterial = s.mat = new Material(Shader.Find("Sprites/Default"));
        return s;
    }

    void Update()
    {
        if ((t += Time.deltaTime / .45f) >= 1f) { gameObject.SetActive(false); pool.Push(this); return; }
        transform.localScale = new Vector3(1f, .15f, 1f) * (2f * r * t);
        mat.color = new Color(1f, .6f, .2f, .6f * (1f - t));
    }
}