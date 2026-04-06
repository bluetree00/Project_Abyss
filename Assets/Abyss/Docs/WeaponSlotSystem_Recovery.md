---
name: 무기 슬롯 시스템 + 교체 팝업 복구 가이드
description: dev/KBG-N의 commit 23b93ad1 작업 내용 — 레거시 참조용 (메인-메인 구조 전환 완료)
type: project
---

# 무기 슬롯 시스템 + 교체 팝업 재구현 가이드

> **[2026-04-06 구조 변경]** 메인/서브 슬롯 구분을 폐기하고 **메인-메인 (Slot0/Slot1) 동등 구조**로 전환 완료.
> - `WeaponSlotType` enum 제거
> - `slotType` 필드 제거 (WeaponSO, WeaponData)
> - `MainSlot`/`SubSlot` → `Slot0`/`Slot1`
> - `MainWeaponData`/`SubWeaponData` → `Weapon0Data`/`Weapon1Data`
> - `EquipmentSlotType.SubWeapon` → `Weapon1`
>
> 아래 내용은 **원본 커밋 참조용**으로만 보존합니다. 현재 코드와 다릅니다.

**원본 커밋**: `23b93ad1` (dev/KBG-N, 2026-03-19)
**제목**: feat: 메인/서브 무기 슬롯 시스템 및 교체 팝업 1:1 비교 UI 구현
**복구 참조 브랜치**: commit은 `develop`에 revert됐으나 `git show 23b93ad1 -- <파일>` 로 코드 열람 가능

**Why:** KBG-D 기반으로 재시작했을 때 이 작업을 다시 구현해야 하므로 보존
**How to apply:** KBG-D 브랜치에서 새 작업 브랜치를 만들고 아래 순서대로 재구현

---

## 1. WeaponSlotType enum 추가

**파일**: `Assets/Abyss/Shared/Characters/Weapon/WeaponScripts/WeaponType.cs`

```csharp
/// <summary>
/// 무기 슬롯 타입 — 슬롯 인덱스와 1:1 대응 (Main=0, Sub=1)
/// </summary>
public enum WeaponSlotType
{
    Main = 0,
    Sub  = 1,
}
```

---

## 2. WeaponSO에 slotType 필드 추가

**파일**: `Assets/Abyss/Shared/Characters/Weapon/WeaponScripts/WeaponSO.cs`

```csharp
public WeaponType     weaponType     = WeaponType.Sword;
public WeaponSlotType weaponSlotType = WeaponSlotType.Main;  // Main=0 / Sub=1
```

> ⚠️ KBG-D에는 `slotType` 필드명을 사용 중일 수 있음 — 기존 필드명 확인 후 통합

---

## 3. WeaponData에 slotType 필드 + 생성자 연결

**파일**: `Assets/Abyss/Shared/Characters/Weapon/WeaponScripts/WeaponData.cs`

```csharp
// 필드 추가
public WeaponSlotType weaponSlotType = WeaponSlotType.Main;

// 생성자(WeaponData(WeaponSO so))에 추가
weaponSlotType = so.weaponSlotType;
```

---

## 4. PlayerWeaponManager.HandlePickupAsync 변경

**파일**: `Assets/Abyss/Shared/Characters/Weapon/PlayerWeaponManager.cs`

### 시그니처 변경
```csharp
// 구버전 (KBG-D)
public async UniTask HandlePickupAsync(WeaponData runtimeData, bool autoEquip = true)

// 신버전
public async UniTask<bool> HandlePickupAsync(WeaponData runtimeData)
// 반환값: true = 획득함(픽업 오브젝트 제거 가능) / false = 버림(픽업 오브젝트 유지)
```

### 핵심 로직
```csharp
public async UniTask<bool> HandlePickupAsync(WeaponData runtimeData)
{
    if (runtimeData == null) return false;

    int slotIndex = (int)runtimeData.weaponSlotType;  // Main=0, Sub=1
    var slot = slots[slotIndex];

    // 빈 슬롯이면 바로 장착
    if (slot.IsEmpty)
    {
        _owned.Add(runtimeData);
        bool activate = currentSlotIndex < 0;
        await EquipToSlotAsync(slotIndex, runtimeData, activate);
        return true;
    }

    // 슬롯 차있음 → 1:1 비교 팝업
    bool replace = await ShowReplacePromptAsync(slot.runtimeData, runtimeData, slotIndex);
    if (replace)
    {
        _owned.Add(runtimeData);
        var replaced = await ReplaceSlotAsync(slotIndex, runtimeData);
        return true;
    }

    return false;  // 버리기 선택
}

private async UniTask<bool> ShowReplacePromptAsync(WeaponData currentWeapon, WeaponData newWeapon, int slotIndex)
{
    var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_WeaponReplacePopup>();
    if (popup == null)
    {
        Debug.LogWarning("[PlayerWeaponManager] UI_WeaponReplacePopup 로드 실패, 기본값 false");
        return false;
    }
    popup.Setup(currentWeapon, newWeapon, slotIndex);
    return await popup.WaitForChoiceAsync();
}
```

---

## 5. WorldWeaponDisplay 픽업 취소 처리

**파일**: `Assets/Abyss/Shared/Characters/Weapon/WorldWeaponDisplay.cs`

```csharp
private async void OnTriggerEnter(Collider other)
{
    if (_pickedUp) return;
    var player = other.GetComponent<PlayerController>();
    if (player == null) return;

    _pickedUp = true;

    bool acquired = false;
    if (player.WeaponManager != null)
        acquired = await player.WeaponManager.HandlePickupAsync(data);

    if (acquired)
    {
        Destroy(gameObject);
    }
    else
    {
        // 버리기 선택 → 2초 후 콜라이더 복원 + 파티클 재시작
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        await UniTask.Delay(2000);
        if (this != null && gameObject != null)
        {
            if (col != null) col.enabled = true;
            _pickedUp = false;
            if (_weaponInstance != null)
                foreach (var ps in _weaponInstance.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Play();
        }
    }
}
```

---

## 6. UI_WeaponReplacePopup 1:1 비교 팝업

**파일**: `Assets/Abyss/UI/Popup/UI_WeaponReplacePopup.cs`

### 레이아웃 구조
```
┌──────────────────────────────────────┐
│  새 무기 획득!                        │
│  ─── 메인 무기 ───                   │
│                                      │
│  [현재]           [새 무기]           │
│  아이콘            아이콘             │
│  이름              이름              │
│                                      │
│  공격력   50  →  75   ▲ +25 (녹색)  │
│  방어력   10  →   5   ▼  -5 (빨강)  │
│                                      │
│     [교체하기]      [버리기]          │
└──────────────────────────────────────┘
```

### 핵심 직렬화 필드
```csharp
[SerializeField] private TMP_Text slotLabelText;   // "메인 무기" / "서브 무기"
[SerializeField] private Image    currentIcon;
[SerializeField] private TMP_Text currentName;
[SerializeField] private Image    newIcon;
[SerializeField] private TMP_Text newName;
// 스탯 비교 (공격력)
[SerializeField] private TMP_Text atkCurrentText;
[SerializeField] private TMP_Text atkNewText;
[SerializeField] private TMP_Text atkDeltaText;    // ▲+25 / ▼-5 / —
// 스탯 비교 (방어력)
[SerializeField] private TMP_Text defCurrentText;
[SerializeField] private TMP_Text defNewText;
[SerializeField] private TMP_Text defDeltaText;
// 버튼
[SerializeField] private Button replaceButton;     // 교체하기
[SerializeField] private Button discardButton;     // 버리기
```

### Public API
```csharp
// UniTaskCompletionSource 방식
private UniTaskCompletionSource<bool> _tcs;

public void Setup(WeaponData current, WeaponData incoming, int slotIndex)
{
    slotLabelText.text = slotIndex == 0 ? "메인 무기" : "서브 무기";
    ApplyIcon(currentIcon, current?.icon);
    ApplyIcon(newIcon, incoming?.icon);
    currentName.text = current?.displayName  ?? "없음";
    newName.text     = incoming?.displayName ?? "없음";

    float curAtk = current?.baseAttack  ?? 0f;
    float newAtk = incoming?.baseAttack ?? 0f;
    float curDef = current?.baseDefense  ?? 0f;
    float newDef = incoming?.baseDefense ?? 0f;
    ApplyStat(atkCurrentText, atkNewText, atkDeltaText, curAtk, newAtk);
    ApplyStat(defCurrentText, defNewText, defDeltaText, curDef, newDef);

    _tcs = new UniTaskCompletionSource<bool>();
    replaceButton?.onClick.RemoveAllListeners();
    replaceButton?.onClick.AddListener(() => _tcs?.TrySetResult(true));
    discardButton?.onClick.RemoveAllListeners();
    discardButton?.onClick.AddListener(() => _tcs?.TrySetResult(false));
}

public UniTask<bool> WaitForChoiceAsync() => _tcs.Task;
```

### 스탯 델타 표시 색상
```csharp
private static readonly Color ColorUp      = new Color(0.20f, 0.90f, 0.30f); // 녹색 (증가)
private static readonly Color ColorDown    = new Color(0.95f, 0.30f, 0.30f); // 빨강 (감소)
private static readonly Color ColorNeutral = new Color(0.75f, 0.75f, 0.75f); // 회색 (동일)

// delta > 0  → "▲ +{delta:F0}" (녹색)
// delta < 0  → "▼ {delta:F0}"  (빨강)
// delta == 0 → "—"             (회색)
```

---

## 7. WeaponReplacePopupBuilder 에디터 툴

**파일**: `Assets/Abyss/Editor/WeaponReplacePopupBuilder.cs`
**메뉴**: `Tools → Build WeaponReplacePopup Prefab`

프리팹 경로: `Assets/Abyss/UI/Popup/UI_WeaponReplacePopup.prefab`
폰트 경로: `Assets/Abyss/Fonts/NotoSansKR-VariableFont_wght SDF.asset`

팝업 패널 크기: **820 × 540**

색상 상수:
```csharp
BgPanel      = new Color(0.10f, 0.11f, 0.15f, 1.00f)  // 어두운 배경
BgBlocker    = new Color(0.00f, 0.00f, 0.00f, 0.65f)  // 반투명 블로커
ColorGold    = new Color(1.00f, 0.85f, 0.40f, 1.00f)  // 제목 강조
BtnReplace   = new Color(0.18f, 0.55f, 0.28f, 1.00f)  // 교체 버튼 (초록)
BtnDiscard   = new Color(0.60f, 0.18f, 0.18f, 1.00f)  // 버리기 버튼 (빨강)
```

> 전체 소스코드: `git show 23b93ad1 -- Assets/Abyss/Editor/WeaponReplacePopupBuilder.cs`

---

## 8. 프리팹 경로 정리 (Addressables)

무기 디스플레이 프리팹을 `Assets/Abyss/Prefabs/Weapons/`로 통합:
- `SwordDisplayPrefab.prefab`
- `TestBowDisplayPrefab.prefab`
- `TestSwordDisplayPrefab.prefab`
- `TestBowPrefab.prefab`

Addressables 그룹 `WeaponDisplayPrefab`의 주소 업데이트 필요.

---

## 9. WeaponReplacePopupBuilder 상세 레이아웃 수치

### 전체 패널 구조 (RectTransform 기준)

```
Root (풀스크린 블로커, 반투명 검정 BgBlocker)
└── Panel (820 × 540, 중앙 정렬)
    ├── TitleText         "새 무기 획득!"      W:760  H:50   pos:(0, -38)    fontSize:28  Bold  White
    ├── SlotLabelText     "─── 메인 무기 ───"  W:760  H:34   pos:(0, -82)    fontSize:17  Gold
    ├── Divider           구분선               W:720  H:1    pos:(0, -118)   color: white 12% alpha
    │
    ├── CurrentPanel      현재 무기            W:240  H:220  pos:(-230, +40)
    │   ├── [레이블]      "현재"               W:220  H:28   anchorTop posY:-16  fontSize:16  SubText
    │   ├── IconImage     96×96               pos:(0, +26)   color: white 12% (비어있을때)
    │   └── NameText      무기명               W:220  H:34   anchorBottom posY:+52  fontSize:17  Bold
    │
    ├── VSText            "VS"                 W:44   H:44   pos:(0, +40)    fontSize:16  Bold  Gray
    │
    ├── NewPanel          새 무기              W:240  H:220  pos:(+230, +40)
    │   ├── [레이블]      "새 무기"            W:220  H:28   anchorTop posY:-16  fontSize:16  ColorGreen
    │   ├── IconImage     96×96               pos:(0, +26)   color: white 12%
    │   └── NameText      무기명               W:220  H:34   anchorBottom posY:+52  fontSize:17  Bold
    │
    ├── AtkStatRow        공격력 비교행        W:700  H:38   pos:(0, -128)
    │   HorizontalLayoutGroup spacing:10
    │   ├── StatLabel  "공격력"  W:80   H:38  fontSize:14  SubText
    │   ├── CurValue   "—"      W:64   H:38  fontSize:17  White
    │   ├── Arrow      "→"      W:28   H:38  fontSize:14  Gray
    │   ├── NewValue   "—"      W:64   H:38  fontSize:17  White
    │   └── DeltaText  "—"      W:108  H:38  fontSize:15  SubText
    │
    ├── DefStatRow        방어력 비교행        W:700  H:38   pos:(0, -175)
    │   (AtkStatRow와 동일 구조, statLabel="방어력")
    │
    ├── ReplaceButton     "교체하기"           W:280  H:52   pos:(-145, -232)  색상:BtnReplace(초록)
    │   └── ButtonText   fontSize:18  Bold  White
    └── DiscardButton     "버리기"             W:280  H:52   pos:(+145, -232)  색상:BtnDiscard(빨강)
        └── ButtonText   fontSize:18  Bold  White
```

### 색상 상수 전체
```csharp
BgPanel      = new Color(0.10f, 0.11f, 0.15f, 1.00f)  // 패널 배경 (어두운 네이비)
BgBlocker    = new Color(0.00f, 0.00f, 0.00f, 0.65f)  // 풀스크린 반투명 블로커
ColorGold    = new Color(1.00f, 0.85f, 0.40f, 1.00f)  // 슬롯 레이블 (금색)
ColorGreen   = new Color(0.20f, 0.85f, 0.40f, 1.00f)  // "새 무기" 레이블
ColorGray    = new Color(0.65f, 0.65f, 0.65f, 1.00f)  // VS, → 화살표
ColorSubText = new Color(0.75f, 0.75f, 0.75f, 1.00f)  // 스탯 라벨, 델타 기본값
BtnReplace   = new Color(0.18f, 0.55f, 0.28f, 1.00f)  // 교체하기 버튼 배경
BtnDiscard   = new Color(0.60f, 0.18f, 0.18f, 1.00f)  // 버리기 버튼 배경
BtnHover     = new Color(1.00f, 1.00f, 1.00f, 0.15f)  // 버튼 호버 오버레이
// 스탯 델타 색상
ColorUp      = new Color(0.20f, 0.90f, 0.30f)  // ▲ 증가 (녹색)
ColorDown    = new Color(0.95f, 0.30f, 0.30f)  // ▼ 감소 (빨강)
ColorNeutral = new Color(0.75f, 0.75f, 0.75f)  // — 동일 (회색)
```

### 스탯 행 HorizontalLayoutGroup 설정
```
spacing: 10
childAlignment: MiddleCenter
childControlWidth: false
childControlHeight: false
childForceExpandWidth: false
childForceExpandHeight: false
```

---

## 복구 순서 요약

1. `WeaponType.cs`에 `WeaponSlotType` enum 추가
2. `WeaponSO.cs` + `WeaponData.cs`에 `weaponSlotType` 필드 추가
3. `PlayerWeaponManager.HandlePickupAsync` 를 `UniTask<bool>` 반환으로 변경
4. `UI_WeaponReplacePopup.cs` 재설계 (Setup + WaitForChoiceAsync 패턴)
5. `WorldWeaponDisplay.OnTriggerEnter` 비동기 픽업 + 취소 복원 로직
6. `WeaponReplacePopupBuilder.cs` 에디터 툴 추가
7. 프리팹 경로 통합 + Addressables 업데이트

> 전체 diff 보기: `git show 23b93ad1`
