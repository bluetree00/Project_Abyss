using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicArrow : MonoBehaviour
{
    [SerializeField] private float speed = 30f;
    private Vector3 direction;

    public void Fire(Vector3 dir)
    {
        direction = dir.normalized;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        transform.position += direction * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 히트 처리 및 풀로 복귀
        gameObject.SetActive(false);
    }
}
