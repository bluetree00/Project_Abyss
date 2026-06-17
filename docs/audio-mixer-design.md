# 오디오 믹서 도입 설계서 (계획서)

> 대상: Unity 6, RelicFairy / 작성일: 2026-06-17
> 상태: **계획 단계 — 코드 구현 전. 본 문서 합의 후 단계별 착수.**
> 관련: [SoundManager.cs](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs), [SoundEventTableSO.cs](../Assets/RelicFairy/Systems/Sound/SoundEventTableSO.cs), [SoundKey.cs](../Assets/RelicFairy/Utils/SoundKey.cs)

---

## 0. TL;DR

- 현재 볼륨은 `source.volume`에 직접 곱하는 방식(`volume * _bgmVolume`). **마스터 볼륨·UI 채널·더킹·dB 곡선이 전부 없다.**
- **AudioMixer + 그룹(Master/BGM/SFX/UI) + 노출 파라미터(dB)** 로 교체한다. 볼륨 슬라이더는 이 노출 파라미터를 움직인다.
- SoundManager는 순수 C# 클래스라 직렬화 참조를 못 가진다 → **믹서를 Addressable로 로드**해 주입한다(기존 `SetEventTable` 패턴 그대로).
- **믹서 로드 실패 시 현행 곱셈 방식으로 graceful fallback** — 오프라인/CDN 실패에도 소리는 난다.
- ⚠️ **믹서 그룹·노출 파라미터·스냅샷 authoring은 Unity 에디터 수동 작업**(파라미터 Expose는 API로 불가, `execute_code`도 사용 불가 — [reference 메모] 참조). 본 설계가 authoring 스펙을 정확히 제공하고, 코드는 노출 이름으로 get/set만 한다.
- 구현은 3페이즈: **P1 믹서 골격+볼륨 dB화+폴백**, **P2 옵션 UI 슬라이더**, **P3(선택) 더킹·일시정지 연동**.

---

## 1. 현행 구조와 한계

| 항목 | 현행 | 위치 |
|---|---|---|
| 채널 | Bgm / Effect 2개 (`Define.Sound`) | [Define.cs:29](../Assets/RelicFairy/Utils/Define.cs#L29) |
| 볼륨 적용 | `source.volume = volume * _bgmVolume` (선형 곱) | [SoundManager.cs:153](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L153), [:319](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L319) |
| 저장 | PlayerPrefs `sound_bgm_vol` / `sound_effect_vol` | [:11](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L11) |
| setter | `SetBgmVolume` / `SetEffectVolume` (**현재 호출처 0건**) | [:36](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L36) |
| 믹서 자산 | **없음** | — |
| 옵션/볼륨 UI | **없음** | — |

**한계**
1. **마스터 볼륨 없음** — 전체 음량 일괄 조절 불가.
2. **UI음이 SFX 채널에 섞임** — UI음/전투음 분리 조절 불가.
3. **선형 곱** — 인지 음량과 안 맞음(0.5가 절반으로 안 들림). 표준은 dB 곡선.
4. **더킹 불가** — 보스 등장/컷신에서 BGM을 일시적으로 낮출 수단 없음.
5. 곱셈 방식은 **재생 중 이펙트에 실시간 미반영**(#6) — 믹서는 그룹 단위라 자동 해결.

---

## 2. 목표 채널 구성

```
Master (exposed: MasterVolume)
├── BGM  (exposed: BgmVolume)
├── SFX  (exposed: SfxVolume)
└── UI   (exposed: UiVolume)
```

- **Master**: 전체. 옵션 "마스터 볼륨".
- **BGM**: 배경음. 루프, 동시 1개.
- **SFX**: 전투/월드/픽업 등 게임플레이 효과음(현재 `Effect` 채널 + 풀 전체).
- **UI**: 버튼 클릭/호버/팝업 등. (현재 SFX와 섞여 있음 → 분리)

> **결정 필요 ①**: UI를 별도 채널로 둘지(4그룹), v1은 SFX에 합쳐 3그룹(Master/BGM/SFX)으로 갈지. 본 설계는 **4그룹 권장**(옵션에서 UI음 따로 끄고 싶은 니즈가 흔함), 단 라우팅만 다를 뿐 코드 골격은 동일.

**라우팅 매핑**

| AudioSource | 출력 그룹 |
|---|---|
| `@Sound/Bgm` (고정) | Master/BGM |
| `@Sound/Effect` (고정, 현재 사실상 미사용) | Master/SFX |
| `EffectPool/EffectSource_NN` (풀 전체) | Master/SFX |
| (신규) UI 재생 경로 | Master/UI |

> UI 채널 적용 방법: `PlayEvent`/`PlayEffectAsync`에 채널 인자를 추가하거나, UI 전용 키 접두사(`sfx_ui_`)를 SFX와 분리 라우팅. **권장: 명시적 채널 enum 인자**(아래 §4 API) — 키 문자열 규칙 의존보다 안전.

---

## 3. 볼륨 → dB 변환

슬라이더 선형값 `v ∈ [0,1]` → 믹서 dB:

```
dB = (v <= 0.0001f) ? -80f : Mathf.Log10(v) * 20f
```

- `v=1` → 0dB, `v=0.5` → ≈ -6dB, `v=0.1` → -20dB, `v=0` → -80dB(무음).
- 역변환(믹서값 로드 시): `v = Mathf.Pow(10f, dB / 20f)`. 단 **권위는 PlayerPrefs 선형값**으로 유지, 믹서는 출력 대상.

---

## 4. SoundManager 변경 설계

### 4.1 필드/상수 추가
```
private const string kMasterVolKey = "sound_master_vol";
private const string kUiVolKey     = "sound_ui_vol";   // UI 채널 채택 시
private const string kMixerParamMaster = "MasterVolume";
private const string kMixerParamBgm    = "BgmVolume";
private const string kMixerParamSfx    = "SfxVolume";
private const string kMixerParamUi     = "UiVolume";

private AudioMixer _mixer;                 // Addressable 주입
private AudioMixerGroup _bgmGroup, _sfxGroup, _uiGroup;
private float _masterVolume = 1f;
private float _uiVolume = 1f;
```

### 4.2 믹서 주입 (AppBootstrapper에서, `SetEventTable`과 동형)
```
public void SetMixer(AudioMixer mixer)
{
    _mixer = mixer;
    if (_mixer == null) return;
    _bgmGroup = _mixer.FindMatchingGroups("Master/BGM").FirstOrDefault();
    _sfxGroup = _mixer.FindMatchingGroups("Master/SFX").FirstOrDefault();
    _uiGroup  = _mixer.FindMatchingGroups("Master/UI").FirstOrDefault();
    RouteExistingSources();      // 고정 소스 + 풀 전체 outputAudioMixerGroup 지정
    ApplyAllVolumesToMixer();    // PlayerPrefs 로드값을 dB로 SetFloat
}
```
AppBootstrapper 추가(현재 [InitSoundTableAsync:665](../Assets/RelicFairy/Systems/Bootstrapper/Scripts/AppBootstrapper.cs#L665) 옆):
```
var mixer = await addr.TryLoadAssetAsync<AudioMixer>("GameAudioMixer");
if (mixer != null) Managers.Sound?.SetMixer(mixer);
```

### 4.3 볼륨 setter — dB 경로 + 폴백 분기
```
public void SetBgmVolume(float v)
{
    _bgmVolume = Mathf.Clamp01(v);
    PlayerPrefs.SetFloat(kBgmVolKey, _bgmVolume); PlayerPrefs.Save();
    if (_mixer != null) _mixer.SetFloat(kMixerParamBgm, LinearToDb(_bgmVolume));
    else { var s = GetAudioSource(Define.Sound.Bgm); if (s) s.volume = _bgmVolume; } // 폴백
}
```
- Master/SFX/UI도 동형. **믹서 있으면 SetFloat, 없으면 현행 곱셈 폴백.**

### 4.4 재생 경로의 per-clip 볼륨 의미 변경
- 믹서 채택 후 `source.volume`은 **그 사운드의 상대 음량(0~1)만** 담는다. 채널·마스터 음량은 믹서가 처리.
- 즉 [:153](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L153) `volume * _bgmVolume` → `volume`, [:319](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L319) `volume * _effectVolume` → `volume` 로 변경 (믹서 경로일 때).
- **폴백 경로(믹서 null)에서는 기존 곱셈 유지** → 분기 필요. (한 줄짜리 헬퍼 `ChannelScale(type)`로 믹서 유무에 따라 1f 또는 `_xxxVolume` 반환)

### 4.5 신규 소스 생성 시 라우팅
- `CreatePooledAudioSource()`([:348](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L348))에서 `source.outputAudioMixerGroup = _sfxGroup;` (믹서 있을 때).
- `EnsureAudioSource`도 동일.

### 4.6 UI 채널 재생 API (UI 4그룹 채택 시)
```
public void PlayUi(string key, float volume = 1f) => PlayPooledEffect(..., group: _uiGroup, ...);
```
또는 `PlayEvent`에 채널 파라미터 추가. UIButtonFeedback/UI_Button 등이 호출.

---

## 5. 더킹 (Phase 3, 선택)

보스 등장·중요 컷신에서 BGM을 일시 감쇠.

- **권장: AudioMixer 스냅샷** — `Default` / `BgmDucked`(BGM 그룹만 -12dB 등) 2개를 authoring.
- API: `Managers.Sound.TransitionSnapshot("BgmDucked", 0.4f)` → 내부 `snapshot.TransitionTo(time)`.
- **사용자 볼륨 설정과 충돌 없음** — 스냅샷은 노출 파라미터와 독립적으로 합성됨.
- 호출처 후보: `SoundEvent.BossAppear`([SoundEvent.cs:6](../Assets/RelicFairy/Utils/SoundEvent.cs#L6)) 발생 시 덕트, 보스 처치/페이즈 종료 시 복귀.

> v1에서는 제외 가능. **결정 필요 ②**: 더킹을 이번 작업에 포함할지.

---

## 6. 일시정지 연동 (Phase 3, 선택)

- 현재 이펙트 릴리즈는 `UnscaledDeltaTime`([:369](../Assets/RelicFairy/Systems/Managers/Scripts/SoundManager.cs#L369))이라 일시정지 중에도 진행. [TimeScaleArbiter](../) Pause와 사운드는 독립.
- 일시정지 시 게임플레이 음 정지 원하면: **SFX/UI 그룹만 스냅샷으로 감쇠** 또는 `AudioListener.pause`(전체 정지, BGM 포함 주의).
- **결정 필요 ③**: 일시정지 시 (a) 그대로 재생 / (b) SFX만 멈춤 / (c) 전체 멈춤.

---

## 7. 믹서 자산 Authoring 스펙 (수동 에디터 작업)

> ⚠️ 파라미터 Expose·그룹 생성·스냅샷은 코드 API로 불가 → **사용자가 Unity 에디터에서 직접 authoring**. 아래 스펙대로 만들면 코드가 이름으로 바인딩한다.

**자산 경로**: `Assets/RelicFairy/Systems/Sound/AudioMixer/GameAudioMixer.mixer`
**Addressable 키**: `GameAudioMixer` (그룹 등록은 기존 SoundEventTable과 동일 방식)

**그룹 트리**: Master → BGM, SFX, UI

**노출 파라미터** (각 그룹 Attenuation > Volume 우클릭 → Expose, 이름 정확히):
| 그룹 | 노출 이름 |
|---|---|
| Master | `MasterVolume` |
| BGM | `BgmVolume` |
| SFX | `SfxVolume` |
| UI | `UiVolume` |

**스냅샷(P3 채택 시)**: `Default`(기본), `BgmDucked`(BGM Volume -12dB).

---

## 8. 영향 범위 / 호환성

| 대상 | 영향 |
|---|---|
| 기존 BGM 재생 3곳 (GameRunBootstrapper) | **무변경** — `PlayBgmAsync` 시그니처 유지, 내부만 dB |
| 기존 PlayEvent 4곳 | **무변경** |
| MonsterAnimEventReceiver `PlayEffectAsync` | **무변경** (SFX 채널 자동) |
| PlayerPrefs 키 | `sound_master_vol`, `sound_ui_vol` **신규 추가** (기존 2개 유지) |
| 믹서 로드 실패 | **현행 곱셈 폴백** — 회귀 없음 |

**기존 코드의 동작은 믹서 유무와 무관하게 보존**된다. 믹서는 "더 나은 출력 경로"를 추가하는 것이지 호출 규약을 바꾸지 않는다.

---

## 9. 단계별 착수 계획

**Phase 1 — 믹서 골격 + 볼륨 dB화 (코어)**
1. (사용자) `GameAudioMixer.mixer` authoring + Addressable 등록 → verify: 그룹 4개, 노출 4개
2. SoundManager: `SetMixer`/라우팅/dB setter/폴백 분기/마스터·UI 필드 → verify: 컴파일 0 에러
3. AppBootstrapper: 믹서 Addressable 로드 + 주입 → verify: 플레이 시 BGM/SFX가 해당 그룹으로 출력(믹서 창에서 레벨 미터 확인)
4. 믹서 제거(폴백) 시에도 소리 정상 → verify: 일시적으로 키 오타 줘서 폴백 경로 확인

**Phase 2 — 옵션 UI 볼륨 슬라이더** (별도 UI 작업, `Provider→Presenter→View`)
- Master/BGM/SFX(/UI) 슬라이더 → `Set*Volume` 호출 → PlayerPrefs 저장/복원 → verify: 슬라이더 조작이 실시간 반영, 재시작 후 유지
- 배치 후보: 일시정지([UI_Pause](../Assets/RelicFairy/UI/Popup/UI_Pause.cs)) / 로비 옵션

**Phase 3 — 더킹·일시정지 (선택)**
- 스냅샷 authoring + `TransitionSnapshot` API + BossAppear/Pause 훅 → verify: 보스 등장 시 BGM 감쇠/복귀

---

## 10. 결정 필요 사항 (착수 전 합의)

| # | 결정 | 권장 |
|---|---|---|
| ① | 채널 수 — 3그룹(UI=SFX) vs **4그룹(UI 분리)** | **4그룹** |
| ② | 더킹 P1 포함 vs P3로 분리 | **P3 분리**(코어 먼저) |
| ③ | 일시정지 시 음향 — 유지 / SFX만 정지 / 전체 정지 | **SFX만 정지** (BGM은 유지) |
| ④ | 믹서 자산 Addressable 로드 방식 확정 | ✅ (SoundEventTable과 동일) |

---

## 11. 대안 검토 (구조 설계 체크)

- **대안 A: 현행 곱셈 유지 + 마스터 볼륨만 추가** — 가장 단순하나 더킹/UI분리/dB곡선 모두 못 얻음. 출시 품질엔 부족.
- **대안 B(채택): AudioMixer** — Unity 표준, 더킹·스냅샷·dB·그룹 일괄 해결. 비용은 믹서 수동 authoring 1회 + setter dB 분기.
- **대안 C: FMOD/Wwise 미들웨어** — 오버킬. 현 규모/팀에 불필요한 의존성·빌드 복잡도.

→ **B 채택**. 폴백 분기로 안정성 확보, 호출 규약 무변경으로 surgical.
