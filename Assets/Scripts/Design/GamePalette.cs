using UnityEngine;

namespace IGMain.Design
{
    public enum TileColorScheme { Flat, ValueMapped }

    public enum PaletteRole
    {
        // ── Surface ───────────────────────────────────────────
        Bg,             // 카메라/화면 배경
        BoardBg,        // 보드 배경 패널
        CellEmpty,      // 빈 셀
        Panel,          // 카드·chip_pill·podium_side
        Elevated,       // btn_circle·toggle_off·로고 밝은 칸
        ElevatedDim,    // 로고 어두운 칸
        TraySlot,       // tray_slot

        // ── Block (HDR) ───────────────────────────────────────
        BlockAdd,
        BlockSub,
        BlockMul,
        BlockDiv,
        BlockWild,


        // ── Accent 계열 ───────────────────────────────────────
        Accent,         // btn_play, logo_accent, CALC 텍스트 등
        BgGlow,         // 화면 전체 ambient glow 스프라이트 틴트
        AccentGlow,     // 집중 글로우 — logo_glow, word_calc_glow, btn_play_glow
        TitleTMPGlow,   // CALC 타이틀 Glow
        PlayGlow,       // PLAY halo — btn_play_glow, glow_radial(버튼 위)
        AccentAmbient,  // 배경 상단 glow_radial 틴트
        AccentLine,     // accent 계열 테두리
        AccentSoft,     // accent 소프트 배경 (아이콘 bg 등)

        SoundCircle,
        SoundGlow,
        SoundRing,

        // ── Danger 계열 ───────────────────────────────────────
        Danger,
        DangerLine,     // danger 계열 테두리
        DangerSoft,     // danger 아이콘 bg

        // ── Text / UI ────────────────────────────────────────
        TileText,
        Fg1,            // TETRIS, 점수, BEST 숫자
        Fg2,            // 일반 아이콘, BEST 라벨
        Fg3,            // 태그라인, 설정 라벨, toggle_knob_off
        PlayIcon,       // ▶ 아이콘
        Amber,          // 트로피 아이콘, ÷ 배경 기호

        // ── UI_Block (HDR) ───────────────────────────────────────
        UI_BlockAdd,
        UI_BlockSub,
        UI_BlockMul,
        UI_BlockDiv,
        UI_BlockWild,

        Accent3,        // 팔레트 스와치 그라데이션 끝

        Bg2,            // 배경 그라데이션 끝

        BackDrop,

    }

    [CreateAssetMenu(fileName = "GamePalette", menuName = "CalcTetris/Palette")]
    public class GamePalette : ScriptableObject
    {
        // ── Background ───────────────────────────────────────
        [Header("Background")]
        public Color bg = H("#070B14");
        public Color bg2 = H("#060A11");

        // ── Surfaces ─────────────────────────────────────────
        [Header("Surfaces")]
        public Color boardBg = H("#070B14");
        public Color cellEmpty = H("#0D1525");
        public Color panel = H("#131E30");
        public Color elevated = H("#131E30");
        public Color elevatedDim = HA("#131E30", 0.50f);   // 로고 어두운 칸
        public Color traySlot = H("#0F1828");

        // ── Blocks (HDR) ─────────────────────────────────────
        [Header("Blocks (HDR)")]
        [ColorUsage(true, true)] public Color blockAdd = H("#00C8E0");
        [ColorUsage(true, true)] public Color blockSub = H("#1A7FFF");
        [ColorUsage(true, true)] public Color blockMul = H("#6B82FF");
        [ColorUsage(true, true)] public Color blockDiv = H("#FFB700");
        [ColorUsage(true, true)] public Color blockWild = H("#00FFC2");


        // ── UI Blocks (HDR) ─────────────────────────────────────
        [Header("Blocks (HDR)")]
        [ColorUsage(true, true)] public Color uiblockAdd = H("#00C8E0");
        [ColorUsage(true, true)] public Color uiblockSub = H("#1A7FFF");
        [ColorUsage(true, true)] public Color uiblockMul = H("#6B82FF");
        [ColorUsage(true, true)] public Color uiblockDiv = H("#FFB700");
        [ColorUsage(true, true)] public Color uiblockWild = H("#00FFC2");

        // ── Accent ───────────────────────────────────────────
        [Header("Accent")]
        [ColorUsage(true, true)]
        public Color accent = H("#00E5FF");
        public Color accent3 = H("#0096C8");
        public Color bgGlow = H("#131E30");              // ambient glow 스프라이트 틴트
        public Color accentGlow = HA("#00E5FF", 0.45f);      // 집중 글로우
        public Color TitleTMPGlow = HA("#00E5FF", 0.15f);      // CALC 글로우
        public Color playGlow = HA("#00E5FF", 0.45f);      // PLAY halo
        public Color accentAmbient = HA("#00E5FF", 0.05f);      // 배경 상단 glow_radial
        public Color accentLine = HA("#00E5FF", 0.14f);      // 테두리
        public Color accentSoft = HA("#00E5FF", 0.10f);      // 소프트 배경

        [Header("Sound")]
        public Color soundCircle = HA("#00E5FF", 0.10f);      // 사운드 기본
        public Color soundGlow = HA("#00E5FF", 0.10f);      // 사운드 글로우
        public Color soundRing = HA("#00E5FF", 0.10f);      // 사운드 테두리

        // ── Danger ───────────────────────────────────────────
        [Header("Danger")]
        [ColorUsage(true, true)]
        public Color danger = H("#FF3B5C");
        public Color dangerLine = HA("#FF3B5C", 0.18f);         // 테두리
        public Color dangerSoft = HA("#FF3B5C", 0.10f);         // 소프트 배경

        // ── Text / UI ────────────────────────────────────────
        [Header("Text / UI")]
        public Color tileText = Color.white;
        public Color uiFg1 = H("#E0EEFF");
        public Color uiFg2 = H("#8BA8C8");
        public Color uiFg3 = H("#3D5A7A");
        public Color playIcon = H("#04222B");
        public Color amber = H("#FFB700");

        // ── Tile Visual ───────────────────────────────────────
        [Header("Tile Visual")]
        public Color tileHighlightColor = HA("#00E5FF", 0.14f);
        public Color tileFilledColor = new Color(1f, 1f, 1f, 0.82f);
        public Color tileGlowColor = H("#00E5FF");
        public Color BackDrop = H("#E0EEFF");

        public TileColorScheme colorScheme = TileColorScheme.Flat;

        // ── Brightness ───────────────────────────────────────
        [Header("Brightness")]
        [Range(0.45f, 1.8f)] public float globalBrightness = 1.0f;

        // ── Resolve ──────────────────────────────────────────
        public Color Resolve(PaletteRole role)
        {
            switch (role)
            {
                // Surface
                case PaletteRole.Bg: return bg;
                case PaletteRole.Bg2: return bg2;
                case PaletteRole.BoardBg: return boardBg;
                case PaletteRole.CellEmpty: return cellEmpty;
                case PaletteRole.Panel: return panel;
                case PaletteRole.Elevated: return Bright(elevated);
                case PaletteRole.ElevatedDim: return elevatedDim;
                case PaletteRole.TraySlot: return traySlot;
                // Block (HDR)
                case PaletteRole.BlockAdd: return Bright(blockAdd);
                case PaletteRole.BlockSub: return Bright(blockSub);
                case PaletteRole.BlockMul: return Bright(blockMul);
                case PaletteRole.BlockDiv: return Bright(blockDiv);
                case PaletteRole.BlockWild: return Bright(blockWild);
                // Accent
                case PaletteRole.Accent: return Bright(accent);
                case PaletteRole.Accent3: return Bright(accent3);
                case PaletteRole.BgGlow: return bgGlow;
                case PaletteRole.AccentGlow: return accentGlow;
                case PaletteRole.TitleTMPGlow: return TitleTMPGlow;
                case PaletteRole.PlayGlow: return playGlow;
                case PaletteRole.AccentAmbient: return accentAmbient;
                case PaletteRole.AccentLine: return accentLine;
                case PaletteRole.AccentSoft: return accentSoft;
                // Danger
                case PaletteRole.Danger: return Bright(danger);
                case PaletteRole.DangerLine: return dangerLine;
                case PaletteRole.DangerSoft: return dangerSoft;
                // Text / UI
                case PaletteRole.TileText: return tileText;
                case PaletteRole.Fg1: return uiFg1;
                case PaletteRole.Fg2: return uiFg2;
                case PaletteRole.Fg3: return uiFg3;
                case PaletteRole.PlayIcon: return playIcon;
                case PaletteRole.Amber: return amber;

                // UI Blocks (Symbols)
                case PaletteRole.UI_BlockAdd: return Bright(uiblockAdd);
                case PaletteRole.UI_BlockSub: return Bright(uiblockSub);
                case PaletteRole.UI_BlockMul: return Bright(uiblockMul);
                case PaletteRole.UI_BlockDiv: return Bright(uiblockDiv);
                case PaletteRole.UI_BlockWild: return Bright(uiblockWild);

                //Sound
                case PaletteRole.SoundCircle: return soundCircle;
                case PaletteRole.SoundGlow: return soundGlow;
                case PaletteRole.SoundRing: return soundRing;

                case PaletteRole.BackDrop: return BackDrop;

                default: return Color.white;
            }
        }

        // ── 배경 기호 헬퍼 (+−×÷) ────────────────────────────
        public Color SymbolColor(PaletteRole baseRole, float alpha = 0.07f)
        {
            Color c = Resolve(baseRole);
            return new Color(c.r, c.g, c.b, alpha);
        }

        // ── 내부 헬퍼 ────────────────────────────────────────
        private Color Bright(Color c)
            => new Color(c.r * globalBrightness, c.g * globalBrightness, c.b * globalBrightness, c.a);

        // ── 레거시 프로퍼티 (외부 코드 무수정 호환) ──────────────
        public Color bgBase => boardBg;
        public Color bgSurface => cellEmpty;
        public Color bgElevated => panel;
        public Color cyan => accent;
        public Color blue => blockSub;
        public Color violet => blockMul;
        public Color success => blockWild;
        public Color fg1 => tileText;
        public Color fg2 => uiFg2;
        public Color fg3 => uiFg3;
        public Color playIconColor => playIcon;
        public Color button_Play => accent;

        // ── 파서 헬퍼 ─────────────────────────────────────────
        private static Color H(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        private static Color HA(string hex, float alpha)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return new Color(c.r, c.g, c.b, alpha);
        }
    }
}