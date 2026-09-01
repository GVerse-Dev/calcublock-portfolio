# 시뮬레이션 결과 데이터

`Assets/Tests/EditMode/LevelDesignSimulation.cs` 실행 결과입니다.
전략별 1,000판씩, Unity 렌더링 없이 EditMode에서 돌렸습니다.

| 파일 | 내용 |
|---|---|
| `*_game_summary.csv` | 판 단위 요약 — 1,000행 전체 |
| `*_turn_detail.sample.csv` | 턴 단위 상세 — **앞 2,000행 발췌** (원본 random 16,203행 / greedy 32,097행) |

턴 상세 전체는 저장소 용량만 차지하므로 발췌만 뒀습니다. 시뮬레이션을 직접 돌리면 전량
재생성됩니다.

## 스키마

**game_summary** — `game_id, strategy, total_turns, final_score, max_expression, min_expression,
negative_count, negative_rate, phase0_turns, phase1_turns, phase2_turns, phase3_turns, end_board_occupancy`

**turn_detail** — `game_id, turn, phase, clear_count, board_occupancy, expression_result,
combo_multiplier, num_tiles, op_tiles, blank_tiles`

분석 결과는 [docs/automated-qa.md](../../docs/automated-qa.md)에 정리했습니다.
