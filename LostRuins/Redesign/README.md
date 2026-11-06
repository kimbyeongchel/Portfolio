# LostRuins Redesign

졸업 작품 **LostRuins**(Unity, Photon PUN 2, 3인 협동 액션)의 플레이어·멀티플레이 구조를 **C++ 권위 서버 + Unity 클라이언트**로 다시 설계한 프로토타입입니다. 원본에서 겪은 문제(일부 플레이어 2~3초 지연, 데미지·이펙트 불일치, 굼뜬 점프)를 코드와 측정으로 다시 풀었습니다.

> 범위: 전사·궁수·창술사·힐러 4직업, 박스로 단순화한 맵, 몬스터 12마리. 원본 게임 전체를 옮긴 것은 아닙니다.

## 문서

**[문서 목차 바로 열기](https://kimbyeongchel.github.io/Portfolio/LostRuins/Redesign/docs/)** · 아래 링크를 누르면 브라우저에서 바로 열립니다.

| 문서 | 내용 |
|---|---|
| [1편 기술 포스트모템](https://kimbyeongchel.github.io/Portfolio/LostRuins/Redesign/docs/postmortem.html) | 원본 진단. 2~3초 지연, 굼뜬 점프, 판정 불일치 등 문제 11가지의 증상·근거·원인·해결 |
| [2편 재설계서](https://kimbyeongchel.github.io/Portfolio/LostRuins/Redesign/docs/redesign.html) | 무엇이 가장 크게 바뀌는지, 구조, 클래스 다이어그램, 전체 소스, 측정값, 8개월 로드맵 |
| [3편 해설서](https://kimbyeongchel.github.io/Portfolio/LostRuins/Redesign/docs/guide.html) | 서버 개념을 비유·애니메이션·시뮬레이션으로 설명, 재검증 기록, 코드 한 줄씩 해설 |
| [4편 직업 재설계](https://kimbyeongchel.github.io/Portfolio/LostRuins/Redesign/docs/classes.html) | 네 직업의 원본 동작·버그, 재설계 표와 코드, 스킬 타임라인, 테스트, 고민 노트 |
| [docs/REVIEW.md](docs/REVIEW.md) | 재검증에서 고친 21개 항목과 이유 |

읽는 순서: 처음이라면 3편(개념) → 2편(설계) → 4편(직업 코드), 시간이 없다면 4편부터. 1편은 "원래 무엇이 문제였나"를 볼 때 읽습니다.

2·3편 문서는 직업을 추가하기 전(궁수만 있던) 코드와 수치를 기준으로 합니다. 뼈대를 이해하기 쉬운 작은 버전이라 그대로 두었습니다. 직업 추가로 바뀐 패킷 크기와 측정값은 4편(classes.html)과 아래 표가 최신입니다.

HTML 원본은 [docs/](docs/) 폴더에 있습니다. GitHub에서 파일을 직접 열면 소스가 보이므로, 위 링크(GitHub Pages)로 여세요.

## 가장 크게 바뀌는 것

1. **규칙이 사는 곳.** 데미지, 명중, 치명타 난수, 몬스터 AI, 체력이 Unity 컴포넌트에서 서버의 순수 C++ 시뮬레이션(`World`)으로 옮겨 갑니다. 클라이언트는 입력, 예측, 표현만 합니다.
2. **이동 코드의 성격.** Rigidbody와 애니메이션 이벤트에 맡기던 이동이, C++과 C#에서 **비트 단위로 같은 결과**를 내는 함수 `Step()` 하나가 됩니다. 서버가 판정하면서도 내 캐릭터가 즉시 반응하는 방법입니다.
3. **클래스 구조.** 상속 3단(`PlayerState → Warrior/Dealer/Lancer/Healer → Multi*`)이 직업 표(`abilities.h`의 타이밍, `classes.h`의 효과)와 공용 엔진으로 바뀌고, 멀티 전용 클래스는 0개가 됩니다. 직업 하나를 추가하면 표 두 곳에 줄이 늘어납니다.
4. **판정 시점.** 원본은 애니메이션 진행률을 매 프레임 기다려서 fps에 따라 판정 시점이 흔들렸습니다. 재설계는 선딜·판정·후딜을 틱 단위 프레임 데이터로 정하고, 애니메이션 재생 속도를 그 틱에 맞춥니다.

## 폴더 구조

```
Redesign/
├─ server/                    C++20 게임 서버 (CMake)
│  ├─ src/shared/             서버·클라 공용 규칙: movement.h(Step), abilities.h(스킬 타이밍·직업 타이밍 표), protocol.h(패킷), bitbuffer.h
│  ├─ src/net/                udp_socket.h(소켓), reliability.h(ACK 비트필드, 신뢰 이벤트)
│  ├─ src/game/               world.h/.cpp(게임 규칙·효과 적용), classes.h(직업 효과 표), geometry.h(판정)
│  ├─ src/server/             game_server.h/.cpp(30Hz 틱 루프, 스냅샷), main.cpp
│  ├─ tests/unit_tests.cpp    단위 테스트 37개 (CTest)
│  └─ tools/                  loadtest(봇 3명 측정), bench_tick(틱 비용), parity(C++↔C# 일치 검사)
├─ client-unity/              Unity 클라이언트 C#
│  ├─ Shared/                 Movement.cs, Abilities.cs, Protocol.cs (서버 shared/와 한 줄씩 대응)
│  ├─ Client/                 LocalPlayerPredictor, SnapshotInterpolator, EventScheduler, NetClient (Unity 무관)
│  ├─ Unity/                  MonoBehaviour: GameClient, InputReader, ClientCamera, 뷰, 풀, ClassVisuals(연출 표), SkillBarView
│  └─ Tests/                  ParityTest.cs (mono), Stub/UnityStub.cs (CI 타입 검사 전용)
└─ docs/                      index.html(목차), postmortem.html(1편), redesign.html(2편), guide.html(3편), classes.html(4편), REVIEW.md
```

## 빌드 · 실행 · 테스트

```bash
cmake -S server -B server/build -DCMAKE_BUILD_TYPE=Release
cmake --build server/build -j
ctest --test-dir server/build --output-on-failure      # 단위 테스트 + 서버·봇 연기 테스트
./server/build/lr_server 27015 12 1                     # 포트, 몬스터 수, 입력 버퍼(틱)
./server/build/lr_loadtest 20                           # 시나리오 7개 × 20초 측정 (JSON 출력)
./server/build/lr_bench                                 # 시뮬레이션 틱 비용
```

메모리 오류·미정의 동작 검사 빌드:

```bash
cmake -S server -B server/build-asan -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLR_SANITIZE=ON
cmake --build server/build-asan -j && ctest --test-dir server/build-asan --output-on-failure
```

C++ ↔ C# 일치 검사 (mono 필요):

```bash
mkdir -p parity && cd parity
../server/build/lr_parity gen inputs.txt states.txt && ../server/build/lr_parity snap snapshot.bin accept.bin
mcs -out:ParityTest.exe ../client-unity/Shared/*.cs ../client-unity/Client/*.cs ../client-unity/Tests/ParityTest.cs
mono ParityTest.exe . && ../server/build/lr_parity input input.bin connect.bin
```

Unity에서 쓸 때는 `client-unity/Shared`, `Client`, `Unity` 폴더를 `Assets/` 아래로 복사합니다(`Tests/`는 넣지 않습니다). Input System 패키지와 Animation Rigging 패키지가 필요합니다. 직업마다 `ClassVisuals` 에셋(애니메이션 트리거, 클립 길이, 타격 지점)을 만들고, 애니메이터에 `skillSpeed`(스킬 상태의 Speed Multiplier)와 `charging` 파라미터를 추가합니다.

## 측정 결과

봇 3명이 실제 UDP(루프백)로 접속하고, 지연·지터·손실은 봇 쪽에서 흉내 냈습니다. 실제 인터넷·Unity 측정이 아닙니다. 입력 버퍼 1틱, 몬스터 12마리, 시나리오마다 20초. 봇의 직업 조합은 시나리오마다 바꿨고, 봇은 동작이 끝나면 다음 스킬을 씁니다(충전 스킬은 누르고 있다가 뗌).

| 시나리오 | 직업 | 예측이 서버와 비트 일치 (이동 + 스킬) | 보정 | 서버 확인 p50 / p95 | 클라 1명 올림 / 내림 | 서버 틱 평균 |
|---|---|---|---|---|---|---|
| 지연 0 | 힐러·전사·궁수 | 1,794 / 1,794 | 0 (버퍼 0이면 3) | 34 / 34 ms | 1.1 / 7.0 KB/s | 56 µs |
| 왕복 100ms · 손실 2% | 전사·궁수·창술사 | 1,733 / 1,733 | 0 (버퍼 0이면 5) | 153 / 156 ms | 1.1 / 6.6 KB/s | 57 µs |
| 왕복 200ms · 손실 5% | 궁수·창술사·힐러 | 1,654 / 1,662 | 8 (버퍼 0이면 8) | 335 / 343 ms | 1.0 / 7.0 KB/s | 59 µs |

- 몬스터 200마리: 스냅샷 최대 1200B / 평균 1193B (1,200B 예산, 먼 엔티티부터 제외), 서버 틱 평균 164µs, 부활 11번 동안 보정 0
- C++ ↔ C# 일치: 직업 4개 × 무작위 입력 3,000틱(스킬 사용 5,699틱, 충전 877틱, 콤보 2·3타 295틱 포함), 이동 14 + 스킬 15 필드 모두 비트 일치

시뮬레이션만의 틱 비용 (플레이어 3명이 스킬을 계속 사용, 9,000틱):

| 몬스터 | 평균 | p99 | 틱 예산(33,333µs) 대비 |
|---|---|---|---|
| 12 | 0.8 µs | 1.5 µs | 0.003% |
| 50 | 2.3 µs | 4.0 µs | 0.007% |
| 200 | 5.3 µs | 15.1 µs | 0.016% |
| 1,000 | 25.2 µs | 61.2 µs | 0.075% |

## 원본 문제와의 대응

| 원본 문제 | 재설계 |
|---|---|
| 매 프레임 RPC (조준 안 하는 궁수 60회/초) | RPC 없음. 30Hz 입력 1개 ↔ 30Hz 스냅샷 1개 |
| Photon 연결이 TCP | UDP + ACK 비트필드, 신뢰 이벤트는 확인될 때까지 재적재 |
| 화면마다 다른 판정 | 명중·데미지·난수 모두 서버 `World` |
| 애니메이션 이벤트로 0.446초 뒤 이륙 | `Step()`에서 입력 틱에 이륙, 코요테 타임·점프 버퍼 |
| Rigidbody 보간 꺼짐, 마우스에 deltaTime | 틱 사이 렌더 보간, 카메라 LateUpdate·델타 그대로 |
| 원격 캐릭터 Lerp | 서버 틱 기준 3틱 과거 스냅샷 보간 |
| bool 10개 + 매직 인덱스 | 상태 하나(`AbilityState`) + 틱 단위 타이머, 패시브 버그 수정 |
| 상속 복사, Multi 클래스 4개 | 직업 표 + 공용 엔진, 멀티 전용 클래스 0개 |
| 애니메이션 진행률을 기다리는 판정 (fps 의존) | 틱 단위 프레임 데이터(선딜·판정·후딜·캔슬) + 애니메이션 리타이밍 |
| 버프를 8초 뒤 "빼기"로 복원, 같은 직업 수치가 프리팹마다 다름 | 남은 시간만 저장하고 수치는 매번 계산, 수치는 표 한 곳 |
| 데미지 숫자가 보이는 모습보다 먼저 뜸 | 이벤트를 보간 시각에 맞춰 표시 (내 공격 결과는 즉시) |
| Instantiate/Destroy, 매 프레임 문자열 | ObjectPool, TMP SetText, 패킷 버퍼 재사용 |
| 유료 에셋 커밋, CP949 혼재 | 외부 에셋 없음, `.editorconfig`로 UTF-8 고정 |

## 한계

- 프레임 값은 원본 에셋의 비율을 "1초 클립"으로 가정해 틱으로 바꾼 설계값입니다. 실제 클립 길이로 한 번 보정해야 합니다. 궁수는 원본에 타이밍 에셋이 없어 새로 정했습니다.
- 기획 확인이 필요한 값(창술사 강화 공격 60 < 일반 65, 전사 패시브 0.07과 0, 번개검 대상 선택, 화상·저주 중첩 방식)은 `classes.h`에 `[원본 확인 필요]`·`[설계]`로 표시했습니다.
- 맵은 박스 4개, 몬스터는 직선 추적입니다. 메시 충돌(Jolt 등)과 길찾기(Recast/Detour)는 없습니다.
- Unity 스크립트는 실행하지 않았습니다. 렌더 보간, 풀링, IK 블렌드, 리타이밍의 효과는 Unity에서 확인해야 합니다.
- 컷신 동기화(서버 틱 기준 시작 이벤트)와 발 IK는 설계만 했습니다.
- 보안은 패킷·입력 검증까지입니다. 입장 토큰, 접속 빈도 제한, 암호화는 없습니다.
