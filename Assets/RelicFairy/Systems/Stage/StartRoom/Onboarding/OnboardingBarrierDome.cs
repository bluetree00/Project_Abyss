using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 온보딩 구역 잠금용 투명 돔 배리어. 솔리드 콜라이더로 접근을 막고, Unlock() 시 콜라이더를 즉시 해제한 뒤
/// 스케일 업 + 알파 페이드(디졸브)로 사라진다. Director가 단계 완료 시 호출.
/// (디졸브 머티리얼이 있으면 그걸 쓰는 게 이상적이나, 여기선 투명 머티리얼 알파 페이드 폴백.)
/// </summary>
public sealed class OnboardingBarrierDome : MonoBehaviour
{
    [SerializeField] private float dissolveDuration = 0.5f;
    [SerializeField] private float dissolveScaleUp = 1.15f;

    private bool _unlocking;

    public void Unlock()
    {
        if (_unlocking) return;
        _unlocking = true;

        // 접근 차단 즉시 해제
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        DissolveAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid DissolveAsync(CancellationToken ct)
    {
        Vector3 from = transform.localScale;
        Vector3 to = from * dissolveScaleUp;
        float dur = Mathf.Max(0.01f, dissolveDuration);
        var renderers = GetComponentsInChildren<Renderer>(true);

        try
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                transform.localScale = Vector3.Lerp(from, to, k);
                float a = 1f - k;
                for (int i = 0; i < renderers.Length; i++) SetAlpha(renderers[i], a);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }

        if (this != null && gameObject != null) gameObject.SetActive(false);
    }

    private static void SetAlpha(Renderer r, float a)
    {
        if (r == null) return;
        var mat = r.material;   // 인스턴스(일회성 디졸브라 허용)
        if (mat.HasProperty("_BaseColor"))
        {
            var c = mat.GetColor("_BaseColor"); c.a = a; mat.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            var c = mat.color; c.a = a; mat.color = c;
        }
    }
}
