# LostRuins Redesign

졸업 작품 **LostRuins**(Unity, Photon PUN 2, 3인 협동 액션)의 플레이어·멀티플레이 구조를 **C++ 권위 서버 + Unity 클라이언트**로 다시 설계한 프로토타입입니다. 원본에서 겪은 문제(일부 플레이어 2~3초 지연, 데미지·이펙트 불일치, 굼뜬 점프)를 코드와 측정으로 다시 풀었습니다.

> 범위: 궁수 1직업, 박스로 단순화한 맵, 몬스터 12마리. 원본 게임 전체를 옮긴 것은 아닙니다.

## 문서

| 문서 | 내용 |
|---|---|
| [docs/redesign.html](docs/redesign.html) | 재설계서. 무엇이 가장 크게 바뀌는지, 구조, 클래스 다이어그램, 전체 소스, 측정값, 8개월 로드맵 |
| [docs/guide.html](docs/guide.html) | 해설서. 서버 개념을 비유·애니메이션·시뮬레이션으로 설명, 재검증 기록, 코드 한 줄씩 해설 |
| [docs/REVIEW.md](docs/REVIEW.md) | 재검증에서 고친 17개 항목과 이유 |

HTML 문서는 내려받아 브라우저로 열면 됩니다(인터넷이 없으면 시스템 글꼴로 보입니다).

## 가장 크게 바뀌는 것

1. **규칙이 사는 곳.** 데미지, 명중, 치명타 난수, 몬스터 AI, 체력이 Unity 컴포넌트에서 서버의 순수 C++ 시뮬레이션(`World`)으로 옮겨 갑니다. 클라이언트는 입력, 예측, 표현만 합니다.
2. **이동 코드의 성격.** Rigidbody와 애니메이션 이벤트에 맡기던 이동이, C++과 C#에서 **비트 단위로 같은 결과**를 내는 함수 `Step()` 하나가 됩니다. 서버가 판정하면서도 내 캐릭터가 즉시 반응하는 방법입니다.
3. **클래스 구조.** 상속 4단(`PlayerState → Dealer → MultiDealer`)이 데이터(스킬 표)와 조합(예측기, 보간기, 뷰)으로 바뀌고, 멀티 전용 클래스는 0개가 됩니다.

## 폴더 구조

```
Redesign/
├─ server/                    C++20 게임 서버 (CMake)
│  ├─ src/shared/             서버·클라 공용 규칙: movement.h(Step), protocol.h(패킷), bitbuffer.h
│  ├─ src/net/                udp_socket.h(소켓), reliability.h(ACK 비트필드, 신뢰 이벤트)
│  ├─ src/game/               world.h/.cpp(게임 규칙), skills.h(스킬 표), geometry.h(판정)
│  ├─ src/server/             game_server.h/.cpp(30Hz 틱 루프, 스냅샷), main.cpp
│  ├─ tests/unit_tests.cpp    단위 테스트 20개 (CTest)
│  └─ tools/                  loadtest(봇 3명 측정), bench_tick(틱 비용), parity(C++↔C# 일치 검사)
├─ client-unity/              Unity 클라이언트 C#
│  ├─ Shared/                 Movement.cs, Protocol.cs (서버 shared/와 한 줄씩 대응)
│  ├─ Client/                 LocalPlayerPredictor, SnapshotInterpolator, NetClient (Unity 무관)
│  ├─ Unity/                  MonoBehaviour: GameClient, InputReader, ClientCamera, 뷰, 풀
│  └─ Tests/                  ParityTest.cs (mono), Stub/UnityStub.cs (CI 타입 검사 전용)
└─ docs/                      redesign.html, guide.html, REVIEW.md
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
../server/build/lr_parity gen inputs.txt states.txt && ../server/build/lr_parity snap snapshot.bin
mcs -out:ParityTest.exe ../client-unity/Shared/*.cs ../client-unity/Client/*.cs ../client-unity/Tests/ParityTest.cs
mono ParityTest.exe . && ../server/build/lr_parity input input.bin
```

Unity에서 쓸 때는 `client-unity/Shared`, `Client`, `Unity` 폴더를 `Assets/` 아래로 복사합니다(`Tests/`는 넣지 않습니다). Input System 패키지와 Animation Rigging 패키지가 필요합니다.

## 측정 결과

봇 3명이 실제 UDP(루프백)로 접속하고, 지연·지터·손실은 봇 쪽에서 흉내 냈습니다. 실제 인터넷·Unity 측정이 아닙니다. 입력 버퍼 1틱, 몬스터 12마리, 시나리오마다 20초.

| 시나리오 | 예측이 서버와 비트 일치 | 보정 | 서버 확인 p50 / p95 | 클라 1명 올림 / 내림 | 서버 틱 평균 |
|---|---|---|---|---|---|
| 지연 0 | 1,791 / 1,791 | 0 (버퍼 0이면 2) | 67 / 67 ms | 1.1 / 6.9 KB/s | 81 µs |
| 왕복 100ms · 손실 2% | 1,732 / 1,732 | 0 (버퍼 0이면 3) | 153 / 156 ms | 1.1 / 6.8 KB/s | 92 µs |
| 왕복 200ms · 손실 5% | 1,655 / 1,662 | 7 (버퍼 0이면 11) | 307 / 342 ms | 1.1 / 6.9 KB/s | 89 µs |

- 몬스터 200마리: 스냅샷 최대 1200B / 평균 1195B (1,200B 예산, 먼 엔티티부터 제외), 서버 틱 평균 220µs
- C++ ↔ C# 이동 일치: 무작위 입력 3,000틱(공중 1,643틱, 구르기 198틱 포함) 모두 비트 일치

시뮬레이션만의 틱 비용 (플레이어 3명, 9,000틱):

| 몬스터 | 평균 | p99 | 틱 예산(33,333µs) 대비 |
|---|---|---|---|
| 12 | 1.5 µs | 3.5 µs | 0.01% |
| 50 | 4.5 µs | 10.2 µs | 0.01% |
| 200 | 15.6 µs | 43.4 µs | 0.05% |
| 1,000 | 71.0 µs | 175.4 µs | 0.21% |

## 원본 문제와의 대응

| 원본 문제 | 재설계 |
|---|---|
| 매 프레임 RPC (조준 안 하는 궁수 60회/초) | RPC 없음. 30Hz 입력 1개 ↔ 30Hz 스냅샷 1개 |
| Photon 연결이 TCP | UDP + ACK 비트필드, 신뢰 이벤트는 확인될 때까지 재적재 |
| 화면마다 다른 판정 | 명중·데미지·난수 모두 서버 `World` |
| 애니메이션 이벤트로 0.446초 뒤 이륙 | `Step()`에서 입력 틱에 이륙, 코요테 타임·점프 버퍼 |
| Rigidbody 보간 꺼짐, 마우스에 deltaTime | 틱 사이 렌더 보간, 카메라 LateUpdate·델타 그대로 |
| 원격 캐릭터 Lerp | 서버 틱 기준 3틱 과거 스냅샷 보간 |
| bool 10개 + 매직 인덱스 | 틱 단위 타이머 + 스킬 데이터 표, 패시브 버그 수정 |
| 상속 복사, Multi 클래스 4개 | 조합 구조, 멀티 전용 클래스 0개 |
| Instantiate/Destroy, 매 프레임 문자열 | ObjectPool, TMP SetText, 패킷 버퍼 재사용 |
| 유료 에셋 커밋, CP949 혼재 | 외부 에셋 없음, `.editorconfig`로 UTF-8 고정 |

## 한계

- 궁수 1직업만 옮겼습니다. 전사 근접 판정, 창술사 실드, 힐러 버프는 같은 패턴으로 추가해야 합니다.
- 맵은 박스 4개, 몬스터는 직선 추적입니다. 메시 충돌(Jolt 등)과 길찾기(Recast/Detour)는 없습니다.
- Unity 스크립트는 실행하지 않았습니다. 렌더 보간, 풀링, IK 블렌드의 효과는 Unity에서 확인해야 합니다.
- 보안은 패킷·입력 검증까지입니다. 입장 토큰, 접속 빈도 제한, 암호화는 없습니다.
