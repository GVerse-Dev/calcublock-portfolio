using System.Collections.Generic;
using UnityEngine;

public class IGConfig
{
    // ── 보드 ─────────────────────────────────────────────────────────────────
    public static readonly int BOARD_COL = 9;
    public static readonly int BOARD_ROW = 9;

    // ── 화면 ─────────────────────────────────────────────────────────────────
    public static readonly int SCREEN_WIDTH = 720;
    public static readonly int SCREEN_HEIGHT = 1280;
    public static readonly int SCREEN_WIDTH_HALF = SCREEN_WIDTH / 2;
    public static readonly int SCREEN_HEIGHT_HALF = SCREEN_HEIGHT / 2;

    // ── 타일 ─────────────────────────────────────────────────────────────────
    public static readonly int TILE_WIDTH = 68;
    public static readonly int TILE_HEIGHT = 68;
    public static readonly int TILE_GAP = 3;
    public static readonly int TILE_WIDTH_HALF = TILE_WIDTH / 2;
    public static readonly int TILE_HEIGHT_HALF = TILE_HEIGHT / 2;

    // ── 게임오버 연출 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 게임오버가 확정된 뒤 결과 팝업과 전면 광고를 띄우기까지 기다리는 시간(초).
    ///
    /// 예전에는 판정 즉시 팝업과 광고가 떠서 **유저가 실패했다는 것을 인지할 틈이 없었다.**
    /// 이 시간 동안 보드 흔들림 연출만 보여주고, 입력은 상태 전이로 이미 차단된다.
    ///
    /// 팝업(HUDView)과 광고(IGGameController)가 **같은 값을 참조해야** 둘의 등장 시점이
    /// 어긋나지 않는다. 값을 바꿀 때는 여기만 고칠 것.
    /// </summary>
    public static readonly float GAME_OVER_PRESENTATION_DELAY = 1.2f;

    /// <summary>게임오버 시 보드 흔들림 지속 시간(초). 위 대기 시간보다 짧아야 한다.</summary>
    public static readonly float GAME_OVER_SHAKE_DURATION = 0.4f;

    /// <summary>
    /// 게임오버 흔들림 세기. **월드 단위다** (픽셀이 아니다).
    ///
    /// 이 파일의 TILE_WIDTH(68) 같은 값은 픽셀이고, 보드는 스프라이트 월드 좌표계다
    /// (타일 간격이 약 0.71 월드 단위 — IGGameController의 `0.68f * 3f` 참고).
    /// 처음에 픽셀 상수로 세기를 계산해 넘겼다가 단위가 섞였다. 여기 값은 타일 절반 정도다.
    /// </summary>
    public static readonly float GAME_OVER_SHAKE_STRENGTH = 0.35f;

    /// <summary>게임오버 시 보드 타일이 무채색으로 바뀌는 데 걸리는 시간(초).</summary>
    public static readonly float GAME_OVER_GRAY_DURATION = 0.3f;

    /// <summary>
    /// 게임오버 시 채워진 타일에 덮는 무채색. 알파는 팔레트의 채움 색을 따른다.
    /// 타일 스프라이트가 이 색으로 틴트되므로 값이 낮을수록 어둡게 죽은 느낌이 난다.
    /// </summary>
    public static readonly Color GAME_OVER_GRAY = new Color(0.58f, 0.60f, 0.63f, 1f);

    // ── 블록 타입 딕셔너리 ────────────────────────────────────────────────────
    public static readonly Dictionary<EBlockShapeType, int[,]> BlockTypes;

    static IGConfig()
    {
        BlockTypes = new Dictionary<EBlockShapeType, int[,]>
        {
            // ── 1타일 ────────────────────────────────────────────────────────
            { EBlockShapeType.Dot,      Dot     },

            // ── 2타일 ────────────────────────────────────────────────────────
            { EBlockShapeType.H2,       H2      },
            { EBlockShapeType.V2,       V2      },

            // ── 3타일 직선 ───────────────────────────────────────────────────
            { EBlockShapeType.H3,       H3      },
            { EBlockShapeType.V3,       V3      },

            // ── 3타일 작은 L (2×2 모서리 제거) ──────────────────────────────
            { EBlockShapeType.SL_RD,    SL_RD   },
            { EBlockShapeType.SL_LD,    SL_LD   },
            { EBlockShapeType.SL_RU,    SL_RU   },
            { EBlockShapeType.SL_LU,    SL_LU   },

            // ── 4타일 ────────────────────────────────────────────────────────
            { EBlockShapeType.Square2,  Square2 },
            { EBlockShapeType.H4,       H4      },
            { EBlockShapeType.V4,       V4      },

            // ── 4타일 L형 ────────────────────────────────────────────────────
            { EBlockShapeType.L_RD,     L_RD    },
            { EBlockShapeType.L_LD,     L_LD    },
            { EBlockShapeType.L_RU,     L_RU    },
            { EBlockShapeType.L_LU,     L_LU    },
            { EBlockShapeType.L_RR,     L_RR    },
            { EBlockShapeType.L_LL,     L_LL    },
            { EBlockShapeType.L_RR_U,   L_RR_U  },
            { EBlockShapeType.L_LL_U,   L_LL_U  },

            // ── 4타일 T형 ────────────────────────────────────────────────────
            { EBlockShapeType.T_D,      T_D     },
            { EBlockShapeType.T_U,      T_U     },
            { EBlockShapeType.T_R,      T_R     },
            { EBlockShapeType.T_L,      T_L     },

            // ── 4타일 S/Z형 ──────────────────────────────────────────────────
            { EBlockShapeType.S_H,      S_H     },
            { EBlockShapeType.Z_H,      Z_H     },
            { EBlockShapeType.S_V,      S_V     },
            { EBlockShapeType.Z_V,      Z_V     },

            // ── 5타일 십자 ───────────────────────────────────────────────────
            { EBlockShapeType.Plus,     Plus    },
        };
    }

    // ── 열거형 ────────────────────────────────────────────────────────────────
    public enum EBlockShapeType
    {
        // 1타일
        Dot,

        // 2타일
        H2, V2,

        // 3타일 직선
        H3, V3,

        // 3타일 작은 L (2×2 모서리 제거)
        SL_RD, SL_LD, SL_RU, SL_LU,

        // 4타일
        Square2,
        H4, V4,

        // 4타일 L형
        L_RD, L_LD, L_RU, L_LU,   // 세로 L (2열 3행)
        L_RR, L_LL, L_RR_U, L_LL_U, // 가로 L (3열 2행)

        // 4타일 T형
        T_D, T_U, T_R, T_L,

        // 4타일 S/Z형
        S_H, Z_H, S_V, Z_V,

        // 5타일 십자
        Plus,

        // 9타일
        Square3,
    }

    // ── 1타일 ─────────────────────────────────────────────────────────────────
    //  ■
    public static readonly int[,] Dot = { { 1 } };

    // ── 2타일 ─────────────────────────────────────────────────────────────────
    //  ■■
    public static readonly int[,] H2 = { { 1, 1 } };

    //  ■
    //  ■
    public static readonly int[,] V2 = { { 1 }, { 1 } };

    // ── 3타일 직선 ─────────────────────────────────────────────────────────────
    //  ■■■
    public static readonly int[,] H3 = { { 1, 1, 1 } };

    //  ■
    //  ■
    //  ■
    public static readonly int[,] V3 = { { 1 }, { 1 }, { 1 } };

    // ── 3타일 작은 L ────────────────────────────────────────────────────────────
    //  ■□      □■      ■■      ■■
    //  ■■      ■■      ■□      □■
    public static readonly int[,] SL_RD = { { 1, 0 }, { 1, 1 } };
    public static readonly int[,] SL_LD = { { 0, 1 }, { 1, 1 } };
    public static readonly int[,] SL_RU = { { 1, 1 }, { 1, 0 } };
    public static readonly int[,] SL_LU = { { 1, 1 }, { 0, 1 } };

    // ── 4타일 ─────────────────────────────────────────────────────────────────
    //  ■■
    //  ■■
    public static readonly int[,] Square2 = { { 1, 1 }, { 1, 1 } };

    //  ■■■■
    public static readonly int[,] H4 = { { 1, 1, 1, 1 } };

    //  ■
    //  ■
    //  ■
    //  ■
    public static readonly int[,] V4 = { { 1 }, { 1 }, { 1 }, { 1 } };

    // ── 4타일 L형 (세로, 2열 3행) ────────────────────────────────────────────
    //  ■□      □■      ■■      ■■
    //  ■□      □■      ■□      □■
    //  ■■      ■■      ■□      □■
    public static readonly int[,] L_RD = { { 1, 0 }, { 1, 0 }, { 1, 1 } };
    public static readonly int[,] L_LD = { { 0, 1 }, { 0, 1 }, { 1, 1 } };
    public static readonly int[,] L_RU = { { 1, 1 }, { 1, 0 }, { 1, 0 } };
    public static readonly int[,] L_LU = { { 1, 1 }, { 0, 1 }, { 0, 1 } };

    // ── 4타일 L형 (가로, 3열 2행) ────────────────────────────────────────────
    //  ■□□      □□■      ■■■      ■■■
    //  ■■■      ■■■      ■□□      □□■
    public static readonly int[,] L_RR = { { 1, 0, 0 }, { 1, 1, 1 } };
    public static readonly int[,] L_LL = { { 0, 0, 1 }, { 1, 1, 1 } };
    public static readonly int[,] L_RR_U = { { 1, 1, 1 }, { 1, 0, 0 } };
    public static readonly int[,] L_LL_U = { { 1, 1, 1 }, { 0, 0, 1 } };

    // ── 4타일 T형 ─────────────────────────────────────────────────────────────
    //  ■■■      □■□      □■      ■□
    //  □■□      ■■■      ■■      ■■
    //                   □■      ■□
    public static readonly int[,] T_D = { { 1, 1, 1 }, { 0, 1, 0 } };
    public static readonly int[,] T_U = { { 0, 1, 0 }, { 1, 1, 1 } };
    public static readonly int[,] T_R = { { 0, 1 }, { 1, 1 }, { 0, 1 } };
    public static readonly int[,] T_L = { { 1, 0 }, { 1, 1 }, { 1, 0 } };

    // ── 4타일 S/Z형 ───────────────────────────────────────────────────────────
    //  □■■      ■■□      ■□      □■
    //  ■■□      □■■      ■■      ■■
    //                   □■      ■□
    public static readonly int[,] S_H = { { 0, 1, 1 }, { 1, 1, 0 } };
    public static readonly int[,] Z_H = { { 1, 1, 0 }, { 0, 1, 1 } };
    public static readonly int[,] S_V = { { 1, 0 }, { 1, 1 }, { 0, 1 } };
    public static readonly int[,] Z_V = { { 0, 1 }, { 1, 1 }, { 1, 0 } };

    // ── 5타일 십자 ─────────────────────────────────────────────────────────────
    //  □■□
    //  ■■■
    //  □■□
    public static readonly int[,] Plus = { { 0, 1, 0 }, { 1, 1, 1 }, { 0, 1, 0 } };


}
