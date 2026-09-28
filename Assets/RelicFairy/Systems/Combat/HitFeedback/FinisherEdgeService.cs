using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스킬 막타 신호 — 화면 <b>가장자리에만</b> 금빛이 아주 잠깐 번졌다 사라진다.
///
/// 사용자 기준(09-25): 플레이어 연출은 자주 보니 흔들림은 「살짝 느껴질 정도」까지만, 더 전하려면 화면 이펙트로.
/// 그래서 막타의 무게는 흔들림 대신 이 신호와 멈칫(히트스톱)이 맡는다.
///
/// <b>HUD 아래</b>에 그린다 — HUD는 스크린 오버레이 캔버스라, 카메라 공간 캔버스는 3D 화면 위·HUD 아래에 깔린다.
/// 전투 중 읽어야 할 HUD(체력·스킬 칸)를 덮지 않는다. FXLayer(HUD 위)는 쓰지 않는다.
///
/// 색은 <b>금</b> — 리치 색 규약에서 「플레이어의 힘」. 빨강(피격·즉시 회피)·보라(보스 마법)는 피해
/// 받는 쪽 피격 비네트와 헷갈리지 않게 한다. URP 비네트는 화면을 색으로 <b>곱해</b> 가장자리가 어두워질 뿐이라
/// 금빛이 안 난다 → 알파로 얹는 캔버스 이미지를 쓴다.
///
/// 호스트·캔버스·텍스처를 런타임에 만든다(에셋 0개). 시간 정지·히트스톱과 무관하게 unscaledTime으로 흐른다.
/// </summary>
public static class FinisherEdgeService
{
    // ── Constants ─────────────────────────────────────────────────
    private const float PeakAlpha    = 0.42f;  // 모서리 최대 불투명도 — 0.30은 밝은 바닥에서 거의 안 보였다(모서리 +1~8, 09-25 실측)
    private const float RiseSeconds  = 0.04f;
    private const float FallSeconds  = 0.26f;
    private const float MinInterval  = 0.20f;  // 막타가 연달아 나도 깜빡이지 않게
    private const float EdgeStart    = 0.55f;  // 중심→가장자리 거리(0~1.41) 중 여기부터 물든다 — 가운데는 비운다
    private const int   TextureSize  = 128;

    private static readonly Color Gold = new(1f, 0.80f, 0.36f, 1f);

    // ── Static ────────────────────────────────────────────────────
    private class Host : MonoBehaviour
    {
        private void LateUpdate() => Tick();
    }

    private static Host      _host;
    private static Canvas    _canvas;
    private static RawImage  _image;
    private static Texture2D _edgeTexture;
    private static bool      _initialized;
    private static float     _pulseStart = -999f;
    private static bool      _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _initialized = false;
        _host        = null;
        _canvas      = null;
        _image       = null;
        _edgeTexture = null;
        _pulseStart  = -999f;
        _active      = false;
        CurrentAlpha = 0f;
    }

    // ── Properties ────────────────────────────────────────────────
    /// <summary>지금 모서리 불투명도(0~PeakAlpha). 실측용.</summary>
    public static float CurrentAlpha { get; private set; }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>막타 신호를 한 번 띄운다. 짧은 간격 안의 반복 호출은 무시한다.</summary>
    public static void Pulse()
    {
        if (!_initialized) Init();
        if (Time.unscaledTime - _pulseStart < MinInterval) return;
        if (!BindCamera()) return;
        _pulseStart = Time.unscaledTime;
        _active     = true;
        _canvas.enabled = true;
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        var go = new GameObject("[FinisherEdgeHost]");
        Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.enabled    = false;

        var imgGo = new GameObject("Edge");
        imgGo.transform.SetParent(go.transform, worldPositionStays: false);
        var rt = imgGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _edgeTexture = BuildEdgeTexture();
        _image = imgGo.AddComponent<RawImage>();
        _image.texture       = _edgeTexture;
        _image.raycastTarget = false;
        _image.color         = new Color(Gold.r, Gold.g, Gold.b, 0f);
    }

    /// <summary>카메라 공간 캔버스는 카메라가 있어야 그려진다. 씬이 바뀌면 카메라도 바뀌므로 매 펄스 확인한다.</summary>
    private static bool BindCamera()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        if (_canvas.worldCamera != cam)
        {
            _canvas.worldCamera   = cam;
            _canvas.planeDistance = cam.nearClipPlane + 0.02f;   // 모든 지오메트리 앞
        }
        return true;
    }

    private static void Tick()
    {
        if (!_active || _image == null) return;

        float t = Time.unscaledTime - _pulseStart;
        float a;
        if (t < RiseSeconds)                    a = t / RiseSeconds;
        else if (t < RiseSeconds + FallSeconds) a = 1f - (t - RiseSeconds) / FallSeconds;
        else
        {
            _active = false;
            _canvas.enabled = false;
            a = 0f;
        }

        a *= a;   // 끝이 빨리 빠지게 — 여운이 남으면 다음 막타와 겹친다
        CurrentAlpha = a * PeakAlpha;
        var c = Gold;
        c.a = CurrentAlpha;
        _image.color = c;
    }

    /// <summary>가운데는 투명, 가장자리로 갈수록 진해지는 알파 텍스처(모서리가 가장 진하다).</summary>
    private static Texture2D BuildEdgeTexture()
    {
        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "FinisherEdge (Runtime)",
        };
        var px = new Color32[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        for (int x = 0; x < TextureSize; x++)
        {
            float u = x / (TextureSize - 1f) * 2f - 1f;
            float v = y / (TextureSize - 1f) * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);                       // 0(중심) ~ 1.41(모서리)
            float k = Mathf.Clamp01((d - EdgeStart) / (1.41421f - EdgeStart));
            byte alpha = (byte)Mathf.RoundToInt(k * k * 255f);
            px[y * TextureSize + x] = new Color32(255, 255, 255, alpha);
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }
}
