using UnityEngine;

namespace IGMain.Design
{
    /// <summary>
    /// CalculationTetris Design System — Color Tokens
    /// 
    /// 모든 게임 색상의 단일 진실의 원천 (Single Source of Truth).
    /// 색을 바꿔야 할 때는 여기서만 바꾼다.
    /// 
    /// 출처: /Design/colors_and_type.css (Claude Design 결과물)
    /// </summary>
    public static class CTColors
    {
        // ── Backgrounds ─────────────────────────────────────
        // 게임 화면 베이스부터 위로 갈수록 밝아짐
        public static readonly Color BgBase     = Hex("#070B14"); // 가장 어두운 (Camera background)
        public static readonly Color BgSurface  = Hex("#0D1525"); // 패널 배경
        public static readonly Color BgElevated = Hex("#131E30"); // 떠있는 카드, 하이라이트 영역
        public static readonly Color BgOverlay  = Hex("#000000", 0.65f); // 모달 뒤 어둡게

        // ── Accents ─────────────────────────────────────────
        // 강조 색. 점수, 액티브 상태 등에 사용
        public static readonly Color AccentCyan   = Hex("#00E5FF"); // 메인 강조 (점수, 충돌 신호)
        public static readonly Color AccentBlue   = Hex("#0066FF"); // 보조 강조
        public static readonly Color AccentViolet = Hex("#4B6EFF"); // 보조 강조 2
        public static readonly Color AccentAmber  = Hex("#FFB700"); // BEST 점수, 황금색

        // ── Block Type Colors ────────
        // 연산자별 블록 색
        public static readonly Color BlockNumber = Hex("#E0EEFF"); // 숫자 (밝은 흰색, 차분함)
        public static readonly Color BlockAdd    = Hex("#00C8E0"); // + (시안)
        public static readonly Color BlockSub    = Hex("#1A7FFF"); // - (파랑)
        public static readonly Color BlockMul    = Hex("#6B82FF"); // × (보라)
        public static readonly Color BlockDiv    = Hex("#FFB700"); // ÷ (앰버)
        public static readonly Color BlockWild   = Hex("#00FFC2"); // 보너스 (민트)

        // ── State Colors ────────────────────────────────────
        // 시스템 상태 표현
        public static readonly Color Success = Hex("#00FFC2"); // 성공, 완성, 클리어
        public static readonly Color Danger  = Hex("#FF3B5C"); // 실패, 게임오버
        public static readonly Color Warning = Hex("#FFB700"); // 경고

        // ── Foreground (텍스트) ─────────────────────────────
        // 어두운 배경 위 밝은 텍스트
        public static readonly Color FgPrimary   = Hex("#E0EEFF"); // 주요 텍스트
        public static readonly Color FgSecondary = Hex("#8BA8C8"); // 보조 텍스트 (라벨)
        public static readonly Color FgMuted     = Hex("#3D5A7A"); // 약한 텍스트 (placeholder)
        public static readonly Color FgDisabled  = Hex("#1F3552"); // 비활성

        // ── Tile States ──────
        // IGBoardTileView.UpdateVisualColideState 등에서 사용
        public static readonly Color TileEmpty       = Color.white;       // 빈 타일 (원본 색)
        public static readonly Color TileFilled      = Hex("#1A7FFF");    // 배치된 타일
        public static readonly Color TileHighlight   = AccentCyan;        // 충돌/예상 위치
        public static readonly Color TilePreview     = Hex("#FFFFFF", 0.4f); // 라인 클리어 예고

        // ── Daylight Palette ────────────────────────────────
        // 밝은 배경(Surface=#FFFFFF) 위에서 쓰는 색상 토큰. 발광 없이 솔리드 색면으로 강조.
        public static readonly Color DayAccent  = Hex("#0088B0"); // 강조 (SCORE 라벨, 베이스 칩 BG)
        public static readonly Color DayFg1     = Hex("#1B2738"); // 주요 텍스트 (점수 숫자)
        public static readonly Color DayFg2     = Hex("#5A6D82"); // 보조 텍스트 (× = 기호)
        public static readonly Color DaySurface = Hex("#FFFFFF"); // 카드 배경
        public static readonly Color DayBorder  = Hex("#E2E8F1"); // 카드 테두리
        public static readonly Color DayAmber   = Hex("#D98A0A"); // BEST 점수, 결과 칩
        public static readonly Color DayViolet  = Hex("#6D45D0"); // ×2 배수 칩
        public static readonly Color DayDanger  = Hex("#D83B5A"); // ×3+ 배수 칩

        // ── Helper: Hex → Color 변환 ────────────────────────
        private static Color Hex(string hex, float alpha = 1f)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c))
            {
                c.a = alpha;
                return c;
            }
#if UNITY_EDITOR
            Debug.LogError($"CTColors: Invalid hex '{hex}'");
#endif
            return Color.magenta; // 디버그용 — 잘못된 색은 분홍색으로 두드러짐
        }
    }
}