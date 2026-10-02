using System;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// PhantomRush 분신 오브젝트에 부착하는 컴포넌트.
/// PropertyBlock의 Emission 강도로 페이드인/아웃 연출. (투명도 의존 없음)
/// </summary>
public class DKPhantomClone : MonoBehaviour
{
    private enum Phase { FadeIn, Visible, FadeOut, Done }

    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private static readonly Color GhostBase     = new Color(0.4f, 0.6f, 1.0f, 1f);
    private static readonly Color GhostEmission = new Color(0.5f, 0.7f, 1.5f, 1f);

    private Color                _base     = GhostBase;       // 제 색(반역의 환영 흑백 — 10-03)
    private Color                _emission = GhostEmission;
    private Phase                _phase = Phase.Done;
    private float                _timer;
    private float                _duration;
    private Action               _onFadeOutDone;
    private Renderer[]           _renderers;
    private MaterialPropertyBlock _mpb;

    public void Initialize()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb       = new MaterialPropertyBlock();
        ApplyTint(0f);
    }

    /// <summary>환영 색 — 기본 푸른 유령빛 대신(반역의 환영: 흰 / 검은 환영).</summary>
    public void SetTint(Color baseColor, Color emission)
    {
        _base     = baseColor;
        _emission = emission;
    }

    /// <summary>다 드러난 채 색만 바꿨을 때 — 지금 색으로 바로 다시 칠한다(환영 기수 피격 번쩍임). 페이드 중엔 페이드가 칠한다.</summary>
    public void Repaint()
    {
        if (_phase == Phase.Visible) ApplyTint(1f);
    }

    public void StartFadeIn(float duration)
    {
        _phase    = Phase.FadeIn;
        _duration = Mathf.Max(0.01f, duration);
        _timer    = 0f;
    }

    public void StartFadeOut(float duration, Action onDone = null)
    {
        _phase         = Phase.FadeOut;
        _duration      = Mathf.Max(0.01f, duration);
        _timer         = 0f;
        _onFadeOutDone = onDone;
    }

    private void Update()
    {
        if (_phase == Phase.Done) return;
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.FadeIn:
                ApplyTint(Mathf.Clamp01(_timer / _duration));
                if (_timer >= _duration) _phase = Phase.Visible;
                break;
            case Phase.FadeOut:
                ApplyTint(1f - Mathf.Clamp01(_timer / _duration));
                if (_timer >= _duration)
                {
                    _phase = Phase.Done;
                    _onFadeOutDone?.Invoke();
                }
                break;
        }
    }

    private void ApplyTint(float t)
    {
        if (_renderers == null) return;
        Color baseCol = Color.Lerp(Color.clear,  _base,     t);
        Color emiCol  = Color.Lerp(Color.black,  _emission, t);
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId,     baseCol);
            _mpb.SetColor(EmissionColorId, emiCol);
            r.SetPropertyBlock(_mpb);
        }
    }
}
}
