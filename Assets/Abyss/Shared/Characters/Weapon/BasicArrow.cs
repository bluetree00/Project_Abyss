using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicArrow : MonoBehaviour
{
    [SerializeField] private float speed = 30f;
    [SerializeField] private float damage = 20f;
    [SerializeField] private float lifetime = 5f;
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

        // 발사 방향으로 회전
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction);
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

        gameObject.SetActive(false);
    }
}
