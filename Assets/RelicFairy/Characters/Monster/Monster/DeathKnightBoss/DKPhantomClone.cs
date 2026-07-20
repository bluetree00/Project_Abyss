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
        Color baseCol = Color.Lerp(Color.clear,  GhostBase,     t);
        Color emiCol  = Color.Lerp(Color.black,  GhostEmission, t);
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
