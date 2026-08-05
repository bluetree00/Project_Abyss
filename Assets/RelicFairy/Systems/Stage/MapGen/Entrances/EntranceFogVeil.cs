using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 방 진입 순간 <b>안개를 짙게 덮었다가 걷어내는</b> 베일.
///
/// 절차 방은 화면이 복귀한 뒤에도 블록 디졸브가 진행돼, 플레이어가 방이 조립되는 과정을
/// 그대로 보게 된다 — "대충 만든 방"처럼 읽히는 원인. 짙은 안개로 그 구간을 덮고,
/// 안개가 걷힐 때는 방이 이미 완성돼 있게 만든다("생성"이 아니라 "드러남").
///
/// ⚠️ 안개 <b>색은 건드리지 않는다.</b> 챕터별 라이팅이 정한 색을 그대로 쓰므로 톤이 자동으로 일치한다.
/// 밀도(또는 Linear 모드의 끝거리)만 일시적으로 당겼다가 원래 값으로 복원한다.
/// </summary>
public static class EntranceFogVeil
{
    // ⚠️ 과하면 진입 직후 <b>본 방까지</b> 하얗게 덮여 답답하다. 조립 구간만 흐리는 정도로 억제한다.
    private const float DensityMult  = 2.5f;    // 원래 밀도 대비 베일 배수
    private const float MinDensity   = 0.022f;  // 안개가 꺼져 있던 씬에서 쓸 최소 밀도
    private const float LinearShrink = 0.35f;   // Linear 모드에서 끝거리를 당기는 비율

    /// <summary>짙은 안개로 시작해 clearSeconds에 걸쳐 원래 값으로 걷는다. 취소·예외 시에도 반드시 복원한다.</summary>
    public static async UniTask PlayAsync(float clearSeconds, CancellationToken ct)
    {
        bool    hadFog   = RenderSettings.fog;
        FogMode mode     = RenderSettings.fogMode;
        float   density0 = RenderSettings.fogDensity;
        float   end0     = RenderSettings.fogEndDistance;

        // Linear인데 끝거리가 사실상 0이면 비율 조정이 무의미하다 → 밀도 경로로 처리.
        bool linear = mode == FogMode.Linear && end0 > 1f;

        float veilDensity = Mathf.Max(MinDensity, density0 * DensityMult);
        float veilEnd     = end0 * LinearShrink;

        RenderSettings.fog = true;
        if (linear) RenderSettings.fogEndDistance = veilEnd;
        else        RenderSettings.fogDensity     = veilDensity;

        try
        {
            float dur = Mathf.Max(0.01f, clearSeconds);
            float t   = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;              // 팝업 등으로 시간이 멈춰도 안개는 걷힌다
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                if (linear) RenderSettings.fogEndDistance = Mathf.Lerp(veilEnd, end0, k);
                else        RenderSettings.fogDensity     = Mathf.Lerp(veilDensity, density0, k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 방 전환이 중간에 끊겨도 씬 안개 설정이 오염된 채 남지 않게 원복.
            RenderSettings.fog            = hadFog;
            RenderSettings.fogDensity     = density0;
            RenderSettings.fogEndDistance = end0;
        }
    }
}
