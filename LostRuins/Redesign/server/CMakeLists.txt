cmake_minimum_required(VERSION 3.20)
project(lostruins_server CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)   # clang-tidy 등 분석 도구용
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

option(LR_WERROR "경고를 오류로 취급 (CI에서 켬)" ON)
option(LR_SANITIZE "AddressSanitizer + UndefinedBehaviorSanitizer로 빌드" OFF)

if(MSVC)
  add_compile_options(/W4 /fp:strict /utf-8)   # strict: 클라와 같은 float 결과, utf-8: 한글 주석
  if(LR_WERROR)
    add_compile_options(/WX)
  endif()
else()
  # -ffp-contract=off: a*b+c를 FMA 한 번으로 합치지 않음 → C#과 비트 단위로 같은 결과
  add_compile_options(-Wall -Wextra -Wpedantic -Wshadow -ffp-contract=off)
  if(LR_WERROR)
    add_compile_options(-Werror)
  endif()
  if(LR_SANITIZE)
    add_compile_options(-fsanitize=address,undefined -fno-omit-frame-pointer -fno-sanitize-recover=all)
    add_link_options(-fsanitize=address,undefined)
  endif()
endif()

find_package(Threads REQUIRED)

# 게임 규칙: 소켓을 모르는 순수 라이브러리 → 서버, 테스트, 벤치마크가 함께 쓴다
add_library(lr_game STATIC src/game/world.cpp)
target_include_directories(lr_game PUBLIC src)

add_library(lr_net STATIC src/server/game_server.cpp)
target_link_libraries(lr_net PUBLIC lr_game Threads::Threads)
if(WIN32)
  target_link_libraries(lr_net PUBLIC ws2_32 winmm)
endif()

add_executable(lr_server src/server/main.cpp)
target_link_libraries(lr_server PRIVATE lr_net)

add_executable(lr_loadtest tools/loadtest.cpp)
target_link_libraries(lr_loadtest PRIVATE lr_net)

add_executable(lr_bench tools/bench_tick.cpp)
target_link_libraries(lr_bench PRIVATE lr_game)

add_executable(lr_parity tools/parity.cpp)
target_link_libraries(lr_parity PRIVATE lr_game)

add_executable(lr_unit_tests tests/unit_tests.cpp)
target_link_libraries(lr_unit_tests PRIVATE lr_game)

enable_testing()
add_test(NAME unit_tests COMMAND lr_unit_tests)
add_test(NAME loadtest_smoke COMMAND lr_loadtest 2)   # 서버 + 봇 3명을 2초씩 실제로 돌려 보는 연기 테스트
