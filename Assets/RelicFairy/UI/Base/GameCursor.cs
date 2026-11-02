using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 마우스 커서(11-02 Q13 사용자 「커서 이미지를 제대로 · 몬스터나 상호작용에도 커서가 반응」).
/// 그동안 OS 기본 화살표였다. 에셋 팩엔 커서 묶음이 없어(쪽 넘김 화살표뿐) UI 톤(얇은 금선 · 상아)에 맞춰 코드로 그린다:
///  · 기본 = 상아 화살표 · 몬스터 위 = 붉은 조준 고리(살아 있는 몬스터) · 상호작용 물체 위 = 금 화살표.
/// 마우스 밑을 0.05초마다 한 번 훑고(콜라이더별 분류는 처음 한 번만 찾아 기억), 창이 입력을 막거나 UI 위면 기본.
/// </summary>
public sealed class GameCursor : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private enum Kind { Default = 0, Attack = 1, Interact = 2 }

    private const float ScanInterval = 0.05f;
    private const float RayLength    = 200f;
    private const int   CacheLimit   = 512;

    private static readonly Color Ivory   = new(0.94f, 0.91f, 0.85f, 1f);
    private static readonly Color Ember   = new(1f, 0.52f, 0.44f, 1f);
    private static readonly Color Outline = new(0.08f, 0.07f, 0.06f, 0.92f);

    // 화살표(32 칸 기준 · y 아래로) — 끝점(1,1)이 클릭 자리
    private static readonly Vector2[] Arrow =
    {
        new(1.5f, 1.5f), new(1.5f, 21f), new(6.5f, 16.5f), new(10f, 24.5f), new(13f, 23.2f), new(9.6f, 15.4f), new(16.5f, 15.4f),
    };

    // [F]로 다루는 물체 — 콜라이더의 부모에 이 중 하나가 있으면 상호작용 커서
    private static readonly Type[] InteractTypes =
    {
        typeof(ClearRewardTrigger), typeof(WorldAwakeningAltar), typeof(WorldCovenantAltar), typeof(WorldSwordAwakening),
        typeof(RelicAltar), typeof(WeaponForgeAltar), typeof(WorldItemDisplay), typeof(StartRoomPickup),   // 시작방 문(StartRoomGate)은 방 전체에 붙어 있어 뺐다(실측 콜라이더 3,235)
        typeof(IntroSwordPickup), typeof(BaseCampRecordDoor), typeof(ShopNpcInteraction), typeof(ChapterSpring),
    };

    // ── Static ────────────────────────────────────────────────
    private static GameCursor s_instance;

    // ── Private Fields ────────────────────────────────────────
    private readonly Texture2D[] _tex = new Texture2D[3];
    private readonly Vector2[]   _hot = new Vector2[3];
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly Dictionary<int, (Kind kind, MonsterBase mb)> _cache = new();
    private int    _shown = -1;
    private int    _builtFor;
    private float  _nextScan;
    private Camera _cam;

    // ── Lifecycle ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_instance != null) return;
        var go = new GameObject("[GameCursor]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<GameCursor>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + ScanInterval;
        int px = PixelSize();
        if (px != _builtFor) Build(px);
        Show(Classify());
    }

    private void OnDestroy()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        for (int i = 0; i < _tex.Length; i++) if (_tex[i] != null) Destroy(_tex[i]);
        if (ReferenceEquals(s_instance, this)) s_instance = null;
    }

    // ── Private Methods ───────────────────────────────────────
    private Kind Classify()
    {
        if (!Cursor.visible || UIInputGate.Blocked) return Kind.Default;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return Kind.Default;
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return Kind.Default;

        int n = Physics.RaycastNonAlloc(_cam.ScreenPointToRay(Input.mousePosition), _hits, RayLength, ~0, QueryTriggerInteraction.Collide);
        Kind best = Kind.Default;
        for (int i = 0; i < n; i++)
        {
            var k = KindOf(_hits[i].collider);
            if (k == Kind.Attack) return Kind.Attack;
            if (k == Kind.Interact) best = Kind.Interact;
        }
        return best;
    }

    /// <summary>콜라이더 분류 — 처음 볼 때 한 번만 부모를 찾는다. 몬스터가 죽었으면 기본.</summary>
    private Kind KindOf(Collider col)
    {
        if (col == null) return Kind.Default;
        int id = col.GetInstanceID();
        if (!_cache.TryGetValue(id, out var e))
        {
            var mb = col.GetComponentInParent<MonsterBase>();
            e = mb != null ? (Kind.Attack, mb) : (IsInteractable(col) ? Kind.Interact : Kind.Default, null);
            if (_cache.Count >= CacheLimit) _cache.Clear();
            _cache[id] = e;
        }
        if (e.kind == Kind.Attack) return e.mb != null && !e.mb.IsDead ? Kind.Attack : Kind.Default;
        return e.kind;
    }

    private static bool IsInteractable(Collider col)
    {
        for (int i = 0; i < InteractTypes.Length; i++)
            if (col.GetComponentInParent(InteractTypes[i]) != null) return true;
        return false;
    }

    private void Show(Kind k)
    {
        if ((int)k == _shown || _tex[(int)k] == null) return;
        _shown = (int)k;
        Cursor.SetCursor(_tex[(int)k], _hot[(int)k], CursorMode.Auto);
    }

    /// <summary>화면 높이에 맞춘 커서 크기 — 1080p 32 · 1440p 40 · 4K 64.</summary>
    private static int PixelSize() => Screen.height >= 2000 ? 64 : Screen.height >= 1400 ? 40 : 32;

    private void Build(int px)
    {
        _builtFor = px;
        float k = px / 32f;
        _tex[(int)Kind.Default]  = Paint(px, k, Ivory, ArrowDistance);
        _tex[(int)Kind.Interact] = Paint(px, k, UIPalette.Gold, ArrowDistance);
        _tex[(int)Kind.Attack]   = Paint(px, k, Ember, RingDistance);
        _hot[(int)Kind.Default]  = _hot[(int)Kind.Interact] = new Vector2(1.5f * k, 1.5f * k);
        _hot[(int)Kind.Attack]   = new Vector2(px * 0.5f, px * 0.5f);
        _shown = -1;
    }

    /// <summary>부호 거리(안 = 음수)로 칠한다 — 안은 채움, 밖 1.6칸은 어두운 테두리, 경계는 반 칸 부드럽게.</summary>
    private static Texture2D Paint(int px, float k, Color fill, Func<Vector2, float> sdf)
    {
        var tex = new Texture2D(px, px, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var buf = new Color32[px * px];
        const float edge = 1.6f;
        for (int y = 0; y < px; y++)
        for (int x = 0; x < px; x++)
        {
            var p = new Vector2((x + 0.5f) / k, (y + 0.5f) / k);   // 32 칸 기준 좌표(y 아래로)
            float d = sdf(p) * k;                                   // 화면 칸 단위
            float inner = Mathf.Clamp01(0.5f - d);
            float outer = Mathf.Clamp01(0.5f - (d - edge * k));
            var c = Color.Lerp(Outline, fill, inner);
            c.a = Mathf.Max(outer * Outline.a, inner * fill.a);
            buf[(px - 1 - y) * px + x] = c;                          // 텍스처는 아래에서 위로
        }
        tex.SetPixels32(buf);
        tex.Apply(false, false);
        return tex;
    }

    private static float ArrowDistance(Vector2 p)
    {
        float d = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = Arrow.Length - 1; i < Arrow.Length; j = i++)
        {
            Vector2 a = Arrow[j], b = Arrow[i];
            d = Mathf.Min(d, SegmentDistance(p, a, b));
            if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
        }
        return inside ? -d : d;
    }

    /// <summary>조준 고리 — 반지름 8.5 · 두께 1.6 고리 + 네 방향 짧은 눈금 + 가운데 점(32 칸 기준).</summary>
    private static float RingDistance(Vector2 p)
    {
        var c = new Vector2(16f, 16f);
        Vector2 v = p - c;
        float ring = Mathf.Abs(v.magnitude - 8.5f) - 0.8f;
        float dot  = v.magnitude - 1.4f;
        float tick = Mathf.Min(
            Mathf.Min(SegmentDistance(p, c + new Vector2(0f, -14f), c + new Vector2(0f, -10.5f)), SegmentDistance(p, c + new Vector2(0f, 10.5f), c + new Vector2(0f, 14f))),
            Mathf.Min(SegmentDistance(p, c + new Vector2(-14f, 0f), c + new Vector2(-10.5f, 0f)), SegmentDistance(p, c + new Vector2(10.5f, 0f), c + new Vector2(14f, 0f)))) - 0.8f;
        return Mathf.Min(ring, Mathf.Min(dot, tick));
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
        return (p - (a + ab * t)).magnitude;
    }
}
