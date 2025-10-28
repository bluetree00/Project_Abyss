using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColliderInstance : MonoBehaviour
{
    public string payloadKey;       // 어떤 스텝의 콜라이더인지
    public float damage;            // 데미지
    public float knockbackMultiplier;
    public float hitInterval;       // 타격 간격
    public WeaponActionType actionType; // Light, Heavy, QSkill
    public GameObject owner;        // 생성자(플레이어 등)
}
