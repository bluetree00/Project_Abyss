using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 신탁의 갈림길 — 비전투 이벤트방. 세 문 중 하나만 선택(되돌릴 수 없음), 나머지는 봉인된다.
/// 10-29 개편(설계 §3) — 문마다 받는 것을 이름 그대로 적고, 고른 것을 그 자리에서 준다(룬 고르기 창 없음).
///   · 강화재료의 문(청): 강화재료 묶음
///   · 금화의 문(금): 금화
///   · 정수의 문(청록): 정수
/// 양은 골드 등급 × 챕터(<see cref="EventRewards"/>) — 고르기만 하는 방이라 성적이 없다.
/// 예전 「재화의 길 — 골드 확정」은 금화가 아니라 골드 <b>등급</b>이라 헷갈렸다(B4). 운명문(등급 도박)은 뺐다 — 운 이벤트는 욕심의 상자 하나로.
/// 어포던스 원칙: 각 문 라벨로 무엇을 주는지 읽힌다. 선택의 무게(비가역).
/// </summary>
public sealed class OracleChoiceChallenge : WorldInteractionChallenge
{
    private const string TagMaterial = "material";
    private const string TagGold     = "gold";
    private const string TagEssence  = "essence";
    private const ChallengeGrade DoorGrade = ChallengeGrade.Gold;
    private const string ArchProp   = "EventProp/OracleArch";   // 10-29 글자만 떠 있던 문에 모델(고딕 돌 아치)
    private const float  ArchHeight = 3.4f;
    private const float  DoorDist   = 4.5f;   // 문 사이 약 4 m(아치 폭 2.5 m 남짓)
    private const string RoomTitle  = "신탁의 갈림길";   // 10-30 문 배지(EventRewards.Title)와 같은 이름 — 장소 테마 이름(갈림길의 고목 …)은 문 배지와 달라 같은 방인지 몰랐다
    private const ChallengeGrade TimeoutGrade = ChallengeGrade.Bronze;   // 시간이 다 되면(WorldInteractionChallenge 자동 해결) 이 몫 금화

    private string _chosen;

    /// <summary>SetupEventRoom이 호출(제물 제단에 낼 대가가 없을 때도 이 방이 선다).</summary>
    public void Initialize(GameRunSession run) => InitBase(run);

    /// <summary>10-30 O-02 — 남은 시간 아래 줄. 아무 문도 안 고르고 시간이 다 되면 받는 것.</summary>
    protected override string AutoResolveHint
        => Loc.F("안 고르면 금화 {0}만 받는다", EventRewards.Gold(TimeoutGrade, EventRewards.Chapter(Run)));

    protected override void PlaceMarkers(Transform player)
    {
        // 10-29 무대(방 가운데) 위 · 입구에서 바라보는 쪽 둥근 줄에 문 셋 — 입구 앞에 세우면 문 하나가 벽 쪽으로 밀렸다(10-29 사진).
        int ch = EventRewards.Chapter(Run);
        var ct = this.GetCancellationTokenOnDestroy();
        Door(StageSpot(-55f, DoorDist), "강화재료의 문", new Color(0.45f, 0.7f, 0.95f),
             Loc.F("<color=#73B3F2>[F]</color> 강화재료 +{0}", EventRewards.Material(DoorGrade, ch)), TagMaterial, ct);
        Door(StageSpot(0f, DoorDist + 0.4f), "금화의 문", new Color(0.95f, 0.82f, 0.4f),
             Loc.F("<color=#F2D26A>[F]</color> 금화 +{0}", EventRewards.Gold(DoorGrade, ch)), TagGold, ct);
        Door(StageSpot(55f, DoorDist), "정수의 문", new Color(0.45f, 0.85f, 0.78f),
             Loc.F("<color=#73D9C7>[F]</color> 정수 +{0}", EventRewards.Essence(DoorGrade, ch)), TagEssence, ct);
        // 10-30 장소 이름 · 무엇을 고르는지를 문 배지와 같은 말로(예전 「갈림길의 고목 — 하나만 고를 수 있다」)
        Notice(Loc.F("{0} — 문 {1}개 중 <color=#B892E6>1개만</color> 연다 · 고르면 나머지는 닫힌다", RoomTitle, AllMarkers.Count));
    }

    /// <summary>문 하나 — 글자 + 아치 모델(10-29 글자만 떠 있었다).</summary>
    private void Door(Vector3 p, string label, Color color, string prompt, string tag, System.Threading.CancellationToken ct)
    {
        AddMarker(p, label, color, prompt, tag);
        SpawnPropAsync(ArchProp, p, FacingEntry(p), ArchHeight, ct).Forget();
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null) return;
        _chosen = (string)m.Tag;

        // 나머지 문 봉인 표시
        for (int i = 0; i < AllMarkers.Count; i++)
            if (i != markerIndex) SetLabel(i, "봉인됨", new Color(0.45f, 0.47f, 0.52f));
        Managers.Sound?.PlayEvent(SoundEvent.OracleChoose);

        SetLabel(markerIndex, "열림", new Color(1f, 0.95f, 0.7f));
        FinishWith(DoorGrade, m.Pos);
    }

    /// <summary>고른 문의 보상. 시간이 다 돼 아무 문도 안 골랐으면 브론즈 몫 금화.</summary>
    protected override UniTask GrantRewardAsync(ChallengeGrade grade, Vector3 center, CancellationToken ct)
    {
        int ch = EventRewards.Chapter(Run);
        Vector3 at = EventRewards.PlayerPosOr(center);
        switch (_chosen)
        {
            case TagMaterial:
            {
                int n = EventRewards.Material(grade, ch);
                EventRewards.AddMaterial(Run, n);
                Notice(Loc.F("강화재료의 문 — 강화재료 <color=#73B3F2>+{0}</color>", n));
                break;
            }
            case TagEssence:
            {
                int n = EventRewards.Essence(grade, ch);
                EventRewards.DropEssence(at, n);
                Notice(Loc.F("정수의 문 — 정수 <color=#73D9C7>+{0}</color>", n));
                break;
            }
            default:
            {
                int n = EventRewards.Gold(grade, ch);
                EventRewards.DropGold(at, n);
                Notice(_chosen == TagGold ? Loc.F("금화의 문 — 금화 <color=#F2D26A>+{0}</color>", n)
                                          : Loc.F("문을 고르지 않았다 — 금화 <color=#F2D26A>+{0}</color>", n));   // 10-30 왜 금화만 받았는지(「시간이 다 됐다 — 출구가 열렸다」는 바탕이 먼저 띄운다)
                break;
            }
        }
        return UniTask.CompletedTask;
    }
}
