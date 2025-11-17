using UnityEngine;

public class ColliderInstance : MonoBehaviour
{
    public string payloadKey;       // 어떤 스텝의 콜라이더인지
    public float damage;            // 데미지
    public float knockbackMultiplier;
    public float hitInterval;       // 타격 간격
    public WeaponActionType actionType; // Light, Heavy, QSkill
    public GameObject owner;        // 생성자(플레이어 등)
    public float duration = 1f;     // 지속시간

    private float _elapsedTime;

    private void OnEnable()
    {
        _elapsedTime = 0f;
    }

    //TODO:추후 풀러로 반환하는것으로 교체하기
    private void Update()
    {
        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= duration)
        {
            Destroy(gameObject);
        }
    }
}
