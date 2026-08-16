using TMPro;
using UnityEngine;

/// <summary>
/// 보물고 — 비전투 이벤트방. 제한시간 동안 흩어진 상자를 최대한 개봉(욕심 vs 안전).
/// 상자마다 강화재료 소량 즉시 지급. 시간이 다하거나 전부 열면 개봉 수로 등급 확정:
///   5개 Platinum / 4 Gold / 3 Silver / 2 Bronze / ≤1 Fail. 연료(무기강화 자원) 집중 창구.
/// </summary>
public sealed class TreasureVaultChallenge : WorldInteractionChallenge
{
    private const int   ChestCount   = 5;
    private const float TimeLimit    = 18f;
    private const int   FuelPerChest = 6;    // 상자당 강화재료
    private const float Radius       = 4.2f;

    private float       _timeLeft = TimeLimit;
    private int         _opened;
    private bool        _timing;
    private Vector3     _center;
    private TextMeshPro _timerLabel;

    /// <summary>SetupEventRoom이 호출.</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO table, GameObject e1, GameObject e2)
        => InitBase(run, table, e1, e2);

    protected override void PlaceMarkers(Transform player)
    {
        _center = player.position;
        for (int i = 0; i < ChestCount; i++)
        {
            float ang = (Mathf.PI * 2f) * i / ChestCount + 0.4f;
            Vector3 p = _center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Radius;
            AddMarker(p, "보물 상자", UIPalette.Gold, $"<color={UIPalette.GoldHex}>[F]</color> 개봉", i);
        }
        _timerLabel = MakeText($"보물고 · {Mathf.CeilToInt(TimeLimit)}초", _center + Vector3.up * 3.2f,
                               4f, UIPalette.Gold, 12);
        _timing = true;
        Notice($"보물고! <color={UIPalette.GoldHex}>{Mathf.CeilToInt(TimeLimit)}초</color> 안에 상자를 열어라");
    }

    protected override void Tick()
    {
        if (_timerLabel != null && Camera.main != null)
            _timerLabel.transform.rotation = Camera.main.transform.rotation;

        if (!_timing || IsResolved) return;
        _timeLeft -= Time.deltaTime;
        if (_timerLabel != null) _timerLabel.text = $"보물고 · {Mathf.Max(0, Mathf.CeilToInt(_timeLeft))}초";
        if (_timeLeft <= 0f) Resolve();
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null || m.Consumed) return;

        ConsumeMarker(markerIndex);
        SetLabel(markerIndex, "빈 상자", new Color(0.6f, 0.62f, 0.66f));
        _opened++;
        if (Run?.FuelBank != null) Run.FuelBank.Add(FuelKind.EnhanceMaterial, FuelPerChest);

        if (_opened >= ChestCount) Resolve();
    }

    private void Resolve()
    {
        if (IsResolved) return;
        _timing = false;
        if (_timerLabel != null) _timerLabel.gameObject.SetActive(false);

        ChallengeGrade grade = _opened >= 5 ? ChallengeGrade.Platinum
                             : _opened == 4 ? ChallengeGrade.Gold
                             : _opened == 3 ? ChallengeGrade.Silver
                             : _opened == 2 ? ChallengeGrade.Bronze
                             :                ChallengeGrade.Fail;
        Notice($"보물고 종료 — {_opened}/{ChestCount} 개봉 · <color=#8fd3ff>{GradeName(grade)}</color>");
        FinishWith(grade, _center);
    }
}
