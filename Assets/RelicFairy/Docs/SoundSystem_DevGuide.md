# 사운드 시스템 사용 가이드 (SoundSystem DevGuide)

> 대상: RelicFairy 개발팀 / 작성일: 2026-06-18
> 코드: [SoundManager.cs](../Systems/Managers/Scripts/SoundManager.cs) · 키: [SoundKey.cs](../Utils/SoundKey.cs) · 이벤트: [SoundEvent.cs](../Utils/SoundEvent.cs) · 이벤트테이블: [SoundEventTableSO.cs](../Systems/Sound/SoundEventTableSO.cs)
> 믹서/임포트 설계: [docs/audio-mixer-design.md](../../../docs/audio-mixer-design.md)

---

## 0. TL;DR (가장 자주 쓰는 3줄)

```csharp
Managers.Sound?.PlayBgmAsync(SoundKey.Bgm.InGame).Forget();          // 배경음
Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.PlayerAttack).Forget(); // 2D 효과음
Managers.Sound?.PlayEvent(SoundEvent.ItemPickup);                    // 이벤트 효과음(권장)
```

소리가 나려면 → ① 오디오 파일이 **그 키로 Addressable 등록**돼 있어야 하고, ② 코드가 **그 키로 호출**해야 한다. (§4)

---

## 1. 핵심 모델

모든 사운드는 **문자열 키**로 호출한다. SoundManager가 그 키로 **AddressableManager에서 AudioClip을 로드**(캐시)해 재생한다.

```
Managers.Sound.PlayBgmAsync("bgm_ingame")
   └─ AddressableManager.TryLoadAssetAsync<AudioClip>("bgm_ingame")  // 최초 1회 로드 후 캐시
        └─ @Sound/Bgm 소스(루프) 또는 EffectPool(풀링)에서 재생
```

- 진입점은 항상 **`Managers.Sound`** (서비스 로케이터). null 안전하게 `Managers.Sound?.`로 호출.
- 비동기 메서드(`...Async`)는 `.Forget()`로 fire-and-forget (await 불필요).
- 초기화/이벤트테이블/믹서 주입은 [AppBootstrapper](../Systems/Bootstrapper/Scripts/AppBootstrapper.cs)가 앱 시작 시 자동 수행 — **사용처에서 Init 호출 불필요.**

---

## 2. 배경음 (BGM)

`@Sound/Bgm` 단일 루프 채널.

```csharp
// 재생 — 같은 곡이 이미 재생 중이면 다시 틀지 않음(내부 체크)
Managers.Sound?.PlayBgmAsync(SoundKey.Bgm.InGame).Forget();

// 볼륨/피치 지정 (선택)
Managers.Sound?.PlayBgmAsync(SoundKey.Bgm.Boss, volume: 0.8f, pitch: 1f).Forget();

// 정지
Managers.Sound?.StopBgm();
```

| 상수 ([SoundKey.Bgm](../Utils/SoundKey.cs)) | 키 문자열 | 용도 |
|---|---|---|
| `Logo` | `bgm_logo` | 로고 |
| `Lobby` | `bgm_lobby` | 로비 |
| `InGame` | `bgm_ingame` | 인게임 전투 |
| `Boss` | `bgm_boss` | 보스 |
| `Result` | `bgm_result` | 결과 |

> BGM은 비동기 로드 레이스를 내부 버전 카운터로 방어한다 — 짧은 간격으로 곡을 연속 전환해도 마지막 요청만 재생된다.

---

## 3. 효과음 (SFX)

### 3.1 방식 A — 직접 키 재생 (2D)
코드에서 클립 키를 직접 지정. 간단하지만 코드가 클립 키에 결합된다.

```csharp
Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.PlayerAttack).Forget();
Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.PlayerDash, volume: 0.7f).Forget();
```

### 3.2 방식 B — 이벤트 테이블 (권장)
코드는 **이벤트 ID**만 던지고, "어떤 클립을 어떤 볼륨으로" 는 **데이터(SO)** 가 결정 → 클립 교체·볼륨 튜닝을 코드 수정 없이 Inspector에서 처리.

```csharp
Managers.Sound?.PlayEvent(SoundEvent.RoomClear);
Managers.Sound?.PlayEvent(SoundEvent.GoldPickup);
```

매핑은 `SoundEventTable` 에셋(Addressable 키 `SoundEventTable`)의 엔트리 배열:

| eventId | sfxKey | volume |
|---|---|---|
| `room_clear` | `sfx_room_clear` | 1.0 |
| `item_pickup` | `sfx_item_pickup` | 0.8 |

> 이벤트 ID 상수는 [SoundEvent.cs](../Utils/SoundEvent.cs). 테이블에 매핑이 없으면 **조용히 무시**(에러 아님).
> ⚠️ **이벤트 방식은 2D 전용** — 위치 음향이 필요하면 §3.3.

### 3.3 3D 위치 음향
거리 감쇠가 필요한 월드 사운드(타격·폭발 등). 풀링된 소스로 재생.

```csharp
Managers.Sound?.PlayEffectAtAsync(SoundKey.Sfx.MonsterHit, hitPosition).Forget();

// 거리 파라미터 조정
Managers.Sound?.PlayEffectAtAsync(
    SoundKey.Sfx.MonsterDie, enemy.position,
    volume: 1f, pitch: 1f,
    minDistance: 2f,    // 이 거리 안: 최대 음량
    maxDistance: 25f,   // 이 거리 밖: 거의 안 들림
    rolloffMode: AudioRolloffMode.Logarithmic
).Forget();
```

- 전제: 씬에 **`AudioListener`** 1개(보통 메인 카메라).
- `spatialBlend=1`(완전 3D)로 자동 설정. 2D 재생(`PlayEffectAsync`)은 `spatialBlend=0`.

| 상수 ([SoundKey.Sfx](../Utils/SoundKey.cs)) | 키 문자열 |
|---|---|
| `PlayerAttack/Hit/Die/Dash/Jump` | `sfx_player_*` |
| `MonsterHit/Die/Attack` | `sfx_monster_*` |
| `RoomClear` / `DoorOpen` | `sfx_room_clear` / `sfx_door_open` |
| `ItemPickup` / `GoldPickup` | `sfx_item_pickup` / `sfx_gold_pickup` |
| `UiClick` / `UiHover` | `sfx_ui_click` / `sfx_ui_hover` |

### 3.4 이미 로드된 AudioClip 직접 재생 (드묾)
Addressable이 아니라 손에 든 `AudioClip` 참조로 재생할 때:
```csharp
Managers.Sound?.PlayEffect(myClip, volume: 1f);
Managers.Sound?.PlayEffectAt(myClip, position);
```

---

## 4. 새 사운드 추가 절차 (체크리스트)

예) "대시" 효과음 추가:

1. **오디오 파일 import** — `.wav`/`.ogg`를 프로젝트에 넣는다.
   - 임포트 설정은 §6 / [설계서 §7.5](../../../docs/audio-mixer-design.md) 가이드대로 (짧은 SFX = *Compressed In Memory*, 3D음 = *Force To Mono*, 긴 BGM = *Streaming*).
2. **Addressable 등록** — `Window > Asset Management > Addressables > Groups`에서 클립을 그룹에 넣고, **Address를 키 문자열로** 지정 (예: `sfx_player_dash`).
   - 키 상수는 대개 이미 [SoundKey.cs](../Utils/SoundKey.cs)에 있다 (`PlayerDash = "sfx_player_dash"`). 없으면 상수 먼저 추가.
3. **호출 배선** — 재생 시점 코드에서:
   ```csharp
   Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.PlayerDash).Forget();
   ```
   - 이벤트 방식으로 갈 거면: `SoundEventTable` 에셋에 `(eventId, sfxKey, volume)` 엔트리 추가 → `PlayEvent("...")`.

> SoundKey/SoundEvent의 상수는 "예약된 어휘"일 뿐 — 실제 소리는 **2번(Addressable 등록) + 3번(호출 배선)** 이 돼야 난다.

---

## 5. 볼륨 조절

```csharp
Managers.Sound?.SetMasterVolume(0.8f);  // 0~1, 전체
Managers.Sound?.SetBgmVolume(0.5f);
Managers.Sound?.SetEffectVolume(0.7f);
Managers.Sound?.SetUiVolume(0.6f);

float v = Managers.Sound.BgmVolume;     // 현재값 읽기(프로퍼티)
```

- PlayerPrefs에 자동 저장/복원 (`sound_master_vol` / `sound_bgm_vol` / `sound_effect_vol` / `sound_ui_vol`).
- **믹서 자산이 있으면** dB 곡선으로 그룹 음량 제어, **없으면**(현재) 소스 볼륨 곱셈 폴백 — 둘 다 동작.
- ⚠️ 현재 이 setter를 호출하는 **옵션 UI는 없음**(P2 예정). 코드에서 직접 호출은 지금도 가능.

---

## 6. 오디오 파일 임포트 설정 (요약)

| 분류 | Load Type | 비고 |
|---|---|---|
| BGM (긴 루프) | **Streaming** | 메모리에 안 올림 |
| 일반 SFX (짧음) | **Compressed In Memory** | 메모리↔CPU 트레이드 |
| 빈발/즉시 SFX (타격·대시) | **Decompress On Load** | 디코드 지연 0 |

- 3D positional SFX → **Force To Mono** (좌우 분리 무의미).
- 긴급하지 않은 음 → **Load In Background**.
- 자세한 근거는 [설계서 §7.5](../../../docs/audio-mixer-design.md).

---

## 7. 현재 배선 현황 (실제로 울리는 소리)

| 트리거 | 호출 | 방식 | 위치 |
|---|---|---|---|
| 인게임 진입 | `PlayBgmAsync(Bgm.InGame)` | BGM | [GameRunBootstrapper.cs](../Systems/Bootstrapper/Scripts/GameRunBootstrapper.cs) |
| 아이템 획득 | `PlayEvent(ItemPickup)` | 이벤트 | [RunItemInventory.cs](../Systems/Stage/RunGame/RunItemInventory.cs) |
| 골드 획득 | `PlayEvent(GoldPickup)` | 이벤트 | [GameRunSession.cs](../Systems/Stage/RunGame/GameRunSession.cs) |
| 룸 클리어 | `PlayEvent(RoomClear)` | 이벤트 | [RoomWaveController.cs](../Systems/Stage/RunGame/RoomWaveController.cs) |
| 몬스터 애님 이벤트 | `PlayEffectAsync(key)` | 직접 | [MonsterAnimEventReceiver.cs](../Characters/Monster/Monster/Core/MonsterAnimEventReceiver.cs) |

그 외(플레이어 공격/피격/대시/점프, 보스 등장, 문 열림, UI 클릭/호버)는 **상수만 존재하고 미배선** 상태 — 사운드 작업으로 채워나갈 대상.

---

## 8. 한계 / 주의 (Gotcha)

1. **이벤트(`PlayEvent`)는 2D 전용.** 위치 음 필요 시 `PlayEffectAtAsync` 직접 호출. (`PlayEventAt`류 없음)
2. **위치 음은 고정 좌표 재생** — 호출 시점 좌표에 음을 박고 재생한다. 날아가는 발사체·움직이는 적의 **지속음**처럼 소스가 이동해야 하면 부적합(1회성 타격/폭발음엔 문제없음).
3. **풀 크기 16(자동 확장)** — 동시 SFX 폭주 시 소스가 늘어난다. 극단적 상황만 주의.
4. **클립 미등록 시** `PlayEffectAsync`는 콘솔에 `AudioClip missing` 경고만 남기고 무음 — 에러로 죽지 않는다(배선 누락 디버깅 단서).
5. **일시정지와 사운드는 독립** — 이펙트 릴리즈가 `UnscaledDeltaTime`이라 일시정지 중에도 진행/재생된다. (P3에서 정책 결정 예정)

---

## 9. API 요약

| 메서드 | 용도 |
|---|---|
| `PlayBgmAsync(key, volume, pitch)` | BGM 재생(루프) |
| `StopBgm()` | BGM 정지 |
| `PlayEffectAsync(key, volume, pitch)` | 2D 효과음(키) |
| `PlayEffectAtAsync(key, pos, volume, pitch, minDist, maxDist, rolloff)` | 3D 위치 효과음(키) |
| `PlayEvent(eventId)` | 이벤트 테이블 기반 효과음(2D) |
| `PlayEffect(clip, ...)` / `PlayEffectAt(clip, ...)` | 로드된 AudioClip 직접 재생 |
| `SetMasterVolume/SetBgmVolume/SetEffectVolume/SetUiVolume(0~1)` | 볼륨 |
| `BgmVolume/EffectVolume/MasterVolume/UiVolume` | 현재 볼륨 읽기 |

> `SetEventTable` / `SetMixer` / `Init` / `Clear`는 부트스트랩·매니저 내부용 — 일반 게임플레이 코드에서 호출하지 않는다.
