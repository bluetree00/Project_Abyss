using RelicFairy.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 적을 한 점 쪽으로 짧게 끌어당긴다(유물 성장 v2 — 서광의 각인 · 저무는 해 노을 지대, 명조 데니아 「침식 영역」 결).
/// 순간 이동이 아니라 0.25초에 걸쳐 NavMesh 위로 미끄러진다(agent.Move — 벽을 뚫지 않는다). 보스는 끌지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class RelicPullMotion : MonoBehaviour
{
    private NavMeshAgent _agent;
    private Vector3 _target;
    private float   _speed;
    private float   _until;

    private void Awake() => TryGetComponent(out _agent);

    /// <summary><paramref name="mb"/>를 <paramref name="toward"/> 쪽으로 최대 <paramref name="distance"/>m, <paramref name="duration"/>초에 걸쳐.</summary>
    public static void Pull(MonsterBase mb, Vector3 toward, float distance, float duration = 0.25f)
    {
        if (mb == null || mb.IsDead || mb.Grade == MonsterGrade.Boss || distance <= 0f) return;
        Vector3 from = mb.transform.position;
        Vector3 d = toward - from; d.y = 0f;
        float len = d.magnitude;
        if (len < 0.6f) return;   // 이미 붙어 있다
        float move = Mathf.Min(distance, len - 0.6f);
        if (!mb.TryGetComponent<RelicPullMotion>(out var p)) p = mb.gameObject.AddComponent<RelicPullMotion>();
        p._target = from + d / len * move;
        p._speed  = move / Mathf.Max(0.05f, duration);
        p._until  = Time.time + duration;
        p.enabled = true;
    }

    private void Update()
    {
        if (Time.time >= _until) { enabled = false; return; }
        Vector3 pos = transform.position;
        Vector3 d = _target - pos; d.y = 0f;
        float step = _speed * Time.deltaTime;
        if (d.sqrMagnitude <= step * step) { enabled = false; step = d.magnitude; }
        if (step <= 0f) return;
        Vector3 delta = d.normalized * step;
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Move(delta);
        else transform.position = pos + delta;
    }
}
