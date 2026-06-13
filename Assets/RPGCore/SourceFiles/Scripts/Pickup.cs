using UnityEngine;

public class Pickup : MonoBehaviour
{
    [Header("Ítem")]
    public ItemDefinition item;

    [Header("Effects")]
    public GameObject particleEffectPrefab;

    [Header("Motion")]
    public float rotationSpeed = 100f;
    public float bobbingAmount = 0.1f;
    public float bobbingSpeed = 1f;

    Vector3 _startPos;
    float _timer;
    bool _taken;

    void Start() => _startPos = transform.localPosition;

    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        _timer += Time.deltaTime * bobbingSpeed;
        transform.localPosition = _startPos + new Vector3(0, Mathf.Sin(_timer) * bobbingAmount, 0);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken || !other.CompareTag("Player")) return;
        _taken = true;

        // EXP — no va al inventario, va directo al sistema de stats
        if (item != null && item.expValue > 0f)
        {
            CharacterStats.Instance?.AddExp(item.expValue);
        }
        else
        {
            Inventory.Add(item);
        }

        if (particleEffectPrefab != null)
            Instantiate(particleEffectPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}