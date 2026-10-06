using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방에 선 보상 오브젝트 둘레의 등급 빛 — 보상 오브젝트와 <b>따로</b> 서 있는 뿌리에 붙는다(자식이 아니다).
/// 예고 동안엔 빛이 자라고, 보상이 선 뒤엔 그 곁에 머물며(에픽 · 전설은 맥동), 보상이 사라지면
/// 반복 이펙트를 풀에 돌려주고 스스로 사라진다.
/// 보상 오브젝트는 건드리지 않는다 — RoomClearGate가 그 오브젝트의 수명으로 출구 보류를 잰다.
/// </summary>
public sealed class RewardAura : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float GrowFrom   = 0.3f;    // 예고 시작 크기(목표 크기 대비)
    private const float PulseDepth = 0.12f;   // 맥동 진폭
    private const float FadeOut    = 0.4f;    // 사라질 때 남은 입자가 흩어지는 시간

    // ── Private ────────────────────────────────────────────────
    private readonly List<GameObject> _loops      = new(3);
    private readonly List<Vector3>    _baseScales = new(3);
    private GameObject _target;
    private bool       _following;
    private float      _growSeconds;
    private float      _growStart;
    private float      _pulseHz;
    private bool       _dissolved;

    // ── Lifecycle ──────────────────────────────────────────────
    private void Update()
    {
        if (_following && _target == null)
        {
            Dissolve();
            return;
        }

        // 실시간 기준 — 보상 화면이 시간을 멈춘 동안에도 튀지 않는다
        float k = 1f;
        if (_growSeconds > 0f)
            k = Mathf.Lerp(GrowFrom, 1f, Mathf.Clamp01((Time.unscaledTime - _growStart) / _growSeconds));
        if (_pulseHz > 0f)
            k *= 1f + PulseDepth * Mathf.Sin(Time.unscaledTime * _pulseHz * Mathf.PI * 2f);

        for (int i = 0; i < _loops.Count; i++)
            if (_loops[i] != null) _loops[i].transform.localScale = _baseScales[i] * k;
    }

    private void OnDestroy() => ReleaseLoops(0f);   // 방 전환 등으로 먼저 사라질 때 — 반복 이펙트를 남기지 않는다

    // ── Public Methods ─────────────────────────────────────────
    public static RewardAura Create(Vector3 spot)
    {
        var go = new GameObject("RewardAura");
        go.transform.position = spot;
        RoomScopedDrop.Mark(go);   // 방 전환 때 같이 정리
        return go.AddComponent<RewardAura>();
    }

    /// <summary>반복 이펙트 하나를 맡는다(null이면 무시 — 목록에 칸이 없을 때).</summary>
    public void AddLoop(GameObject loop)
    {
        if (loop == null) return;
        _loops.Add(loop);
        _baseScales.Add(loop.transform.localScale);
    }

    /// <summary>지금부터 <paramref name="seconds"/> 동안 작게 시작해 제 크기로 자란다(예고).</summary>
    public void Grow(float seconds)
    {
        _growSeconds = Mathf.Max(0f, seconds);
        _growStart   = Time.unscaledTime;
    }

    /// <summary>보상 오브젝트가 섰다 — 그것이 사라질 때까지 곁에 머문다.</summary>
    public void Follow(GameObject target, float pulseHz)
    {
        _target    = target;
        _following = true;
        _pulseHz   = Mathf.Max(0f, pulseHz);
    }

    /// <summary>반복 이펙트를 흩고 사라진다.</summary>
    public void Dissolve()
    {
        if (_dissolved) return;
        _dissolved = true;
        ReleaseLoops(FadeOut);
        Destroy(gameObject);
    }

    // ── Private Methods ────────────────────────────────────────
    private void ReleaseLoops(float fade)
    {
        for (int i = 0; i < _loops.Count; i++)
        {
            var loop = _loops[i];
            if (loop != null) RunFx.Stop(ref loop, fade);
        }
        _loops.Clear();
        _baseScales.Clear();
    }
}
