using UnityEngine;
using UnityEngine.SceneManagement;

// Puerta entre escena exterior e interior (carga aditiva; un solo jugador y una sola camara).
// Entrada (exit = false): trigger en el edificio, point = ReturnPoint exterior.
// Salida  (exit = true):  trigger en el interior, point = PlayerSpawn.
// Requisito: el jugador tiene tag "Player" y CharacterController o Rigidbody.
[RequireComponent(typeof(BoxCollider))]
public class SceneDoor : MonoBehaviour
{
    public string scene;
    public bool exit;
    public Transform point;
    public GameObject testCam;   // solo salida: camara de prueba, se apaga si el interior se carga desde otra escena

    static Transform spawn;
    static Vector3 backPos;
    static Quaternion backRot;
    static float until;

    void Awake()
    {
        if (!exit) return;
        spawn = point;
        if (testCam != null && SceneManager.sceneCount > 1) testCam.SetActive(false);
    }

    void OnTriggerEnter(Collider c)
    {
        var t = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform;
        if (Time.time < until || !t.CompareTag("Player")) return;
        if (exit)
        {
            until = Time.time + 1f;
            Move(t, backPos, backRot);
            SceneManager.UnloadSceneAsync(scene);
            return;
        }
        if (SceneManager.GetSceneByName(scene).isLoaded) return;
        until = Time.time + 1f;
        backPos = point.position;
        backRot = point.rotation;
        SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive).completed += _ => Move(t, spawn.position, spawn.rotation);
    }

    static void Move(Transform t, Vector3 p, Quaternion r)
    {
        bool cc = t.TryGetComponent(out CharacterController ch);
        if (cc) ch.enabled = false;
        t.SetPositionAndRotation(p, r);
        if (t.TryGetComponent(out Rigidbody rb)) rb.linearVelocity = Vector3.zero;
        if (cc) ch.enabled = true;
        Physics.SyncTransforms();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!TryGetComponent(out BoxCollider b)) return;
        Color c = exit ? new Color(1f, 0.5f, 0f) : Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(c.r, c.g, c.b, 0.3f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = c;
        Gizmos.DrawWireCube(b.center, b.size);
        if (point == null) return;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawSphere(point.position, 0.3f);
        Gizmos.DrawRay(point.position, point.forward * 1.5f);
        Gizmos.DrawLine(transform.TransformPoint(b.center), point.position);
    }
#endif
}