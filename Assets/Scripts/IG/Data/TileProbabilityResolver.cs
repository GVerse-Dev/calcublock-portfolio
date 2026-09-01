using UnityEngine;

namespace IGMain
{
    /// <summary>
    /// 페이즈 기반 타일 확률 계산기. 순수 정적 클래스 — 상태 없음, MonoBehaviour 아님.
    ///
    /// 입력  : (총 클리어 횟수, 보드 점유율, 페이즈 프로필 배열)
    /// 출력  : 각 타일별 확률 배열 (합계 == 1, 길이 == TilePhaseProfile.TileCount)
    ///
    /// 처리 순서:
    ///   1. 인접 페이즈 간 선형 보간 → 중간 가중치 배열 생성
    ///   2. 보드 점유율 ≥ 70% 시 위험 타일(0, -, /) 가중치 후처리 증폭
    ///   3. 전체 정규화 → 확률 배열 반환
    /// </summary>
    public static class TileProbabilityResolver
    {
        // TileIndex enum 순서와 반드시 일치. TileValueGenerator가 이 배열로 타일 값을 선택한다.
        public static readonly string[] TileValues =
            { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "+", "-", "×", "÷", " " };

        // 보드 점유율 높을 때 증폭할 '위험' 타일 인덱스 (0, -, /)
        private static readonly int[] RiskyIndices =
        {
            (int)TileIndex.N0,
            (int)TileIndex.Sub,
            (int)TileIndex.Div,
        };

        private const float OccupancyThreshold = 0.70f; // 70% 이상부터 증폭 시작
        private const float OccupancyAmplifyMax = 1.30f; // 최대 1.3× 증폭

        /// <summary>
        /// 정규화된 타일 확률 배열을 반환한다.
        /// </summary>
        /// <param name="clearCount">누적 클리어 횟수 (라인 + 스퀘어 총합)</param>
        /// <param name="occupancyRatio">보드 점유율 (0..1)</param>
        /// <param name="profiles">페이즈 프로필 배열 (ClearThreshold 오름차순 정렬 필수)</param>
        public static float[] Resolve(int clearCount, float occupancyRatio, TilePhaseProfile[] profiles)
        {
            int n = TilePhaseProfile.TileCount;
            float[] weights = new float[n];

            InterpolateWeights(clearCount, profiles, weights);
            ApplyOccupancyAmplification(occupancyRatio, weights);
            Normalize(weights);

            return weights;
        }

        // ── 1단계: 페이즈 간 선형 보간 ──────────────────────────────────────
        //
        // clearCount가 두 페이즈 사이에 있으면 Lerp로 부드럽게 전환한다.
        // 급격한 확률 변동을 방지하기 위해 보간 구간은 각 페이즈의 ClearThreshold 사이 전체 범위.
        private static void InterpolateWeights(int clearCount, TilePhaseProfile[] profiles, float[] result)
        {
            int n = TilePhaseProfile.TileCount;
            int last = profiles.Length - 1;

            // 마지막 페이즈 이상이면 마지막 가중치를 그대로 사용
            if (clearCount >= profiles[last].ClearThreshold)
            {
                System.Array.Copy(profiles[last].Weights, result, n);
                return;
            }

            for (int p = 0; p < last; p++)
            {
                TilePhaseProfile from = profiles[p];
                TilePhaseProfile to = profiles[p + 1];

                if (clearCount < to.ClearThreshold)
                {
                    int span = to.ClearThreshold - from.ClearThreshold;
                    float t = Mathf.Clamp01((float)(clearCount - from.ClearThreshold) / span);

                    for (int i = 0; i < n; i++)
                        result[i] = Mathf.Lerp(from.Weights[i], to.Weights[i], t);
                    return;
                }
            }

            // fallback: 첫 페이즈 (정상적으로는 도달하지 않음)
            System.Array.Copy(profiles[0].Weights, result, n);
        }

        // ── 2단계: 보드 점유율 후처리 보정 ──────────────────────────────────
        //
        // 점유율이 OccupancyThreshold(70%) 이상이면 위험 타일의 가중치를
        // 초과분에 비례해 최대 OccupancyAmplifyMax(1.3×)까지 선형 증폭한다.
        private static void ApplyOccupancyAmplification(float occupancyRatio, float[] weights)
        {
            if (occupancyRatio < OccupancyThreshold) return;

            float excess = (occupancyRatio - OccupancyThreshold) / (1f - OccupancyThreshold);
            float amplify = Mathf.Lerp(1f, OccupancyAmplifyMax, excess);

            foreach (int idx in RiskyIndices)
                weights[idx] *= amplify;
        }

        // ── 3단계: 정규화 ────────────────────────────────────────────────────
        //
        // 음수 가중치를 0으로 클램프한 후 합계로 나눠 확률로 변환.
        // 모든 가중치가 0이면 균등 분포를 반환한다 (안전 fallback).
        private static void Normalize(float[] weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = Mathf.Max(0f, weights[i]);
                total += weights[i];
            }

            if (total <= 0f)
            {
                float uniform = 1f / weights.Length;
                for (int i = 0; i < weights.Length; i++)
                    weights[i] = uniform;
                return;
            }

            for (int i = 0; i < weights.Length; i++)
                weights[i] /= total;
        }
    }
}
