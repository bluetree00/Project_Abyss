using UnityEngine;

public class EffectBehaviour : MonoBehaviour
{
    public float lifetime = 1f; // 이펙트의 생존 시간

    private void OnEnable()
    {
        // 생존 시간이 지나면 이펙트를 풀로 반환하는 코루틴 시작
        StartCoroutine(ReturnToPoolAfterDelay(lifetime));
    }

    private System.Collections.IEnumerator ReturnToPoolAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        Managers.ObjectPooler.ReturnToPool(gameObject); // 풀로 반환
    }

}
