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
    public float detectRange = 30f;
    public float walkSpeed = 2.5f, wanderRadius = 40f, idleMin = 1f, idleMax = 4f;
    [Header("Carga")]
    public float windupTime = 0.8f;       // lo fija el Setup con la duracion del clip
    public float chargeSpeed = 22f, chargeAccel = 80f, overshoot = 8f, chargeMax = 45f, chargeTimeout = 3.5f, cooldown = 5f;
    [Header("Impacto")]
    public float shockRadius = 10f, hitRadius = 2.5f, stunTime = 2.5f;
    public int impactDamage = 100;        // al jugador, en el centro (cae hasta 25% en el borde)
    public float smashDamage = 16f;       // a Smashable (arboles/cristales viven 6): el borde del radio todavia los rompe

    enum S { Wander, Windup, Charge, Stun }
    S s;
    float next, until, readyAt, idleUntil, chargeStart;
    NavMeshAgent ag; Animator an; HealthController hp; Transform target;
    UnityAction<int> onHurt; UnityAction onDie;

    static NavMeshPath path;
    static GameObject[] pl = System.Array.Empty<GameObject>();
    static float scanAt;
    static readonly Collider[] buf = new Collider[256];
    static readonly HealthController[] hit = new HealthController[8];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { path = null; pl = System.Array.Empty<GameObject>(); scanAt = 0f; }

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
        an.SetFloat(HSpeed, ag.velocity.magnitude, .1f, Time.deltaTime);
        if (s == S.Windup) { Face(target.position); if (t >= until) Charge(); return; }
        if (s == S.Charge) { if (ChargeHit()) Impact(transform.position + transform.forward * 1.5f); return; }
        if (t < next) return;
        next = t + .2f;
        if (s == S.Wander) Wander(t);
        else if (t >= until) { an.SetBool(HStunned, false); ag.isStopped = false; ag.speed = walkSpeed; ag.acceleration = 8f; s = S.Wander; }
    }

    void Wander(float t)
    {
        var p = Nearest();
        if (p && t >= readyAt && Sees(p.transform)) { Windup(p.transform, t); return; }
        if (ag.pathPending || ag.remainingDistance > .8f) { idleUntil = 0f; return; }   // sigue caminando
        if (idleUntil == 0f) idleUntil = t + Random.Range(idleMin, idleMax);
        if (t < idleUntil) return;
        idleUntil = 0f;
        Vector2 r = Random.insideUnitCircle * wanderRadius;
        Vector3 from = transform.position;
        if (NavMesh.SamplePosition(from + new Vector3(r.x, 0f, r.y), out var h, 20f, NavMesh.AllAreas)
            && NavMesh.CalculatePath(from, h.position, NavMesh.AllAreas, path ??= new NavMeshPath()) && path.status == NavMeshPathStatus.PathComplete) ag.SetPath(path);
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

    // Linea de vista: la bloquea el terreno o las paredes; arboles y cristales no.
    bool Sees(Transform p) =>
        !Physics.Linecast(transform.position + Vector3.up * 1.5f, p.position + Vector3.up, out var h, ~0, QueryTriggerInteraction.Ignore)
        || h.transform.IsChildOf(p) || h.transform.IsChildOf(transform) || h.collider.TryGetComponent<Smashable>(out _);

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

    // El destino se fija UNA vez, pasado el jugador; NavMesh.Raycast lo recorta contra paredes/precipicios del NavMesh.
    void Charge()
    {
        Vector3 p = transform.position, d = target.position - p; d.y = 0f;
        float len = Mathf.Min(d.magnitude + overshoot, chargeMax);
        Vector3 to = p + d.normalized * len;
        if (NavMesh.Raycast(p, to, out var h, NavMesh.AllAreas)) to = h.position;
        else if (NavMesh.SamplePosition(to, out h, 4f, NavMesh.AllAreas)) to = h.position;
        ag.isStopped = false; ag.speed = chargeSpeed; ag.acceleration = chargeAccel; ag.stoppingDistance = 0f; ag.SetDestination(to);
        s = S.Charge; chargeStart = Time.time;
        an.SetBool(HCharging, true);
    }

    // Termina la carga al tocar al jugador, chocar con algo vertical, llegar/frenar o por tiempo.
    bool ChargeHit()
    {
        Vector3 p = transform.position; float el = Time.time - chargeStart;
        if (el > chargeTimeout) return true;
        foreach (var g in pl) if (g && g.activeInHierarchy && (g.transform.position - p).sqrMagnitude < hitRadius * hitRadius) return true;
        if (Physics.Raycast(p + Vector3.up, transform.forward, out var h, 2.5f, ~0, QueryTriggerInteraction.Ignore) && !h.transform.IsChildOf(transform) && h.normal.y < .5f) return true;
        return el > .3f && !ag.pathPending && (!ag.hasPath || ag.remainingDistance < 1f || ag.velocity.sqrMagnitude < 4f);
    }

    void Impact(Vector3 c)
    {
        s = S.Stun; until = Time.time + stunTime; readyAt = until + cooldown;
        ag.velocity = Vector3.zero; ag.ResetPath(); ag.isStopped = true;
        an.SetBool(HCharging, false); an.SetBool(HStunned, true); an.SetTrigger(HImpact);
        ShockRing.Play(c, shockRadius);
        int n = Physics.OverlapSphereNonAlloc(c, shockRadius, buf, ~0, QueryTriggerInteraction.Ignore), done = 0;
        for (int i = 0; i < n; i++)
        {
            var col = buf[i];
            if (col.transform.IsChildOf(transform)) continue;
            float k = 1f - Mathf.Min(Vector3.Distance(col.bounds.center, c) / shockRadius, 1f);   // 1 en el centro, 0 en el borde
            if (col.TryGetComponent(out Smashable sm)) { sm.Damage(smashDamage * (.4f + .6f * k)); continue; }
            var h = col.GetComponentInParent<HealthController>();
            if (!h || h.EffectiveTeam != Team.Player || System.Array.IndexOf(hit, h, 0, done) >= 0) continue;   // 1 golpe por jugador
            if (done < hit.Length) hit[done++] = h;
            h.TakeDamage(Mathf.RoundToInt(impactDamage * Mathf.Lerp(.25f, 1f, k)));
        }
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