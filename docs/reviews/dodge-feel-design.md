# RelicFairy — 회피(닷지/대시) 연출 & 이동 중 회피 자연스러움 리서치 종합

> 대상: Unity 6, Hades식 3D 로그라이크(액션) / 작성일: 2026-06-11
> 우리 현황: 코드 구동 `LocoDodgeState`(루트모션 아닌 dashSpeed×duration+거리보너스, 쿨다운, 회피=달리기 트리거로 Exit) · 가속 기반 이동(점진 램프 + MoveTowards) · 타이머 기반 i-frame 활성창(startDelay 0.05 + duration 0.3, `OnDodgeIFrame(bool)` 훅 노출, 시각 피드백 미연결)
> 표기: 출처 명확=링크, 근거 약함/특정게임 일반화=**[추측]**. 수치는 출처 게임 고유값이므로 우리 템포에 맞게 튜닝 전제.

## 0. TL;DR
- 자연스러움의 핵심은 "전이". 코드 구동 대시는 일관성 유리하나 로코모션→대시 전이에서 스냅·미끄러짐이 생기기 쉬움. 전이시간(crossfade)·접지 정렬·모멘텀·방향 규칙을 잡으면 루트모션 없이도 자연스러움.
- i-frame은 "보여야" 가치. 방금 만든 `OnDodgeIFrame(bool)` 훅을 잔상+색/림라이트 변화에 1:1로 켜고 끄면 됨.
- 회피는 systemic해야(방향·타이밍·i-frame·거리 상호작용) 마스터리 생김(Callisto 반면교사).
- 정량 앵커: Dark Souls 1 30FPS 프레임표(롤 11 i-frame + 부하별 회복/instability). 우리 0.3초 활성창=60FPS 환산 ~18프레임=관대한 편.
- 우선순위: ①방향/모멘텀 전이 규칙 → ②잔상+색변화 i-frame 가독성 → ③대시 스미어/먼지/트레일 → ④회복구간+입력버퍼 → ⑤퍼펙트닷지 슬로모/카메라/사운드.

## 1. 이동 중 회피의 자연스러움 — 애니메이션/모션
### 1-1. 전이에서 스냅·미끄러짐·방향꺾임 줄이기
Unity 공식: 트랜지션 접지 정렬로 foot slip·애니메이션 점프·모션 손실 방지. Fixed Duration(초단위)·짧은 고정초(0.05~0.1s)·Has Exit Time off·AnyState 트리거(큐 최우선)·Interruption Source 제어.
출처: https://docs.unity3d.com/6000.2/Documentation/Manual/class-Transition.html , https://docs.unity3d.com/Manual/class-BlendTree.html , https://unity.com/blog/engine-platform/state-machine-transition-interruptions
### 1-2. 루트모션 vs 코드 구동
루트모션=사실적이나 일관성↓, 코드=직접제어·예측가능하나 발미끄러짐 별도관리. 케이스별 결정.
출처: https://docs.unity3d.com/6000.3/Documentation/Manual/RootMotion.html , https://subscription.packtpub.com/book/game-development/9781785883910/6/ch06lvl1sec63/using-root-motion-to-create-a-dodge-move
**[추측]** 대시 거리·i-frame이 밸런싱 직결인 로그라이크는 코드 구동 적합, 루트모션은 접지·상체 시각보강만 부분적용 하이브리드. → 우리 LocoDodgeState 코드구동 선택과 정합.
### 1-3. 회피 방향 결정 — 입력 vs 이동/속도 vs 바라보는방향
입력 하드코딩 시 회전상태에 따라 의도와 다른 방향 위험(For Honor 사례). James Margaris Callisto 분석: 회피는 systemic해야, 공격 "쪽으로(into)" 회피가 히트박스 겹침 최소화. 출처: https://jmargaris.substack.com/p/dodging-in-the-callisto-protocol
**권고**: 입력 있으면 입력방향(카메라 상대 변환), 없으면 직전 이동속도 방향 → 폴백 바라보는방향.
### 1-4. 모멘텀/관성 보존, 시작·회복 구간
달리기 중 대시 모멘텀 스태킹·종료 후 미끄러짐 처리가 게임필 가름. 회피=시작→i-frame(front-loaded)→회복(back-loaded). 무적은 앞, 취약회복은 뒤=남발 억제.
**[추측]** "이동 속도벡터를 대시 시작속도에 합산" 구체공식은 직접근거 없음, 가속이동에 맞아 실험가치.
### 1-5. 애니메이션 캔슬+입력 버퍼링
입력 버퍼=직전동작 끝 ~10프레임 저장, 발동 직후 clear(너무 길면 오발동). 홀드입력은 단순 대안. 출처: https://medium.com/@yosispring/input-buffering-action-canceling-and-also-forbidden-knowledge-47a3f8a95151
### 1-6. 8방향 회피 정렬
블렌드 스페이스+입력/이동 방향과 클립 정렬, 캐릭터를 이동방향 회전정렬. 출처: https://forums.unrealengine.com/t/need-help-setting-up-an-8-way-roll-dodge-blend-space/132465

## 2. 회피 연출(juice/피드백)
### 2-1. i-frame 가독성 시각신호
플래시/색변화/반투명(고전 검증). 잔상(afterimage)=무적+속도감, Unity 파티클 Rate Over Distance·색/알파 변경·World space·Lifetime 페이드. 출처: https://justenyc.medium.com/creating-a-simple-afterimage-effect-for-your-sprite-in-unity-cdf1a8d0fc38 . **[추측]** 3D는 SkinnedMeshRenderer.BakeMesh 스냅샷 페이드. 스미어=실루엣 유지·내부 왜곡(가독성 우선). 출처: https://prolificstudio.co/blog/smear-frames/ . 디졸브 위상이동(Nier 퍼펙트닷지): https://halisavakis.com/my-take-on-shaders-teleportation-dissolve/ . 카메라 펀치/줌 0.1~0.3s, Vlambeer Art of Screenshake: https://www.youtube.com/watch?v=AJdEqssNZ-U
### 2-2. 대시 VFX
먼지·반짝임·파편, 스피드라인(포스트프로세스), 트레일. 출처: https://dev.epicgames.com/community/learning/tutorials/bZkn/unreal-engine-speedline-dash-vfx-tutorial-dynamic-motion-effects-post-process-material
### 2-3. 퍼펙트닷지/저스트닷지
슬로우모션이 핵심보상(Bayonetta Witch Time/GoW/Nier): ①성공 피드백 ②반격윈도우 ③적 회복프레임 읽기 도움. 출처: https://kotaku.com/nothing-beats-a-great-gaming-dodge-1827288664
### 2-4. 사운드
대시=whoosh, 발소리 별도설계로 보행과 차별. 출처: https://www.asoundeffect.com/product-tag/dash/
### 2-5. 기반이론
Swink Game Feel: 입력→반응 100ms 미만이면 지연 무감지. 히트스톱=충돌 시 수프레임 정지(퍼펙트닷지 반격에 적용). Hades=명료 피드백·무제한 대시, 단 Hades II는 자기VFX가 위험지대 가독성 붕괴(반면교사). 출처: https://en.wikipedia.org/wiki/Game_feel , https://critpoints.net/2017/05/17/hitstophitfreezehitlaghitpausehitshit/ , https://steamcommunity.com/app/1145350/discussions/2/4358999171577943234/

## 3. i-frame 타이밍 관례
### 3-1. 원칙
시동→활성무적(front-loaded)→취약회복(back-loaded). 부분무적 vs 전체무적. i-frame 짧을수록 관찰·정확반응 강제. 텔레그래프 명확해야 공정. 과의존=적패턴 설계 게을러짐. 출처: https://critpoints.net/2017/07/25/how-iframes-augment-dodge-rolls/ , https://critpoints.net/2023/02/20/frame-data-patterns-that-game-designers-should-know/
### 3-2. 정량앵커 Dark Souls 1 (30FPS)
Fast Roll(≤25%): i-frame 11, 회복 3, instability 7~8. Mid: 11/4/14. Fat: 11/9/25. Ninja Flip: 13/0/0. i-frame은 거의 동일, 차이는 총길이·회복·instability(피격 시 1.4x). 출처: https://darksouls.fandom.com/wiki/Rolling
### 3-3. 우리 0.3초 위치잡기
startDelay 0.05+duration 0.3 ≈ 60FPS 시동3+무적18프레임. 소울류(11~13)보다 관대. 빠른템포·다수투사체엔 관대함이 맞을 수 있으나 회복취약구간 거의없음(회피=달리기 바로Exit)→남발억제 약함→대시끝~복귀 사이 짧은 무적없는 회복프레임 검토.
### 3-4. 레퍼런스 요약
Hades(동작전체 무적, 다른행동시 잔여무적 소멸; 정확프레임 미확정[추측]). Elden Ring(i-frame 롤시작 ~0.5s, 부하3단계; 세부수치 커뮤니티 추정[추측]). Hollow Knight(기본대시 ~4프레임[추측], Shade Cloak 전체무적이나 함정엔 미적용). Returnal(대시간 취약창으로 연타무적 불가). HLD(i-frame 없음→추가→축소 튜닝 케이스). RoR2(공용닷지 없음, 빌드에 위임). DMC/Bayonetta(닷지오프셋·퍼펙트이베이드·Witch Time). 출처: https://hades.fandom.com/wiki/Gameplay_mechanics , https://eldenring.wiki.fextralife.com/Dodging , https://er-frame-data.nyasu.business/ , https://hollowknight.wiki.fextralife.com/Shade+Cloak , https://twinfinite.net/guides/returnal-dodge-dash-how/ , https://steamcommunity.com/games/257850/announcements/detail/594848467137959659 , https://riskofrain2.fandom.com/wiki/Survivors , https://devilmaycry.fandom.com/wiki/Perfect_Evade , https://bayonetta.fandom.com/wiki/Witch_Time

## 4. 레퍼런스 종합표
| 게임 | 회피방식 | 자연스러움 | 연출 |
|---|---|---|---|
| Hades | 무제한 코드대시·전체무적 | crisp애님·즉발 | 잔상+트레일(II는 VFX노이즈 주의) |
| Souls/Elden | 루트모션 롤·부하별 | 접지·무게감·instability 처벌 | 절제·텔레그래프 의존 |
| HLD | 짧은대시 | i-frame 튜닝 공정성 | 잔상/스미어 |
| Returnal | 코드대시·취약창 | 3D공중대시·반응성 | 스피드트레일 |
| Hollow Knight | 대시/Shade Cloak | 함정엔 무적미적용 긴장 | 디졸브풍 |
| DMC/Bayonetta | 닷지오프셋·퍼펙트 | 공격캔슬→콤보 | Witch Time 슬로모 |
가장 정량신뢰: Dark Souls 위키 롤표, Elden Frame Data Explorer. 설계원리: CritPoints(Celia Wagar), Margaris Callisto 분석.

## 5. RelicFairy 적용 권고·우선순위
### 5-1. 자연스러움(코드구동 유지)
1. 회피 방향규칙: 입력 있으면 입력방향(카메라 상대), 없으면 직전 이동속도방향→폴백 바라보는방향. 8방향클립 있으면 방향-클립 정렬.
2. 모멘텀 전이: 현재 속도벡터를 대시 시작속도에 일부 블렌딩해 방향튐 감소 **[추측·실험 권장]**.
3. 전이시간: 로코모션→대시 크로스페이드 짧은 고정초(0.05~0.1s), AnyState+Has Exit Time off. 대시→달리기 복귀도 동일.
4. 회복구간 신설: 현재 회복취약구간 거의없음→대시끝~복귀 사이 짧은 무적없는 프레임(0.05~0.1s)으로 남발 억제.
5. 입력버퍼: 대시입력 ~8~10프레임 버퍼·발동후 clear.
### 5-2. `OnDodgeIFrame(bool)` 훅 연출(직접적)
ON: 잔상(3D=SkinnedMeshRenderer 스냅샷 0.04~0.06s 간격 페이드[추측])+림라이트/틴트 색변화(+선택 디졸브 위상). OFF: 잔상중지+색 즉시원복(신호가 무적보다 오래 남지않게). 대시자체: 시작 먼지퍼프+진행 트레일+순간 국소 스미어(실루엣 유지).
### 5-3. 퍼펙트닷지(추가 동사 도입 시)
적공격 활성직전 회피 판정→짧은 슬로모(토글옵션)+강한 플래시/스파클+카메라줌+반격윈도우.
### 5-4. 우선순위(효과/비용)
1.[높음/저비용] 방향·모멘텀·전이시간 정리(자연스러움 체감 최대). 2.[높음/저비용] OnDodgeIFrame에 잔상+색변화(무적 가독성=공정성). 3.[중간] 대시 먼지/트레일/스미어. 4.[중간] 회복구간+입력버퍼. 5.[낮음/후순위] 퍼펙트닷지 슬로모·카메라·사운드.
**관통원칙(Hades II 반면교사)**: 어떤 연출도 적공격·위험지대·무적신호 가독성을 가리면 안 됨. 화려함보다 명료함.

## 부록 — 출처
Unity: class-Transition / class-BlendTree / RootMotion / State Machine Transition Interruptions. 디자인: Margaris Callisto, Yosi Spring 입력버퍼, Swink Game Feel, Vlambeer Screenshake, CritPoints iFrames/FrameData/Hitstop, Parry Everything DS롤. 연출: Justen Chong afterimage, Prolific Smear, Alisavakis dissolve, Unreal speedline, A Sound Effect dash. 게임데이터: Dark Souls Rolling 위키, Elden Frame Data Explorer/Fextralife, Hollow Knight Shade Cloak, Returnal Twinfinite, HLD Steam/Kotaku, RoR2 위키, DMC/Bayonetta Fandom. (전체 URL은 본문 각 섹션 참조)
