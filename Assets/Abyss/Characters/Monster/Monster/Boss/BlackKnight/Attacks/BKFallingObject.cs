using System.Collections;
using UnityEngine;

/// <summary>
/// RainAttack 운석 VFX 에 동적으로 부착.
/// 지정 목표 위치까지 낙하 후 VFX 재생을 기다려 풀에 반납 (Pool 미설정 시 Destroy).
/// </summary>
public class BKFallingObject : MonoBehaviour
{
    private Vector3 _target;
    private float   _speed;
    private bool    _arrived;

    /// <summary>풀에서 꺼낸 경우 반납 대상 풀. null 이면 Destroy.</summary>
    public BKEffectPool Pool { get; set; }

    /// <param name="targetPos">착지 목표 월드 위치</param>
    /// <param name="fallDuration">낙하 소요 시간 (초)</param>
    public void Init(Vector3 targetPos, float fallDuration)
    {
        _target  = targetPos;
        _arrived = false;
        float dist = Vector3.Distance(transform.position, targetPos);
        _speed = dist / Mathf.Max(0.05f, fallDuration);
    }

    private void Update()
    {
        if (_arrived) return;

        transform.position = Vector3.MoveTowards(
            transform.position, _target, _speed * Time.deltaTime);

        if ((transform.position - _target).sqrMagnitude < 0.04f)
        {
            _arrived = true;
            StartCoroutine(ReturnAfterDelay(1.5f));
        }
    }

    private IEnumerator ReturnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (Pool != null) Pool.Return(gameObject);
        else              Destroy(gameObject);
    }
}
