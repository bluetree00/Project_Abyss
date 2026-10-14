using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 클립의 수평 이동을 지운다 — 루트 모션 노드가 빈 애니(황소 탱크 · Zectorlab)는 몸(루트 뼈)이 앞으로 걸어 나갔다 돌아왔다
/// (10-14 사용자 「루트 모션 · 포지션이 애니라 다르게 나온다」 · 실측: 달리기 1.9~3.2 m · 내려찍기 1.9 m · 돌진 최대 10.7 m).
/// 애니 적용 뒤(LateUpdate) 루트 뼈의 수평 자리를 처음 자세로 되돌려 몸이 늘 몬스터 자리(이동 · 판정 기준) 위에 있게 한다.
/// 높이는 그대로 둔다(도약 · 웅크림). 가져오기 설정(.meta)은 건드리지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterRootXZLock : MonoBehaviour
{
    private const float ReleaseHold = 0.25f;   // 공격이 끝나도 이만큼 더 잡는다 — 교차 페이드 동안 몸이 튀지 않게

    private Transform _bone;
    private Vector3   _rest;          // 몬스터 기준 처음 자리(애니가 돌기 전 · 묶음 자세)
    private bool      _always = true; // 황소: 늘 · 정예: 공격 동안만(Begin · End)
    private bool      _on;
    private float     _releaseAt;

    /// <summary>처음 켜질 때(애니가 돌기 전) 부른다 — 이미 붙어 있으면 그대로.</summary>
    public static void Attach(GameObject go, string boneName)
    {
        if (go == null || go.TryGetComponent<MonsterRootXZLock>(out _)) return;
        var bone = Find(go.transform, boneName);
        if (bone == null) return;
        var lockComp = go.AddComponent<MonsterRootXZLock>();
        lockComp._bone = bone;
        lockComp._rest = go.transform.InverseTransformPoint(bone.position);
    }

    /// <summary>
    /// 공격 동안만 잡는다 — 지금 자리를 기준으로(10-14 정예 점검: 정예 공격 클립이 몸을 판정보다 1.4~2.0 m 앞으로 내보냈다). 스킨 루트 뼈를 쓴다.
    /// </summary>
    public static void Begin(MonsterBase m)
    {
        if (m == null) return;
        if (!m.TryGetComponent<MonsterRootXZLock>(out var c))
        {
            Transform bone = null;
            foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.rootBone != null) { bone = smr.rootBone; break; }
            if (bone == null) return;
            c = m.gameObject.AddComponent<MonsterRootXZLock>();
            c._bone = bone;
            c._always = false;
        }
        if (c._always) return;
        if (!c._on || Time.time >= c._releaseAt) c._rest = m.transform.InverseTransformPoint(c._bone.position);
        c._on = true;
        c._releaseAt = float.MaxValue;
    }

    /// <summary>공격이 끝났다 — <see cref="ReleaseHold"/>초 뒤에 놓는다.</summary>
    public static void End(MonsterBase m)
    {
        if (m != null && m.TryGetComponent<MonsterRootXZLock>(out var c) && !c._always && c._on)
            c._releaseAt = Time.time + ReleaseHold;
    }

    private void LateUpdate()
    {
        if (_bone == null) return;
        if (!_always)
        {
            if (!_on) return;
            if (Time.time >= _releaseAt) { _on = false; return; }
        }
        Vector3 want = transform.TransformPoint(_rest);
        Vector3 p = _bone.position;
        _bone.position = new Vector3(want.x, p.y, want.z);
    }

    private static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = Find(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
}
