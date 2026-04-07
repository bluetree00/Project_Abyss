using UnityEngine;

public class BasicArrow : MonoBehaviour
{
    [SerializeField] private float speed = 30f;
    [SerializeField] private float damage = 20f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private Vector3 modelRotationOffset = new Vector3(0f, -90f, 0f);
    [SerializeField] private string hitEffectKey = "BlueShootHit";
    [SerializeField] private float hitEffectScale = 0.5f;
    private Vector3 direction;
    private GameObject _instigator;
    private float _timer;

    public void Fire(Vector3 dir, GameObject instigator = null, float dmg = -1f)
    {
        direction = dir.normalized;
        _instigator = instigator;
        if (dmg >= 0f) damage = dmg;
        _timer = lifetime;
        gameObject.SetActive(true);

        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(modelRotationOffset);
    }

    private void Update()
    {
        if (direction == Vector3.zero) return;

        transform.position += direction * speed * Time.deltaTime;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            gameObject.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject == _instigator) return;

        if (other.TryGetComponent<IDamageable>(out var damageable))
            damageable.TakeDamage(damage, _instigator);

        SpawnHitEffect(other);
        gameObject.SetActive(false);
    }

    private async void SpawnHitEffect(Collider other)
    {
        if (string.IsNullOrEmpty(hitEffectKey)) return;

        Vector3 hitPos = other.ClosestPoint(transform.position);
        var fx = await Managers.ObjectPooler.SpawnAsync(
            hitEffectKey, ObjectPoolerManager.PoolType.Effect,
            hitPos, Quaternion.identity);

        if (fx == null) return;
        fx.transform.localScale = Vector3.one * hitEffectScale;
    }
}
