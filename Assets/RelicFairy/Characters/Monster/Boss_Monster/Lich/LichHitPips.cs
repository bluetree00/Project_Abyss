using TMPro;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 남은 타격 표식 — 봉인석 · 봉인 조각 머리 위에 떠서 칠 때마다 한 칸씩 찬다(◇◇◇ → ◆◇◇ → ◆◆◇, 10-03).
/// 룬 밝기만으로는 게임 거리에서 몇 번 남았는지 읽히지 않았다. 빈 칸 = 청록(칠 수 있음) · 찬 칸 = 금(플레이어의 봉인) — 리치 색 규약.
/// 월드 글자(TextMeshPro)가 카메라를 본다. 기호는 폰트 화이트리스트 안(◆ ◇) — ● ○는 DNFForgedBlade에 없어 □로 깨진다.
/// 돌의 자식으로 붙어 따라다니고 함께 파괴된다. 디졸브는 월드 글자를 건너뛴다(<see cref="DissolveEffect"/> 제외 규칙).
/// </summary>
public sealed class LichHitPips : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float FontSize = 7f;   // 글자 한 칸 약 0.7 m — 제단 반대편에서도 읽히게

    private static readonly string FullOpen  = "<color=#" + ColorUtility.ToHtmlStringRGB(PatternGuideHelper.PlayerSeal) + ">";
    private static readonly string EmptyOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(PatternGuideHelper.Breakable) + ">";

    // ── Private ───────────────────────────────────────────────────
    private TextMeshPro _text;
    private Transform   _cam;
    private string[]    _labels;   // 찬 칸 수별 글자 — 만들 때 한 번(칠 때마다 문자열을 새로 만들지 않는다)

    // ── Lifecycle ─────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (_cam == null)
        {
            var main = Camera.main;
            if (main == null) return;
            _cam = main.transform;
        }
        transform.rotation = _cam.rotation;   // 카메라를 본다(컷신 샷에서도)
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary><paramref name="parent"/> 위 <paramref name="height"/> m에 <paramref name="total"/>칸 표식을 단다(모두 빈 칸).</summary>
    public static LichHitPips Create(Transform parent, float height, int total)
    {
        var go = new GameObject("LichHitPips");
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshPro>();   // Transform이 RectTransform으로 바뀐다 — 위치는 그 뒤에
        go.transform.localPosition = Vector3.up * height;
        text.fontSize         = FontSize;
        text.alignment        = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.sortingOrder     = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(text);

        var pips = go.AddComponent<LichHitPips>();
        pips._text   = text;
        pips._labels = BuildLabels(Mathf.Max(1, total));
        pips.Set(0);
        return pips;
    }

    /// <summary>찬 칸 수를 보인다.</summary>
    public void Set(int filled) => _text.text = _labels[Mathf.Clamp(filled, 0, _labels.Length - 1)];

    public void Show(bool on)
    {
        if (gameObject.activeSelf != on) gameObject.SetActive(on);
    }

    // ── Private Methods ───────────────────────────────────────────
    private static string[] BuildLabels(int total)
    {
        var labels = new string[total + 1];
        for (int filled = 0; filled <= total; filled++)
            labels[filled] = FullOpen + new string('◆', filled) + "</color>" + EmptyOpen + new string('◇', total - filled) + "</color>";
        return labels;
    }
}
}
