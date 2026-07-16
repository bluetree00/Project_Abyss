using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 가웨인 낙일 — <b>하늘에서 떨어지는 불덩이</b>.
///
/// 벤더 메테오 프리팹(Effect_10/49)은 낙하체가 전부 스크립트 구동이라 그대로 못 쓴다.
/// 그래서 트레일 딸린 불덩이 VFX를 <b>착탄점 위 하늘에서 지면으로 직접 이동</b>시키고,
/// 닿는 순간 착탄 폭발로 교체한다 — "폭발만" 있던 이전과 달리 진짜 메테오가 된다.
///
/// 착탄 예고는 <b>절차 생성 조준 링</b>이다(개발용 GuidelineVisual 반짝임이 아니라).
/// LineRenderer로 실제 폭발 반경과 <b>정확히 같은 원</b>을 그려 낙하 내내 유지하고,
/// 착탄 순간 확 밝아지며 사라진다 — "여기로, 이만큼 떨어진다"가 명확히 읽힌다.
///
/// 순수 연출이다(피해 없음). 피해·화상·작열 지대는 SolarDescentSkillRuntime이 착탄 타이밍에 따로 넣는다.
/// </summary>
public sealed class SolarMeteor : MonoBehaviour
{
    private const string FallVfxKey   = "vfx_gawain_meteor_fall";   // 트레일 딸린 불덩이(낙하체)
    private const string ImpactVfxKey = "vfx_gawain_solar_impact";  // 착탄 폭발
    private const float  FallScale    = 2.4f;   // 낙하체 크기

    private const float  GroundContactHeight = 5f;      // 이 높이 이하로 내려오면 낙하 피해 시작
    private const float  FallTickInterval    = 0.06f;   // 낙하 피해 틱 간격

    // 조준 링
    private const int    RingSegments = 64;
    private const float  RingWidth    = 0.18f;
    private const float  RingLift     = 0.06f;   // 지면 z-파이팅 방지용 살짝 띄움
    private static readonly Color RingColor       = new(1f, 0.72f, 0.20f, 0.85f);   // 태양빛 주황금
    private static readonly Color RingImpactColor = new(1f, 0.95f, 0.55f, 1f);      // 착탄 순간 백열

    private LineRenderer _ring;

    /// <summary>
    /// impact 지점에 메테오를 떨어뜨린다.
    /// blastRadius: 착탄 폭발 반경 — 조준 링을 이 크기로 그린다(피해 범위와 일치).
    /// onGroundContact: 태양이 지면 근처로 오면 매 틱 현재 위치로 호출(낙하 피해).
    /// onImpact: 착탄 순간 1회(대폭발).
    /// </summary>
    public static void Strike(Vector3 impact, float fromHeight, float fallTime, float impactScale,
                              float blastRadius, Action<Vector3> onGroundContact, Action onImpact)
    {
        var go = new GameObject("SolarMeteor");
        go.transform.position = impact + Vector3.up * fromHeight;
        var m = go.AddComponent<SolarMeteor>();
        m.RunAsync(impact, fromHeight, fallTime, impactScale, blastRadius, onGroundContact, onImpact,
                   go.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid RunAsync(Vector3 impact, float fromHeight, float fallTime, float impactScale,
                                       float blastRadius, Action<Vector3> onGroundContact, Action onImpact,
                                       CancellationToken ct)
    {
        try
        {
            Vector3 start = impact + Vector3.up * fromHeight;

            // 착탄 예고 링 — 실제 폭발 반경으로 정확히 그린다. 낙하 내내 유지.
            BuildRing(impact, blastRadius);

            GameObject fall = null;
            try { fall = await Managers.AddressableManager.InstantiateAsync(FallVfxKey, transform, true); }
            catch (Exception) { /* 키 부재 — 궤적 없이 폭발만이라도 나오게 계속 진행 */ }

            if (fall != null)
            {
                fall.transform.position   = start;
                fall.transform.localScale = Vector3.one * FallScale;
                fall.transform.rotation   = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }

            // 가속 낙하(EaseIn). 지면 근처에 들면 매 틱 낙하 피해를 훑는다.
            // 링은 낙하가 진행될수록 서서히 밝아져 '곧 떨어진다'는 긴장을 준다.
            float t = 0f;
            float dur = Mathf.Max(0.05f, fallTime);
            float tickTimer = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float e = t * t;   // EaseInQuad
                Vector3 pos = Vector3.LerpUnclamped(start, impact, e);
                transform.position = pos;

                SetRingColor(Color.Lerp(RingColor, RingImpactColor, t));

                float height = pos.y - impact.y;
                if (onGroundContact != null && height <= GroundContactHeight)
                {
                    tickTimer -= Time.deltaTime;
                    if (tickTimer <= 0f) { tickTimer = FallTickInterval; onGroundContact(pos); }
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.position = impact;

            // 착탄 — 낙하체를 지우고 폭발로 교체 + 콜백. 링은 백열로 번쩍이고 사라진다.
            if (fall != null) Destroy(fall);
            RelicStateVfx.PlayOneShot(ImpactVfxKey, impact, impactScale);
            onImpact?.Invoke();
            SetRingColor(RingImpactColor);

            await FadeRingOutAsync(0.2f, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (this != null) Destroy(gameObject);
        }
    }

    // ── 조준 링 (절차 생성) ────────────────────────────────────
    private void BuildRing(Vector3 center, float radius)
    {
        var go = new GameObject("AimRing");
        go.transform.position = center + Vector3.up * RingLift;

        _ring = go.AddComponent<LineRenderer>();
        _ring.useWorldSpace   = false;
        _ring.loop            = true;
        _ring.positionCount   = RingSegments;
        _ring.widthMultiplier = RingWidth;
        _ring.numCapVertices  = 2;
        _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring.receiveShadows  = false;
        // 항상 또렷하게 — 조명 무관 가산 발광 머티리얼.
        _ring.material = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < RingSegments; i++)
        {
            float a = (i / (float)RingSegments) * Mathf.PI * 2f;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        SetRingColor(RingColor);
    }

    private void SetRingColor(Color c)
    {
        if (_ring == null) return;
        _ring.startColor = c;
        _ring.endColor   = c;
    }

    private async UniTask FadeRingOutAsync(float time, CancellationToken ct)
    {
        if (_ring == null) return;
        Color from = _ring.startColor;
        float t = 0f;
        while (t < 1f)
        {
            if (_ring == null || ct.IsCancellationRequested) return;
            t += Time.deltaTime / Mathf.Max(0.01f, time);
            var c = from; c.a = Mathf.Lerp(from.a, 0f, t);
            SetRingColor(c);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }
}
