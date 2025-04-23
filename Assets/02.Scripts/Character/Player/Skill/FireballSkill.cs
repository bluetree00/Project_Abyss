using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Skill/FireballSkill")]
public class FireballSkill : SkillData
{
    public GameObject fireballPrefab;
    public float damage = 50f;

    public override void Activate(GameObject user)
    {
        Debug.Log($"🔥 {skillName} 발동! 데미지: {damage}");

        if (fireballPrefab != null)
        {
            Vector3 spawnPos = user.transform.position + user.transform.forward;
            GameObject fireball = Instantiate(fireballPrefab, spawnPos, Quaternion.identity);
            // 추가 로직: 타겟팅, 이동 등
        }
    }
}
