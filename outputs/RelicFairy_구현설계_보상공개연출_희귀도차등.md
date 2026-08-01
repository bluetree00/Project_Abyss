# RelicFairy — 보상 공개 연출 재설계 (기대감 빌드업 + 희귀도 차등 리액션)

> 2026-08-01 · **설계·조사 전용 문서 (코드/에셋/씬 미수정)**
> 정본 문제의식: **"보상에 피드백이 부족하다. 확률로 뜨는데 동일 이펙트라 기대감/가치가 안 느껴진다. 좋은 룬일수록 더 큰 반응이 필요하다."**
> 상위 문서: `기획/RelicFairy_통합콘텐츠설계서_시뮬에서게임으로_20260801.md` §2-2-②(희귀도 연출) · §5 우선순위 P0-8
> 선행 문서: `구현설계/RelicFairy_구현설계_룬선택팝업.md`(팝업 구조 확정본) · `기획/RelicFairy_기획_정제소_속성응축.md` §7(리빌 연출 원형)

---

## 0. 한 줄 결론

> **현재 룬 3지선다는 "등급이 텍스트 한 줄의 색"으로만 존재한다.**
> Common이든 Legendary든 **팝업 등장 연출·사운드·화면 반응이 100% 동일**하고, 등급 굴림은 방 클리어 시점에 `Debug.Log`만 남기고 **플레이어에게 한 번도 보이지 않는다.**
> 따라서 필요한 것은 신규 VFX가 아니라 **(a) 이미 알고 있는 rarity 값을 연출 파라미터로 승격**하고, **(b) 결과 공개 앞에 0.3~0.6s의 빌드업 비트를 삽입**하는 것이다. 두 가지 모두 기존 시스템 재사용으로 가능하다.

---

# A. 현재 보상 공개 경로 감사 (read-only)

## A-1. 전체 경로

```
방 클리어
 └ RoomClearGate.cs
    ├ PassDropGate()                        ← 드롭 여부 굴림 (Luck)      ※ 화면에 안 보임
    ├ RollRewardChoices(count, floor)
    │   └ PickItemByRolledRarity()
    │       └ LuckRollService.RollRarity()   ← ★등급 굴림★              ※ Debug.Log만
    └ SpawnRewardObject(center, rewards, isChoice, choiceRounds)
        └ Instantiate(endEffect2Prefab)      ← 등급과 무관한 고정 프리팹
           + ClearRewardTrigger 부착
 └ ClearRewardTrigger.cs
    ├ CreateWorldIndicator()                 ← rewards[0] 아이콘 + "Xm" 텍스트뿐
    ├ [F] 입력 → OpenRewardFlowAsync()
    └ ShowOneRuneChoiceAsync()
        ├ Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>()
        ├ popup.WaitForInteractionAsync(ct)
        └ popup.Setup(candidates, inventory)
 └ UI_RuneSelectPopup.cs
    ├ UI_Popup.PlayOpenAnimation()           ← 모든 팝업 공통 0.20s 스케일 0.85→1 + 페이드
    ├ BuildCards()                           ← 3장 동시 즉시 생성(스태거 없음)
    ├ SetSelected(-1)                        ← SoundKey.Sfx.UiButton 1회
    └ [선택] → ClosePopupUI() → UI_GridPanel.ShowWithNewItem()
```

**파일 위치**
| 역할 | 파일 |
|---|---|
| 등급/드롭 굴림 | `Assets/RelicFairy/Systems/Stage/RunGame/RoomClearGate.cs:180-269` |
| 굴림 서비스 | `Assets/RelicFairy/Shared/Item/LuckRollService.cs:41` |
| 월드 보상 오브젝트 + [F] 흐름 | `Assets/RelicFairy/Systems/Stage/RunGame/ClearRewardTrigger.cs:126-271` |
| 3지선다 팝업 | `Assets/RelicFairy/UI/Popup/UI_RuneSelectPopup.cs` |
| 팝업 공통 등장/퇴장 | `Assets/RelicFairy/UI/Popup/UI_Popup.cs:46-118` |

## A-2. "등장 연출이 등급과 무관하게 동일한가" — **완전히 동일하다**

| 연출 요소 | 현재 상태 | 등급 반영 |
|---|---|---|
| 팝업 등장 | `UI_Popup.PlayOpenAnimation()` — 0.20s, 스케일 0.85→1(EaseOutBack), 알파 0→1. **전 팝업 공용** | ✕ |
| 카드 등장 | `BuildCards()`가 3장을 **같은 프레임에 전부 생성**. 애니메이션 없음 | ✕ |
| 카드 프레임/채움 | `ShopUIStyle.CardBorder` / `CardFill` 고정색 (스킨 시 `cardFrame`/`cardFill` 단일 아트) | ✕ |
| 상단 리본 색 | `ElementDef.IdColor(data.element, ShopUIStyle.RarityGlow(data.rarity))` — **속성색 우선, 등급색은 속성이 없을 때만 쓰이는 폴백** | △ (실질 ✕) |
| 등급 표기 | `meta` 텍스트 1줄의 **색**(`RarityGlow`) + 라벨 접두 `· Common / ◇ Rare / ◆ Epic / ◆ Legendary` | ○ (텍스트뿐) |
| 사운드 | 카드 클릭 / [선택] / [넘기기] 전부 `SoundKey.Sfx.UiButton`("Button") 동일 1종. **팝업 오픈 사운드 없음** | ✕ |
| 파티클·화면효과·카메라·슬로우 | **호출 지점 자체가 없음** | ✕ |

> `RarityLabel()`에서 Epic과 Legendary가 **같은 기호 `◆`** 를 쓴다(`UI_RuneSelectPopup.cs:558-564`). 최상위 등급이 형태로도 구별되지 않는다.

## A-3. 희귀도 데이터 — **팝업은 이미 다 알고 있다**

| 항목 | 위치 |
|---|---|
| enum | `ItemSO.cs:47` — `Common / Rare / Epic / Legendary` (4단, 순서 = 강도 오름차순) |
| 색 테이블 | `Shared/Item/RarityColorTable.cs` — 白 / 청(0.4,0.7,1) / 자(0.8,0.4,1) / 금(1,0.84,0.2) |
| UI 글로우색 | `UI/Popup/ShopUIStyle.cs:55-66` — `RarityGlow(r)` = 위 색 + 등급별 알파(0.14/0.26/0.34/0.42) |
| 영문 라벨 | `ShopUIStyle.cs:68-74` — `RarityLabel(r)` |
| 카드가 보유한 값 | `RuntimeItemData.rarity` — `BuildCardContent()`에서 이미 참조 중 |

→ **데이터 배관은 100% 완비.** 새 필드·새 로드 경로 없이 연출 분기만 얹으면 된다.

## A-4. 재사용 가능한 기존 연출 자산과 그 API

| 시스템 | 파일 | API | 팝업(정지) 중 동작 여부 |
|---|---|---|---|
| `HitFeelService` | `Systems/Combat/HitFeelService.cs` | `HitStop(scale,dur)` `CameraShake(amp,dur)` `CameraShakeDirectional(dir,amp,dur)` `Light()/Heavy()/Crit()` `KillImpact()` | **✕ 완전 무효** (아래 A-5) |
| `CameraShakeExtension` | `Systems/Camera/CameraShakeExtension.cs` | `AddTrauma` / `AddDirectionalPunch` (vcam Finalize 합성) | **✕** `Time.timeScale <= 0f` early-return(:57) |
| `TimeScaleArbiter` | `Systems/Time/TimeScaleArbiter.cs` | `Acquire(owner,scale,Priority)` / `Release` — `HitStop(10) < SlowMotion(100) < Pause(1000)` | 팝업 **밖**에서만 유효 |
| `VolumePulseService` | `Systems/Combat/HitFeedback/VolumePulseService.cs` | `Pulse(peak, duration)` — 크로매틱+모션블러+Bloom 전체화면 펄스 | **○ 유일하게 동작** (`unscaledDeltaTime` 기반, :189) |
| UI 저스 4종 | `UI/Popup/UI_CruciblePanel.cs:970-1030` | `CountLevel` / `PunchCard(amp,dur)` / `FlashCard(color)` / `ShakeCard(amp,dur)` — 전부 `unscaledDeltaTime` + `destroyCancellationToken` | **○** (단 `private`, 재련소 전용) |
| 등급 확률 막대 | `UI/Popup/Widgets/OddsBarView.cs` | `Create(parent, anchor…)` — Rare/Epic/Legend 3단 비율 막대 | **○ 그러나 호출처 0개 (미배선 유휴 자산)** |
| 등급별 VFX 키 | `Shared/Item/ItemVfxConfig.cs` | `GetDisplayVfxKey(rarity)` / `GetTrailVfxKey(rarity)` | 참조처는 `WorldItemDisplay` 1곳. **Legendary 항목 없음 → Common으로 폴백** |
| HUD 배너 | `Systems/Item/Core/ItemEffectVfxHelper.cs:43` | `ShowNotice(msg)` → `HudPresenter.ShowItemEffectNotice` | ○ |
| VFX 스폰 | `ItemEffectVfxHelper.SpawnOneShotAt(key,pos,scale)` / `AttachLoopVfx` | 월드 전용(풀러 경유) | 월드 단계에서만 |
| 사운드 | `Managers.Sound.PlayEffectAsync(key, volume=1, pitch=1)` | **볼륨·피치 인자 존재** → 티어별 피치 램프 무료 | ○ |

### ⚠ 감사에서 나온 결함 3건 (설계 전제)

1. **`ShopUIStyle.PlaySfx`는 죽은 채널이다.**
   `ShopUIStyle.Sfx`(`Action<string>`)에 **대입하는 코드가 프로젝트 전체에 없다**(`ShopUIStyle.cs:21`). 따라서 `UI_CruciblePanel`의 `"crucible_success"` / `"crucible_jackpot"` / `"crucible_fail"`는 **한 번도 소리가 난 적이 없다.**
   → 신규 보상 SFX는 **반드시 `Managers.Sound.PlayEffectAsync` 직접 호출**로 배선한다. (별건: 재련소 SFX 복구는 이 문서 범위 밖 — 별도 티켓 권장)

2. **`UI_CruciblePanel:884`의 주석이 사실과 다르다.**
   *"시간정지 팝업에선 timeScale 무효(무해) — 카메라측 반응만"* → 실제로는 `CameraShakeExtension`이 `timeScale <= 0`에서 early-return하므로 **카메라 반응도 0**이다. `HitFeelService.HitStop` 역시 `HasRequestAtOrAbove(SlowMotion)` 가드(`HitFeelService.cs:63`)에 걸려 **호출 즉시 return**한다. 재련소의 `HitFeelService` 호출 3건은 전부 무동작.
   → **본 설계의 핵심 제약**: 카메라·슬로우 연출은 **팝업이 열리기 전(월드 단계)에만** 가능.

3. **`OddsBarView`가 구현돼 있으나 아무도 부르지 않는다.** → §C-3에서 그대로 활용.

## A-5. 결정적 제약 — 팝업 중에는 timeScale = 0

`UI_RuneSelectPopup.BlocksGameplay => true` (`:24`) → `UIManager.cs:333`이 `TimeScaleArbiter.Acquire(this, 0f, Priority.Pause)`.

```
Pause(1000) > SlowMotion(100) > HitStop(10)
   ↓
팝업이 열려 있는 동안 Time.timeScale == 0 이 확정
   ├ HitFeelService.HitStop  → HasRequestAtOrAbove(SlowMotion) == true → 즉시 return       (무효)
   ├ CameraShake             → CameraShakeExtension이 timeScale<=0 에서 return              (무효)
   ├ TimeScaleArbiter 슬로우 → Pause가 상위 우선순위라 반영 안 됨                            (무효)
   └ VolumePulseService.Pulse → unscaledDeltaTime 구동                                       (유효)
```

**연출 예산은 두 구간으로 쪼개진다.**

| 구간 | timeScale | 쓸 수 있는 것 |
|---|---|---|
| **월드 단계** — [F] 입력 ~ 팝업 오픈 직전 (`ClearRewardTrigger.OpenRewardFlowAsync` 내부) | 1 (또는 SlowMotion 요청 가능) | 카메라 셰이크·펀치, 슬로우모, 히트스톱, 월드 VFX, 사운드 |
| **팝업 단계** — 오픈 이후 | 0 | UI 로컬 애니메이션(unscaled), `VolumePulseService.Pulse`, 사운드 |

## A-6. "확률 굴림이 플레이어에게 보이는가" — **전혀 안 보인다**

- `PassDropGate()`(드롭 여부)와 `RollRarity()`(등급)는 **방을 깬 순간 `RoomClearGate` 안에서 조용히 끝난다.** 산출물은 `Debug.Log` 2줄뿐(`:225`, `:267`).
- 월드 보상 오브젝트는 `endEffect2Prefab` **고정 1종**. 인디케이터는 `rewards[0].so.icon` + `"Xm"` 텍스트만 표시 → **등급 정보 0**.
- 등급이 화면에 처음 나타나는 시점 = **팝업이 이미 완전히 뜬 뒤, 카드 메타 텍스트 한 줄**.
- 즉 **"굴림 → 결과"의 시간 간격이 0**이고, 그 사이에 플레이어가 관여하거나 반응할 프레임이 없다. 사용자가 말한 *"확률로 뜨는데 동일 이펙트"* 의 구조적 원인이 정확히 이 지점이다.

## A-7. 건드리면 안 되는 지점 (기존 버그 수정 이력)

| 지점 | 이유 |
|---|---|
| `OnConfirmClicked` / `OnSkipClicked`의 **`ClosePopupUI()` → `TrySetResult()` 순서** (`UI_RuneSelectPopup.cs:502-518`) | 순서가 뒤집히면 다중 라운드에서 좀비 팝업 → `timeScale=0` 영구 고착. 연출을 넣더라도 **버튼 클릭 즉시 resolve** 원칙 유지 |
| `ShowOneRuneChoiceAsync`의 `UniTask.WaitUntil(GridPanel 닫힘)` (`ClearRewardTrigger.cs:268`) | 다중 라운드 Pause 중첩 방지 |
| `WaitForInteractionAsync`를 `Setup` **전에** 호출 (`ClearRewardTrigger.cs:249-251`) | 후보 0개일 때 `Setup`이 즉시 resolve하는 경로 보호 |
| 빌드업 시퀀스는 **입력 수락을 지연시키면 안 된다** | 조작감 저하. §C-1 스킵 규칙 참조 |

---

# B. 리서치 — 레퍼런스와 근거

## B-1. 기대감(anticipation) 빌드업

| 출처 | 요지 | 본 설계 반영 |
|---|---|---|
| Disney 12원칙 중 **Anticipation** ([GameJuice](https://gamejuice.co.uk/articles/disney-12-animation-principles-games), [Chris Totten](https://totter87.medium.com/12-principles-for-game-animation-a9137ef44345)) | *"큰 동작 앞에 작은 예비동작을 둬서 곧 큰 일이 일어남을 알린다"* | §C-1 Beat 1 차징(0.35s) |
| **Juice It or Lose It** / Vlambeer *Art of Screenshake* ([GameJuice](https://gamejuice.co.uk/resources/juice-it-or-lose-it), [Valdemird](https://valdemird.com/blog/game-feel-on-the-web/)) | 스크린셰이크는 "세계가 당신에게 반응한다"는 신호. 저스는 **핵심 플레이의 메아리**여야 한다 | 티어 상단에만 카메라 반응 배정(§C-2) |
| **Overwatch 로챗박스** — 내용 공개 전 상자가 **떨린다** ([Intenta Digital](https://intenta.digital/game-design/psychology-of-liot-boxes/), [PMC 연구](https://pmc.ncbi.nlm.nih.gov/articles/PMC7882574/)) | 각성(arousal)을 올리는 사전 신호 | 월드 보상 오브젝트 **떨림 + 광량 상승**(§C-1 Beat 1) |
| **원신 소원(Wish)** ([분석글](https://img.krmangalam.edu.in/star-base/genshin-impact-5-star-wish-animation-secrets-1764806225)) | 별 등급에 따라 **연출이 점층**; 5성은 금빛 섬광이 화면을 덮음. 특유의 **'핑' 사운드가 승급의 첫 신호**. **애니메이션 스킵 옵션이 표준** | §C-2 티어 점층 + §C-1 스킵 규칙 |
| **PMC 실험 연구** ([Rare Loot Box Rewards…](https://pmc.ncbi.nlm.nih.gov/articles/PMC7882574/)) | 공개 **직전 예기 각성(anticipatory arousal)이 상승**하며, 희귀 보상일수록 각성·보상 반응·재개봉 충동이 커진다 | "기대감은 결과가 아니라 **결과 직전 구간**에 산다" — 빌드업 비트의 근거 |

## B-2. 희귀도 차등 피드백 (rarity-scaled juice)

| 게임/출처 | 티어링 방식 |
|---|---|
| **Diablo III / ARPG 계열** ([Anthem Dev Tracker 논의](https://devtrackers.gg/anthem/p/a34635a0-no-spoilers-audio-visual-cues-for-loot-drops-need-to-be-enhanced)) | **등급별 드롭 사운드 티어 + 색 빔(loot beam)**. 화면 밖에서도 "뭐가 떨어졌는지" 판정 가능 |
| **Borderlands 2/4** ([ARPG Style Loot Beams 모드](https://www.nexusmods.com/borderlands2/mods/179), [BLCM 소스](https://github.com/BLCM/BLCMods/blob/master/Borderlands%202%20mods/OurLordAndSaviorGabeNewell/ARPGStyleLootBeams.blcm)) | 전설(≈3% 드롭)에 **전용 사운드 + 원거리 가시 빔 + 파티클**. 커뮤니티 모드가 이를 확장할 만큼 **오디오 큐가 체감의 주축** |
| **Hades — Boon 4등급** (Common/Rare/Epic/Heroic, [Hades Wiki](https://hades.fandom.com/wiki/Boons), [Hades II](https://hades.fandom.com/wiki/Boons/Hades_II)) | 등급이 **수치 배율**(Common 40~60% → Heroic 160~240%)과 **카드 프레임 색**으로 이중 표기. 다만 **플레이어 피드백상 "등급의 의미가 잘 안 읽힌다"는 불만이 반복**([Steam 토론](https://steamcommunity.com/app/1145360/discussions/2/3124866920645157274/), [rarity vs lvl](https://steamcommunity.com/app/1145360/discussions/0/2796126653263362641/)) → **색만으로는 부족하다**는 반례로 채택 |
| **원신** | 3성/4성/5성이 **연출 길이·광량·사운드 레이어 수**로 계단식 분리 |

> **교훈 요약:** 등급 차등은 (1) **오디오가 가장 싸고 가장 강하다**, (2) **색만으로는 안 읽힌다 — 시간(연출 길이)과 사운드가 붙어야 위계가 생긴다**, (3) 최상위 티어는 **형태 자체를 다르게**(전용 SFX·전체화면 반응) 해야 "특별함"이 성립한다.

## B-3. 확률성·니어미스 — 채택 범위와 선

| 근거 | 내용 |
|---|---|
| [PlayerCounter](https://playercounter.com/how-loot-boxes-borrowed-probability-design-from-slot-machines) · [GeekVibes](https://geekvibesnation.com/loot-boxes-gacha/) · [NCBI 니어미스 연구](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12657543/) | 니어미스는 **실제 당첨과 유사한 보상 회로를 활성화**해 동기를 올린다. 슬롯머신에서 이식된 기법 |
| [Intenta Digital](https://intenta.digital/game-design/psychology-of-loot-boxes/) · [PSU](https://www.psu.com/news/the-slot-machine-psyche-how-variable-ratio-reinforcement-drives-modern-gaming-engagement/) | 가변비율 강화 + 릴 정지 연출이 각성을 만든다 |

**본 프로젝트에서의 선 긋기 (설계 결정):**
RelicFairy는 **과금 가챠가 아니라 런 내 보상**이다. 니어미스를 *"더 뽑게 만드는 낚시"* 로 쓰면 안 된다.
→ 채택하는 것은 **"굴림을 보여준다"(reveal the roll)** 뿐이다. 이미 확정된 결과를 **0.25s 동안 계단식으로 드러내는 표시 연출**이며, 확률·결과·기대값에 **어떤 영향도 주지 않는다.** 니어미스 스텝은 **Legendary 직전 1회, 0.08s, 스킵 가능**으로 제한한다. (§C-3)

---

# C. 구현 설계

## C-1. 기대감 시퀀스 — 비트 타임라인

**전제**: 결과(등급·후보)는 `RoomClearGate`에서 **이미 확정**돼 있다. 연출은 **표시층 전용**이며 데이터·세이브·확률에 손대지 않는다(재련소 `PlayEnhanceSequence`와 동일 원칙).
**티어 결정값**: `tier = _rewards.Max(r => r.data.rarity)` — 후보 중 **최고 등급**이 시퀀스 강도를 정한다.

```
t=0.00  [F] 입력                     ── 월드 단계 (timeScale 조작 가능) ─────────────
        · UiButton SFX
        · ShowPrompt(false) / HideWorldIndicator()

t=0.00~0.35  ① 차징 (tier에 따라 0.00 / 0.25 / 0.35 / 0.55s)
        · 보상 오브젝트: 스케일 미세 떨림 + 광량 상승 (Overwatch 상자 떨림)
        · 상승 SFX: 같은 클립을 pitch 0.85→1.25 로 램프 (PlayEffectAsync(key, vol, pitch))
        · Epic↑ : TimeScaleArbiter.Acquire(this, 0.45f, Priority.SlowMotion)
        · Legendary : + HitFeelService.CameraShake(0.06f, 0.20f)  ← 여기서만 카메라가 산다

t=0.35  ② 임팩트 & 팝업 오픈         ── 이 프레임부터 timeScale = 0 ────────────────
        · TimeScaleArbiter.Release(this)   ※ 반드시 팝업 오픈 전에 해제
        · Legendary : VolumePulseService.Pulse(0.55f, 0.28f) + 팡파르 SFX
        · ShowPopupUIAndGetAsync<UI_RuneSelectPopup>()  (UI_Popup 기본 0.20s 등장)

t=0.35~0.75  ③ 카드 순차 공개 (unscaled, 스태거 0.10s)
        · 카드는 알파 0 · 스케일 0.9 · y −18 에서 시작 → PunchCard 계열로 팝인
        · 공개 순서 = 등급 오름차순  ← ★상승감의 핵심. 제일 좋은 카드가 마지막에 열린다
        · 카드마다: 등급 SFX 1발 + FlashCard(등급색) + 프레임 글로우 페이드인
        · 카드 등급 라벨은 이 순간 "굴림 리빌"로 열린다 (§C-3-b)

t=0.75  ④ 최상위 티어 방점 (Epic 이상만)
        · Epic      : VolumePulseService.Pulse(0.30f, 0.18f)
        · Legendary : Pulse(0.55f, 0.30f) + 전체 화면 금색 플래시(UI Image 오버레이) + 전용 팡파르

t=0.75  ⑤ 조작 가능 (실제로는 t=0.35부터 이미 입력 수락)
```

### 스킵 규칙 (조작감 보호 — 필수)

| 규칙 | 내용 |
|---|---|
| **입력은 절대 막지 않는다** | 팝업이 뜬 순간(t=0.35)부터 카드 클릭·[선택]·[넘기기] 전부 유효. 시퀀스가 끝나야 고를 수 있는 게 아니다 |
| **아무 입력 = 스냅** | 클릭/Space/F 어느 것이든 들어오면 진행 중 트윈을 **최종 상태로 즉시 스냅**(파괴가 아니라 완료) |
| **연속 라운드 감쇠** | 챌린지 다중 라운드(`choiceRounds > 1`)에서는 2라운드부터 차징 시간 ×0.4. 같은 연출 3연타가 지루함이 되는 지점 |
| **설정 옵션** | `보상 연출: 전체 / 축약(차징 생략, 스태거 0.04s) / 끔` 3단. 원신·가챠 UX의 스킵 표준을 따른다 |
| **월드 단계 슬로우는 취소 안전** | `Release`를 `finally`에 둬서 취소·파괴 시 timeScale 누수 없게 (기존 `GameRunBootstrapper.cs:1046-1048` 패턴 그대로) |

## C-2. 희귀도 4티어 연출 스펙표

> 원칙 (통합설계서 §2-2 원칙 3 "연출 강도는 등급에 비례"):
> **Common = 거의 아무 일도 안 일어난다**가 정답이다. 하위를 미니멀하게 유지해야 상위가 사건이 된다.

### C-2-1. 월드 단계 (팝업 오픈 전 — 카메라/시간 사용 가능)

| 파라미터 | **Common** | **Rare** | **Epic** | **Legendary** |
|---|---|---|---|---|
| 차징 길이 | **0.00s (생략)** | 0.25s | 0.35s | **0.55s** |
| 보상 오브젝트 떨림 | 없음 | 미세(0.02) | 중(0.05) | **강(0.09) + 상승 부양** |
| 광량 상승 | 없음 | 청색 소 | 자색 중 | **금색 대 + 빛기둥** |
| 상승 SFX | 없음 | `sfx_reward_charge` pitch 0.90→1.10 | pitch 0.85→1.20 | pitch 0.85→1.30 **+ 저역 레이어 1장 추가** |
| 슬로우모 (`Priority.SlowMotion`) | ✕ | ✕ | **0.45** | **0.30** |
| 카메라 셰이크 (`HitFeelService`) | ✕ | ✕ | ✕ | **`CameraShake(0.06f, 0.20f)`** |

### C-2-2. 팝업 단계 (timeScale = 0 — UI 로컬 + VolumePulse만)

| 파라미터 | **Common** | **Rare** | **Epic** | **Legendary** |
|---|---|---|---|---|
| 카드 등장 | 페이드인 0.12s | 팝인 0.16s (PunchCard 0.06) | 팝인 0.20s (0.10) + 스태거 강조 | **팝인 0.24s (0.14) + 카드 회전 4°→0** |
| 카드 스태거 | 0.06s | 0.08s | 0.10s | **0.14s** (최상위가 마지막) |
| 프레임 플래시 (`FlashCard`) | ✕ | 청 1회 | 자 1회 + 잔광 0.3s | **금 2회 펄스 + 상시 글로우** |
| 프레임 색/두께 | 회 · 2px | **청 · 2px** | **자 · 3px** | **금 · 3px + 외곽 글로우** |
| 파티클 (Canvas 파티클) | ✕ | ✕ | 카드 하단 소량 상승 | **카드 전체 금가루 + 섬광 1발** |
| 전체 화면 플래시 | ✕ | ✕ | ✕ | **금색 알파 0.35 → 0, 0.25s** |
| `VolumePulseService.Pulse` | ✕ | ✕ | **`Pulse(0.30f, 0.18f)`** | **`Pulse(0.55f, 0.30f)`** |
| 전용 SFX | `sfx_rune_reveal_common` (짧은 탁음) | `..._rare` (청명한 벨) | `..._epic` (벨 + 하모닉) | **`..._legendary` (팡파르 + 서브베이스)** |
| 등급 라벨 | `· Common` 회 | `◇ Rare` 청 | `◆ Epic` 자 | **`★ Legendary` 금 (기호 변경 — 현재 Epic과 중복인 `◆` 수정)** |
| 굴림 리빌 (§C-3-b) | 즉시 확정 | 1단 상승 | 2단 상승 | **3단 상승 + 니어미스 스텝** |

> **읽는 법**: 한 행을 가로로 훑으면 "커먼 → 전설"이 **없음 → 색 → 색+시간 → 색+시간+화면+소리** 로 계단이 진다. 어느 한 채널(색)만 키우지 않고 **채널 개수 자체가 늘어나는 것**이 "가치를 가르치는" 장치다.

## C-3. 확률성 가시화 — "확률인데 동일" 해소

### (a) 월드 레이어에서 미리 알린다 — *방을 깨는 순간*

현재 `endEffect2Prefab`은 **고정 1종**이고 인디케이터는 아이콘+거리뿐이다.
→ **보상 오브젝트의 빛 색·광량을 `_rewards.Max(rarity)`에 맞춘다.**
ARPG의 loot beam과 같은 역할. 방을 깨고 돌아보는 순간 *"이번 건 금색이다"* 가 성립하고, **주우러 가는 3초가 통째로 기대감 구간이 된다.**

- 구현: `ClearRewardTrigger.Initialize()`에서 `maxRarity` 산출 → `CreateWorldIndicator()`의 `iconBgImg.color`/외곽 링 색 + `ItemEffectVfxHelper.AttachLoopVfx(ItemVfxConfig.GetDisplayVfxKey(maxRarity), …)`
- 선행 수정: `ItemVfxConfig`에 **Legendary 슬롯 추가**(현재 없음 → Common 폴백). 최소 4번째 키 필드 1개.

### (b) 굴림 리빌 — 카드 등급 라벨을 "돌린다"

카드가 열릴 때 등급 표기를 **즉시 확정하지 않고 0.25s 동안 계단식으로 올린다.**

```
Common  : (즉시)  · Common
Rare    :  Common → ◇ Rare                              0.10s
Epic    :  Common → ◇ Rare → ◆ Epic                     0.18s
Legendary: Common → ◇ Rare → ◆ Epic → ★ Legendary       0.25s
           └ 니어미스 스텝: ★에 도달하기 0.08s 전 ◆에서 한 번 "멈칫" (틱 SFX + 미세 정지)
```

- **각 단 상승마다 SFX 피치 +2반음** — 원신의 *"승급의 첫 신호는 사운드"* 를 그대로 차용.
- 프레임 색도 라벨과 **동기**해서 올라간다 → 색이 계단을 오르는 것이 눈으로 보인다.
- **니어미스는 Legendary 경로에서만, 1회, 0.08s.** 확률·결과 불변. `보상 연출: 축약/끔`에서 즉시 생략.
- 재련소 `CountLevel()`(`UI_CruciblePanel.cs:970`)과 **완전히 같은 패턴** — 이미 검증된 카운트업 루프의 등급 버전.

### (c) 확률 자체를 상시 노출 — `OddsBarView` 재사용

팝업 하단(현재 [선택]/[넘기기] 좌측 여백)에 **현재 Luck 기준 Rare/Epic/Legend 확률 막대 3단**을 얹는다.

- `OddsBarView.Create(...)`는 **이미 구현돼 있고 호출처가 0개**다. 신규 위젯 제작 비용 0.
- 데이터: `LuckRollService.GetLuckLevel(luck, table)` → `LuckRollTableSO.GetWeights(level)` 정규화.
- 효과 3가지:
  1. *"확률로 뜬다"* 는 사실이 **처음으로 화면에 존재**하게 된다 → 굴림 리빌(b)이 "연출"이 아니라 "정보"로 읽힌다.
  2. **Luck 스탯에 처음으로 체감 피드백이 생긴다** (현재 Luck은 로그에만 존재).
  3. 다음 방 보상에 대한 기대를 만든다 = 통합설계서 §2-2 *"런 내 도파민"* 의 지속 축.

## C-4. 재사용 / 신규 매핑

### 재사용 (신규 코드 최소)

| 연출 요소 | 얹을 기존 시스템 | 비고 |
|---|---|---|
| 차징 슬로우모 | `TimeScaleArbiter.Acquire(owner, 0.45f, Priority.SlowMotion)` | 팝업 **오픈 전**에만. `finally`에서 Release |
| 차징 카메라 | `HitFeelService.CameraShake(amp, dur)` | 월드 단계 한정 |
| 전체화면 펄스 | `VolumePulseService.Pulse(peak, dur)` | **팝업 안에서 쓸 수 있는 유일한 화면효과** |
| 카드 팝인/플래시/셰이크 | `UI_CruciblePanel`의 `PunchCard`/`FlashCard`/`ShakeCard` **패턴** | `private` → **`UIJuice` 정적 헬퍼로 추출** 권장(재련소와 공유, 3번째 복붙 방지) |
| 굴림 카운트 리빌 | `CountLevel()` 패턴 | 레벨 대신 `ItemRarity` 계단 |
| 등급 색 | `RarityColorTable.Get` / `ShopUIStyle.RarityGlow` / `RarityLabel` | **이미 전부 존재.** 통합설계서 P0-8 "희귀도 색 통일"의 첫 적용처 |
| 월드 등급 VFX | `ItemVfxConfig` + `ItemEffectVfxHelper.AttachLoopVfx` | Legendary 슬롯만 추가 |
| 확률 막대 | `OddsBarView.Create` | 미배선 유휴 자산 — 그대로 사용 |
| 사운드 티어링 | `Managers.Sound.PlayEffectAsync(key, volume, pitch)` | **피치 인자로 무료 티어링.** `ShopUIStyle.PlaySfx`는 죽은 채널이라 사용 금지 |
| HUD 알림 | `ItemEffectVfxHelper.ShowNotice` | Legendary 획득 시 배너 |

### 신규로 필요한 **최소** 자산

| # | 자산 | 형태 | 비용 | 대체 가능성 |
|---|---|---|---|---|
| 1 | 상승 SFX `sfx_reward_charge` 1종 | 오디오 1개 | 소 | 기존 클립 피치 램프로 임시 대체 가능 |
| 2 | 등급 리빌 SFX 4종 | 오디오 4개 | 소 | **1종 + 피치 4단**으로 P0에서 대체 가능 |
| 3 | Legendary 팡파르 1종 | 오디오 1개 | 소 | 필수 (전설의 "형태 차이") |
| 4 | 전체화면 플래시 | **UI Image 1장 + 알파 트윈** | **0 (코드)** | — |
| 5 | Legendary 월드 VFX 키 | 기존 VFX 재색상 | 소 | `VFX_Item_Epic` 금색 틴트로 대체 |
| 6 | 카드 금가루 파티클 | Canvas 파티클 | 중 | **P2로 미룸.** 없어도 표가 성립 |

> **신규 VFX 제작 없이 P0~P1이 전부 성립한다.** 새 아트가 필요한 건 표에서 #6 하나뿐이고 그건 P2다.

### 우선순위 · 난이도 · 리스크

| 순위 | 항목 | 난이도 | 리스크 | 효과 | 근거 |
|---|---|---|---|---|---|
| **P0-1** | **티어별 카드 프레임 색·두께 + 등급 라벨 기호 수정(`◆`중복→`★`)** | ★☆☆☆☆ | 없음 | **최상** — 등급이 "보이는" 최소 조건 | 데이터 완비, 색 테이블 존재 |
| **P0-2** | **등급 SFX 티어링** (1클립 + 피치 4단) | ★☆☆☆☆ | 없음 | **최상** — 리서치 공통 결론: 오디오가 가장 싸고 강함 | `PlayEffectAsync`에 pitch 인자 존재 |
| **P0-3** | **카드 순차 공개(등급 오름차순 스태거)** | ★★☆☆☆ | 하 | **상** — 상승감의 뼈대. 신규 자산 0 | `PunchCard` 패턴 이식 |
| **P0-4** | **굴림 리빌 (등급 계단 상승, 니어미스 제외)** | ★★☆☆☆ | 하 | **상** — "확률인데 동일" 직접 해소 | `CountLevel` 패턴 |
| **P0-5** | **스킵/속도 옵션 3단** | ★★☆☆☆ | **중** — 옵션 저장 배선 필요 | **필수** — 조작감 보호 장치. P0 항목과 **동시 출하** | 가챠 UX 표준 |
| **P1-1** | **월드 단계 차징**(떨림·광량·상승 SFX) | ★★☆☆☆ | 하 | 상 — 기대감 구간이 처음 생김 | `ClearRewardTrigger` 내부 |
| **P1-2** | **슬로우모(Epic↑) + 카메라(Legendary)** | ★★★☆☆ | **중 — timeScale 누수** | 상 | `finally` Release 필수. §A-7 |
| **P1-3** | **`VolumePulse` 방점 (Epic/Legendary)** | ★☆☆☆☆ | 하 | 중상 | 이미 재련소에서 검증 |
| **P1-4** | **월드 보상 오브젝트 등급색 빔** | ★★☆☆☆ | 하 | **상** — 주우러 가는 3초가 기대 구간이 됨 | `ItemVfxConfig` Legendary 슬롯 추가 |
| **P1-5** | **`OddsBarView` 배선 (확률 상시 노출)** | ★★☆☆☆ | 하 | 중상 — Luck 스탯 최초 체감 | 위젯 이미 존재 |
| **P2-1** | 니어미스 스텝 (Legendary 한정) | ★★☆☆☆ | **중 — 설계 의도 오독 위험** | 중 | §B-3 선 긋기 준수 |
| **P2-2** | 카드 파티클 · 전용 Legendary VFX | ★★★☆☆ | 하 | 중 | 유일한 신규 아트 |
| **P2-3** | `UIJuice` 헬퍼 추출 (재련소와 공유) | ★★☆☆☆ | 하 | (유지보수) | 3번째 복붙 발생 전에 |

**리스크 요약**
1. **timeScale 누수** — 월드 차징의 `SlowMotion` 요청을 팝업 오픈 전에 반드시 `Release`. 미해제 시 팝업 종료 후에도 슬로우가 남는다. `TimeScaleArbiter.PurgeDeadOwners`가 파괴된 소유자는 걷어내지만 **살아있는 트리거는 안 걷힌다.**
2. **다중 라운드 좀비 팝업** — §A-7의 `Close → resolve` 순서 불변. 연출은 **resolve 이후에 걸지 않는다.**
3. **팝업이 프리팹이 아니라 코드로 그려진다** — 파티클/글로우는 Canvas 하위 `Image`·`ParticleSystem`을 코드 생성해야 한다. P2로 미룬 이유.
4. **연출 길이 vs 반복 피로** — 한 런에 3지선다가 10~20회 발생한다. Common(대다수)에서 **0.12s 페이드**만 남기는 것이 설계의 핵심 방어선이다.

## C-5. 콘텐츠 설계서 "런 내 도파민"과의 연결

`RelicFairy_통합콘텐츠설계서_시뮬에서게임으로_20260801.md`와의 대응:

| 통합설계서 항목 | 본 문서 |
|---|---|
| **§2-2-② 희귀도 연출** (Common 회/없음 · Rare 청/프레임플래시+착지음 · Epic 자/발광+약펄스 · Legendary 금/강발광+`VolumePulse`+팡파르+시간감속) | **§C-2 스펙표가 이 표의 구현 명세판**이다. 시간감속 항목만 **팝업 안에서는 불가능**(§A-5)하여 **팝업 직전 월드 단계로 이동**시킨 것이 유일한 설계 변경 |
| **§2-2 원칙 3** *"연출 강도는 등급에 비례"* | §C-2 채널 계단 (없음→색→색+시간→색+시간+화면+소리) |
| **§2-2-④ 잭팟** | 굴림 리빌(§C-3-b)이 잭팟 순간을 **볼 수 있게** 만든다. 룬 굴림 잭팟(9칸 3×3, 5%)도 같은 리빌 채널에 얹을 수 있다 |
| **§5 P0-8 "희귀도 색 통일 (프레임=희귀도 / 아이콘=속성)"** | **본 문서 P0-1이 그 첫 적용처.** 현재 룬 카드는 리본이 속성색을 쓰고 프레임은 무색이라 P0-8 규칙과 어긋나 있다 → **프레임=희귀도 / 리본·엠블럼=속성**으로 정리하면 두 축이 충돌 없이 공존 |
| **§5 P0-7 "재화 pill + 획득 연출"** | 넘기기 원석 +1(`ClearRewardTrigger.cs:256`)이 현재 `Debug.Log`뿐 → 같은 연출 언어(pill로 빨려듦)를 공유 |
| **§2-2-③ 빌드 폭발 순간** | 본 문서의 `UIJuice` 헬퍼(P2-3)가 시너지 단계 상승 연출(P1-7)의 공용 기반이 된다 |
| **§2-2 원칙 1** *"같은 자극을 두 번 주지 않는다"* | 룬 보상 = **"홀림"**(정제소 계열). 재련소의 "겁" 계열 연출(실패 셰이크·붉은 플래시)을 **여기서 쓰지 않는다** — 보상 공개에는 상승/개화 계열만 배정 |

---

## D. 검증 기준 (구현 시)

```
1. Common 3장 3지선다 → 총 연출 길이 ≤ 0.35s, 화면효과 0     → verify: 반복 20회에 피로 없음
2. Legendary 포함 3지선다 → 차징~방점 ≤ 1.1s, 마지막에 공개  → verify: 등급 모르고 봐도 "뭔가 좋다"가 읽힘
3. 팝업 오픈 후 timeScale == 0 상태에서 카드 애니메이션 정상 → verify: unscaledDeltaTime 사용 확인
4. 차징 슬로우 후 팝업 종료 → Time.timeScale == 1            → verify: TimeScaleArbiter.DescribeHolders() == "(none)"
5. 다중 라운드(choiceRounds=3) 완주 → 고착 없음               → verify: 이벤트방 Platinum 보상 재현
6. 연출 중 즉시 클릭 → 정상 선택 + 트윈 스냅                  → verify: 입력 지연 0
7. 보상 연출 "끔" 설정 → 기존 동작과 100% 동일                → verify: 회귀 없음
```

---

## 부록 — 출처

- [Juice It or Lose It (GameJuice)](https://gamejuice.co.uk/resources/juice-it-or-lose-it)
- [Disney's 12 Animation Principles Applied to Games](https://gamejuice.co.uk/articles/disney-12-animation-principles-games)
- [12 Principles for Game Animation — Chris Totten](https://totter87.medium.com/12-principles-for-game-animation-a9137ef44345)
- [Game feel on the web: squash, shake, and the art of juice](https://valdemird.com/blog/game-feel-on-the-web/)
- [Rare Loot Box Rewards Trigger Larger Arousal and Reward Responses (PMC)](https://pmc.ncbi.nlm.nih.gov/articles/PMC7882574/)
- [The Psychology of Loot Boxes — Intenta Digital](https://intenta.digital/game-design/psychology-of-loot-boxes/)
- [How loot boxes borrowed probability design from slot machines](https://playercounter.com/how-loot-boxes-borrowed-probability-design-from-slot-machines)
- [Loot Boxes, Gacha, And The "Near-Miss" Effect](https://geekvibesnation.com/loot-boxes-gacha/)
- [Derived Relations and Attentional Bias for Near-Misses in Slot Machines (NCBI)](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12657543/)
- [The Slot Machine Psyche — Variable Ratio Reinforcement (PSU)](https://www.psu.com/news/the-slot-machine-psyche-how-variable-ratio-reinforcement-drives-modern-gaming-engagement/)
- [Audio/Visual cues for loot drops need to be enhanced — Anthem Dev Tracker](https://devtrackers.gg/anthem/p/a34635a0-no-spoilers-audio-visual-cues-for-loot-drops-need-to-be-enhanced)
- [ARPG Style Loot Beams — Borderlands 2 Nexus](https://www.nexusmods.com/borderlands2/mods/179)
- [ARPGStyleLootBeams.blcm — BLCMods (GitHub)](https://github.com/BLCM/BLCMods/blob/master/Borderlands%202%20mods/OurLordAndSaviorGabeNewell/ARPGStyleLootBeams.blcm)
- [Boons — Hades Wiki](https://hades.fandom.com/wiki/Boons) · [Boons/Hades II](https://hades.fandom.com/wiki/Boons/Hades_II)
- [Hades Boon Rarity Guide — dbltap](https://www.dbltap.com/posts/hades-boon-rarity-guide-to-standard-and-special-boons-01ek30qqebzt)
- [Eurydice and Boon Level vs Rarity — Steam 토론](https://steamcommunity.com/app/1145360/discussions/2/3124866920645157274/) · [Rarity vs lvl?](https://steamcommunity.com/app/1145360/discussions/0/2796126653263362641/)
- [Genshin Impact 5-Star Wish Animation 분석](https://img.krmangalam.edu.in/star-base/genshin-impact-5-star-wish-animation-secrets-1764806225)
