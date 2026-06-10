# 리포지토리 에셋 위생 — 정리 메모 (후속)

작성: 2026-06-10. **출시 전 정리 후보. 오늘 push가 만든 문제 아님(선재 이슈).** 지금은 기록만, 실행은 팀 협의 후.

## 배경

- dev/KBG-D 첫 원격 push(5/23~6/10 누적 46커밋)에서 LFS 약 7.7GB / 2,232객체 업로드. 점검 결과 **오늘(06-10) 게임플레이 작업분(6커밋)은 정상**(에셋 .meta 완비, 대용량/의도외 포함 없음, LFS 변경 0).
- 단, 일반 git 히스토리(LFS 아님)에 **비-LFS 대용량 약 3.6GB**가 적재돼 있음(주로 5/23~24 서드파티 임포트·이전 시점).

## 발견 (비-LFS 대용량)

| 분류 | 크기 | 비고 |
|---|---|---|
| `ServerData/*.bundle` | 712MB / 92파일 | Addressables **빌드 산출물**이 커밋됨 |
| `.tif` | 512MB / 271파일 | `.gitattributes` 미포함 → LFS여야 |
| `.exr` | 125MB / 56파일 | 미추적, `kloppenheim_06_puresky_4k.exr` 70.5MB(GitHub 50MB 경고 원인) |
| 서드파티 데모 `.unity` | 91/35/35/32MB… | AZURE AN_Demo 등(씬은 LFS 부적합 텍스트) |
| 기타 | — | .hdr·.bmp·.tiff·.ttf·.anim(19MB)·Buto .asset(18MB) 등 비-LFS |

- 현 `.gitattributes` LFS 추적: png/jpg/tga/psd/fbx/obj/wav/ogg/mp4 (총 12,179파일). **커버리지 갭**: `.exr .tif .tiff .hdr .bmp .raw .cubemap`.

## 정리 계획 (출시 전, 팀 협의/백업 후)

1. **ServerData/ 무시 결정** — Addressables 빌드 산출물이면 `.gitignore`에 `ServerData/` 추가(재생성 가능 산출물).
2. **`.gitattributes` 확장** — `.exr .tif .tiff .hdr .bmp` 등 LFS 추가(향후 신규분만 LFS화).
3. **불필요 서드파티 데모 제거** — 미사용 데모 씬·텍스처 정리.
4. **`git lfs migrate`로 기존 히스토리 슬림화** — ⚠️ **히스토리 재작성**이라 모든 협업자 re-clone 필요. **반드시 팀 협의 + 백업(브랜치/미러) 후 진행.**

> ⚠️ 주의: 1~3은 **신규분만** 영향. 기존 히스토리 blob은 4의 migrate 없이는 줄지 않음. 1~3을 먼저 하고, migrate는 출시 직전 정리 윈도에 일괄.

## 비고

- 오늘 push 자체는 정상이며 이 정리는 급하지 않음(리포 용량/클론 시간 최적화 차원).
