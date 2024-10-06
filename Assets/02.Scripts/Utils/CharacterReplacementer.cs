using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Characterreplacementer : MonoBehaviour
{
     public float spawnRadius = 5f; // 스폰 반경

    public int Spawnobject;

    //플레이어 충돌 확인
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SpawnMonster();
            Destroy(gameObject);
        }
    }

    //주변 범위에 랜덤으로 몬스터 풀 생성 사용
    public void SpawnMonster()
    {
        for (int i = 0; i < Spawnobject; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * spawnRadius;
            randomOffset.y = 0f;
            Vector3 spawnPosition = transform.position + randomOffset;
            GameObject Enemy = ObjectPooler.SpawnFromPool("Monster", spawnPosition);
           
        }
    }
}
