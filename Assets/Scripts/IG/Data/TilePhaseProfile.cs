using System;

namespace IGMain
{
    // 배열 인덱스 → 타일 문자 매핑. TileProbabilityResolver.TileValues 순서와 반드시 일치해야 한다.
    public enum TileIndex
    {
        N0 = 0, N1, N2, N3, N4, N5, N6, N7, N8, N9,
        Add, Sub, Mul, Div, Empty,
        Count // 경계 sentinel (= 15)
    }

    /// <summary>
    /// 하나의 게임 페이즈에 대한 타일별 목표 가중치를 보유한다.
    /// 가중치는 정규화되지 않은 원시 값 — TileProbabilityResolver가 보간 후 정규화한다.
    /// </summary>
    public readonly struct TilePhaseProfile
    {
        public const int TileCount = (int)TileIndex.Count; // 15

        /// <summary>이 페이즈가 시작되는 누적 클리어 횟수 하한 (inclusive)</summary>
        public readonly int ClearThreshold;

        /// <summary>
        /// 타일별 목표 가중치 배열 (길이 == TileCount, TileIndex로 인덱싱).
        /// 배열 내용을 런타임에 변경하지 말 것.
        /// </summary>
        public readonly float[] Weights;

        public TilePhaseProfile(int clearThreshold, float[] weights)
        {
            if (weights == null || weights.Length != TileCount)
                throw new ArgumentException($"weights 배열 길이는 {TileCount}이어야 합니다.");
            ClearThreshold = clearThreshold;
            Weights = weights;
        }

        // ── 기본 페이즈 테이블 ────────────────────────────────────────────────
        //
        // 배열 인덱스 순서 (TileIndex enum과 일치):
        //  [0]  0   [1]  1   [2]  2   [3]  3   [4]  4
        //  [5]  5   [6]  6   [7]  7   [8]  8   [9]  9
        //  [10] +   [11] -   [12] *   [13] /   [14] spc
        //
        // TODO: 플레이 테스트 후 각 수치를 조정할 것.
        //       현재 값은 설계 의도를 반영한 초기 추정치.
        public static TilePhaseProfile[] BuildProfiles() => new[]
        {
            // ── Phase 0 : 학습기 (0 ~ 3 클리어) ─────────────────────────────
            // 숫자≈58%  연산자≈23%  빈칸≈19%
            //   - 소수(1-6) 위주, 0 없음, 7-9 소량
            //   - 연산자 : + 주력, - 소량, */÷ 없음
            //   - 빈칸 넉넉 → 이어붙이기 차단, 짧은 수식 유도
            new TilePhaseProfile(clearThreshold: 0, weights: new float[]
            {
            //  0     1     2     3     4     5     6     7     8     9
                0f,   9f,   9f,   8f,   8f,   7f,   7f,   3f,   2f,   2f,
            //  +     -     *     /   spc
               18f,   4f,   0f,   0f,  18f,
            }),

            // ── Phase 1 : 성장기 (4 ~ 9 클리어) ─────────────────────────────
            // 숫자≈55%  연산자≈33%  빈칸≈12%
            //   - 숫자(1-9) 고르게, 0 극소량
            //   - 연산자 : +/- 본격, * 등장, ÷ 소량
            //   - 빈칸 감소
            new TilePhaseProfile(clearThreshold: 4, weights: new float[]
            {
            //  0     1     2     3     4     5     6     7     8     9
                1f,   7f,   7f,   7f,   6f,   6f,   6f,   5f,   4f,   4f,
            //  +     -     *     /   spc
               15f,  11f,   5f,   1f,  12f,
            }),

            // ── Phase 2 : 긴장기 (10 ~ 19 클리어) ───────────────────────────
            // 숫자≈54%  연산자≈37%  빈칸≈8.5%
            //   - 0 등장 증가, 전체 숫자 고르게
            //   - 연산자 : */÷ 본격 등장
            //   - 빈칸 최소
            new TilePhaseProfile(clearThreshold: 10, weights: new float[]
            {
            //  0     1     2     3     4     5     6     7     8     9
                3f,   7f,   6f,   6f,   6f,   6f,   5f,   5f,   4f,   3f,
            //  +     -     *     /   spc
               12f,  10f,   8f,   5f,   8f,
            }),

            // ── Phase 3 : 생존기 (20+ 클리어) ────────────────────────────────
            // 숫자≈52%  연산자≈43%  빈칸≈4.5%
            //   - 0 빈도 높음, 고숫자(7-9) 감소
            //   - 연산자 : -/÷ 비율 최대 → 음수·나눗셈 위협
            //   - 빈칸 거의 없음
            new TilePhaseProfile(clearThreshold: 20, weights: new float[]
            {
            //  0     1     2     3     4     5     6     7     8     9
                8f,   6f,   6f,   5f,   5f,   5f,   4f,   3f,   2f,   2f,
            //  +     -     *     /   spc
               10f,  13f,   7f,   8f,   4f,
            }),
        };
    }
}
