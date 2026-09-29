using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 도박 상자 상호작용 챌린지(비전투 이벤트방 MVP). 플레이어 앞에 상자를 놓고, F로 개봉하면
/// 결정적 등급(seed 고정 → 리로드해도 동일, save-scum 방지)을 공개하고 RoomClearGate(grade)로 보상+연료를 준 뒤
/// OnResolved를 발행한다(→ RunFlowController가 출구 개방). 개봉 전엔 방이 클리어되지 않아 챌린지 필수.
/// </summary>
public sealed class GambleBoxChallenge : MonoBehaviour, IInteractionChallenge
{
    private const float InteractRange = 2.8f;
    private const float LabelHeight   = 1.4f;
    private const float PromptHeight  = 2.0f;
    /// <summary>이 시간이 지나면 자동 개봉. 개봉(OnResolved) 전까지 출구가 열리지 않으므로 탈출구가 필요하다.</summary>
    private const float AutoOpenSeconds = 60f;

    private float _elapsed;

    private GameRunSession  _run;
    private LuckRollTableSO _luckTable;
    private GameObject      _endEffect, _endEffect2;
    private ChallengeGrade  _grade;

    private bool        _resolved;
    private bool        _placed;
    private Vector3     _boxPos;
    private Transform   _camT;
    private TextMeshPro _label, _prompt;

    public event Action OnResolved;

    /// <summary>SetupEventRoom이 호출. seed는 방 결정적 RNG 파생(리로드 동일 결과).</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO luckTable,
                           GameObject endEffect, GameObject endEffect2, int seed)
    {
        _run        = run;
        _luckTable  = luckTable;
        _endEffect  = endEffect;
        _endEffect2 = endEffect2;
        _grade      = RollGrade(new System.Random(seed));
    }

    private void Update()
    {
        if (_resolved) return;

        var pt = Managers.Player?.PlayerTransform;
        if (pt == null) return;

        if (!_placed)
        {
            _camT = Camera.main != null ? Camera.main.transform : null;
            // pt.forward를 그대로 쓰면 안 된다 — RunFlowController.MovePlayer가 위치만 옮기고
            // 캐릭터 회전은 이전 방 값을 그대로 둔다(카메라만 정렬). 진입 직후 forward가 벽을 향하면
            // 상자가 벽 속에 생겨 F를 누를 수 없고, 개봉 전엔 출구가 안 열리므로 그대로 갇힌다.
            ServiceRoomDecorPlacer.SyncPhysics();
            var inward = ServiceRoomDecorPlacer.ResolveOpenDirection(pt.position, pt.forward);
            _boxPos = ServiceRoomDecorPlacer.TryFindSpot(pt.position, inward, 3f, pt.position.y, out var p)
                      ? p : pt.position;
            CreateVisuals();
            _placed = true;
        }

        Billboard();

        // 자동 개봉 — 어떤 이유로든 상자에 닿지 못해도 방을 떠날 수 있게 하는 안전장치.
        _elapsed += Time.deltaTime;
        if (_elapsed >= AutoOpenSeconds) { Resolve(); return; }

        bool inRange = (pt.position - _boxPos).sqrMagnitude <= InteractRange * InteractRange;
        if (_prompt != null) _prompt.gameObject.SetActive(inRange);
        if (inRange && !UIInputGate.Blocked && Input.GetKeyDown(KeyCode.F)) Resolve();
    }

    private void Resolve()
    {
        _resolved = true;
        if (_prompt != null) _prompt.gameObject.SetActive(false);
        if (_label != null) { _label.text = "개봉!"; _label.color = new Color(1f, 0.85f, 0.3f); }

        // 전투 챌린지와 동일 보상 경로 — grade 주입 → 보상 개수/희귀도 스케일 + 연료 지급.
        var gate = gameObject.AddComponent<RoomClearGate>();
        gate.Initialize(_run, _luckTable, _endEffect, _endEffect2, false, _grade);
        gate.Activate(_boxPos);

        ShowNotice();
        OnResolved?.Invoke();
    }

    // ── Private Methods ───────────────────────────────────
    private static ChallengeGrade RollGrade(System.Random rng)
    {
        // 도박 가중 롤: Fail 10 / Bronze 25 / Silver 35 / Gold 22 / Platinum 8 (하이리스크·하이리턴)
        int r = rng.Next(100);
        if (r < 10) return ChallengeGrade.Fail;
        if (r < 35) return ChallengeGrade.Bronze;
        if (r < 70) return ChallengeGrade.Silver;
        if (r < 92) return ChallengeGrade.Gold;
        return ChallengeGrade.Platinum;
    }

    private void CreateVisuals()
    {
        _label  = MakeText(ChallengeFlavor.GambleName(_run), _boxPos + Vector3.up * LabelHeight, 3f, new Color(0.95f, 0.8f, 0.35f), 10);
        _prompt = MakeText($"<color={UIPalette.GoldHex}>[F]</color> 개봉", _boxPos + Vector3.up * PromptHeight, 4f, Color.white, 11);
        _prompt.gameObject.SetActive(false);
    }

    private TextMeshPro MakeText(string text, Vector3 worldPos, float size, Color color, int order)
    {
        var go = new GameObject("GambleText");
        go.transform.SetParent(transform, false);
        go.transform.position = worldPos;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = order;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private void Billboard()
    {
        if (_camT == null) return;
        if (_label  != null) _label.transform.rotation  = _camT.rotation;
        if (_prompt != null && _prompt.gameObject.activeSelf) _prompt.transform.rotation = _camT.rotation;
    }

    private void ShowNotice()
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice($"도박 결과 — <color=#8fd3ff>{GradeName(_grade)}</color>!");
    }

    private static string GradeName(ChallengeGrade g) => g switch
    {
        ChallengeGrade.Platinum => "플래티넘",
        ChallengeGrade.Gold     => "골드",
        ChallengeGrade.Silver   => "실버",
        ChallengeGrade.Bronze   => "브론즈",
        _                       => "꽝",
    };
}
