using UnityEngine;

namespace IGMain
{
    public enum ETileValue
    {
        Multiply = 42,
        Add      = 43,
        Subtract = 45,
        Divede   = 47, // 원본 오타 유지
        Zero     = 48,
        One      = 49,
        Two      = 50,
        Three    = 51,
        Four     = 52,
        Five     = 53,
        Six      = 54,
        Seven    = 55,
        Eight    = 56,
        Nine     = 57,
        Empty    = 0
    }

    /// <summary>
    /// 페이즈 기반 타일 값 생성기.
    ///
    /// - 개별 타일: GetValue() — 매 호출마다 현재 페이즈에 맞는 확률로 타일 값 생성
    /// - 블록 세트: GenerateSetValues(count) — 세트 전체 타일을 한 번에 생성하며
    ///   숫자(1-9)가 하나도 없으면 랜덤 위치에 강제로 숫자를 삽입한다
    /// </summary>
    public class TileValueGenerator
    {
        private readonly IPhaseDataProvider _dataProvider;
        private readonly TilePhaseProfile[] _profiles;

        private static readonly TilePhaseProfile[] s_defaultProfiles = TilePhaseProfile.BuildProfiles();

        public TileValueGenerator(IPhaseDataProvider dataProvider, TilePhaseProfile[] profiles = null)
        {
            _dataProvider = dataProvider;
            _profiles     = profiles ?? s_defaultProfiles;
        }

        // ── 개별 타일 생성 ────────────────────────────────────────────────────

        public string GetValue()
        {
            float[] probs = ResolveProbabilities();
            return SelectByProbability(probs);
        }

        // ── 블록 세트 단위 생성 (최소 1개 숫자 보정 포함) ─────────────────────

        /// <summary>
        /// 세트 내 타일 수를 받아 타일 값 배열을 생성한다.
        /// 확률 스냅샷은 세트 시작 시점 한 번만 계산하며,
        /// 숫자(1-9)가 하나도 없으면 랜덤 인덱스에 강제로 숫자를 삽입한다.
        /// </summary>
        public string[] GenerateSetValues(int count)
        {
            if (count <= 0) return System.Array.Empty<string>();

            float[] probs  = ResolveProbabilities();
            var     values = new string[count];
            bool    hasNumber = false;

            for (int i = 0; i < count; i++)
            {
                values[i] = SelectByProbability(probs);
                if (!hasNumber && IsNumber(values[i]))
                    hasNumber = true;
            }

            if (!hasNumber)
                values[Random.Range(0, count)] = ForceNumber(probs);

            return values;
        }

        // ── 유틸 (IGBlockController의 세트 보정에서도 사용 가능) ──────────────

        public bool IsNumber(string value) =>
            value is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9";

        /// <summary>
        /// 현재 확률 분포에서 숫자(1-9)만 선택해 반환한다.
        /// 0은 수식을 망칠 수 있어 최소 숫자 보장에서 제외한다.
        /// </summary>
        public string GetNumber()
        {
            float[] probs = ResolveProbabilities();
            return ForceNumber(probs);
        }

        // ── 내부 헬퍼 ─────────────────────────────────────────────────────────

        private float[] ResolveProbabilities() =>
            TileProbabilityResolver.Resolve(
                _dataProvider.TotalClearCount,
                _dataProvider.BoardOccupancyRatio,
                _profiles);

        private static string SelectByProbability(float[] probs)
        {
            float roll       = Random.value; // [0, 1)
            float cumulative = 0f;

            for (int i = 0; i < probs.Length; i++)
            {
                cumulative += probs[i];
                if (roll < cumulative)
                    return TileProbabilityResolver.TileValues[i];
            }

            // 부동소수점 오차 대비 fallback
            return TileProbabilityResolver.TileValues[probs.Length - 1];
        }

        // 확률 분포에서 숫자(1-9) 범위만 뽑는다
        private static string ForceNumber(float[] probs)
        {
            float total = 0f;
            for (int i = (int)TileIndex.N1; i <= (int)TileIndex.N9; i++)
                total += probs[i];

            if (total <= 0f) return "1"; // 극단적 fallback

            float roll       = Random.value * total;
            float cumulative = 0f;
            for (int i = (int)TileIndex.N1; i <= (int)TileIndex.N9; i++)
            {
                cumulative += probs[i];
                if (roll < cumulative)
                    return TileProbabilityResolver.TileValues[i];
            }

            return "1";
        }
    }
}
