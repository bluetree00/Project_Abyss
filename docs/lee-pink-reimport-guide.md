# 에셋 핑크(매젠타) 복구 가이드 — 재임포트

> 대상: LEE (dev/lee)
> 증상: 풀 받은 뒤에도 베이스캠프 바닥 맵 리소스, 보스 오라/이펙트가 **핑크(매젠타)** 로 보임
> 결론: **저장소 콘텐츠는 정상**입니다. 거의 확실히 **로컬 셰이더 캐시(Library) 불일치** 문제이므로, 아래 재임포트 절차로 해결됩니다.

---

## 왜 이런 일이 생겼나 (배경)

이전에는 외부 에셋을 전부 LFS로 추적했지만, 구조를 바꿔 **실제 사용되는 리소스만 `Assets/RelicFairy/_Imported/` 로 옮겨 추적**하고, 나머지 원본은 추적 제외 폴더(`Assets/_ThirdParty/`)에 보관합니다.

- KBG가 최근 **사용 에셋 다수를 `_ThirdParty` → `_Imported` 로 이동**했습니다(커밋 `c7c0cbefa`). 파일+`.meta`를 함께 옮겨 **GUID는 보존**됐지만, 에셋의 물리적 경로가 바뀌었습니다.
- KBG 로컬은 기존 `Library/`(컴파일된 셰이더 캐시)가 있어 정상 렌더되지만, **LEE는 이동 전 경로 기준의 낡은 캐시**가 남아 셰이더가 에러 상태(=핑크)로 표시될 수 있습니다.

> 핑크(매젠타)는 **셰이더 누락/컴파일 실패** 신호입니다. 텍스처 누락은 흰색이지 핑크가 아닙니다.

KBG 쪽에서 전수 검사한 결과:
- 사용 에셋이 `_ThirdParty`(공유 안 됨)에 남은 것: **0개**
- 사용 에셋 미추적: **0개** / LFS blob 원격 누락: **0개**
- 깨진 셰이더: **1개**(`VolumetricBlood2.mat`) → **이미 수정 완료**

즉 LEE가 최신을 받고 **재임포트만 하면** 핑크가 풀려야 합니다.

---

## 복구 절차 (순서대로)

### 0. 내 작업 보호 (먼저!)
```bash
git status                 # 변경사항 확인
git add -A && git commit -m "wip: 재임포트 전 작업 백업"   # 또는 git stash
```

### 1. 최신 develop 반영 확인
```bash
git fetch origin
git log --oneline -1 origin/develop      # c7c0cbefa 이상인지 확인
git merge origin/develop                  # dev/lee에 최신 develop 머지 (또는 develop 체크아웃)
git rev-parse HEAD                         # 현재 커밋 해시 기록 (문제 시 KBG에 전달)
```
- `HEAD`가 `c7c0cbefa`(또는 그 이후)가 **아니면**, 에셋 이동 전 상태라 이게 핑크 원인입니다. 머지/풀부터 끝내세요.

### 2. LFS 실제 파일 받기
```bash
git lfs pull
```
- 확인: 텍스처(.png 등)를 텍스트 에디터로 열었을 때 `version https://git-lfs...` 로 시작하면 **아직 포인터 상태** → `git lfs pull` 다시.

### 3. Unity 셰이더 캐시 재생성 (핵심)
1. **Unity 에디터를 완전히 종료**
2. 프로젝트 루트의 **`Library/` 폴더 삭제**
   - (선택) `Temp/`, `obj/` 도 함께 삭제하면 더 깨끗
   - `Library/`는 캐시라 삭제해도 안전하며 Unity가 다시 생성합니다(첫 실행이 오래 걸릴 수 있음)
3. **Unity 다시 열기** → 전체 재임포트 완료까지 대기

> 폴더 통째 삭제가 부담되면, Unity 안에서 **`Assets ▸ Reimport All`** 로도 가능(다만 Library 삭제가 가장 확실).

### 4. 결과 확인
- 베이스캠프 / 보스 씬을 열어 핑크가 사라졌는지 확인
- `Console` 창에서 에러(특히 shader/import) 확인

---

## 그래도 핑크가 남으면 — KBG에 아래 정보 전달

이 정보 한 세트면 어느 파일이 문제인지 **추측 없이 즉시** 특정됩니다.

1. **핑크 오브젝트 클릭 → Inspector의 Material ▸ Shader 필드 값**
   - 예: `Hidden/InternalErrorShader` 인지, `KriptoFX/...` 같은 **특정 셰이더 이름**인지
2. `git rev-parse HEAD` 결과 (현재 커밋 해시)
3. Console 에러 메시지 (shader/import 로 필터)
4. 해당 머티리얼/셰이더 **파일 경로** 와, 그 파일이 실제로 존재하는지

---

## 참고: KBG가 방금 고친 것
- `Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/Arrow/Materials/VolumetricBlood2.mat`
  - 끊긴 셰이더 GUID(`ae42f923…`, 어디에도 없음) → 같은 팩 표준 셰이더 **UberParticles**(`b85d6def…`)로 재연결. develop 반영 후 함께 받게 됩니다.
