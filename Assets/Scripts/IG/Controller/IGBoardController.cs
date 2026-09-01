using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using IGMain;

// =============================================================
// [IGBoardController]
//
// 보드의 상태 소유 + 게임 로직을 담당한다.
// 이전에는 IGBoardManager(상태)와 IGBoardController(로직)로 분리되어 있었으나,
// 두 클래스가 강하게 결합되어 있고 Manager 패턴이 씬 범위를 벗어나는 문제가 있어 통합했다.
//
// [초기화 / 재시작]
//   InitializeController() : 최초 1회. 보드 타일을 풀에서 생성한다.
//   Reset()                 : 재시작 시. 타일 상태만 초기화 (오브젝트 재생성 없음).
// =============================================================
public class IGBoardController : ControllerBase, IGMain.IPhaseDataProvider
{
    // ── 보드 상태 (이전 IGBoardManager 소유분) ───────────────────────────────

    private IGBoardModel _igBoardModel;
    private IGBoardView _igBoardView;
    private IGBoardTileModel[,] _igBoardTileModels;
    private IGBoardTileView[,] _igBoardTileViews;

    // ── 컨트롤러 의존성 ──────────────────────────────────────────────────────

    private IGScoreController _scoreController;

    // ── 통계 ─────────────────────────────────────────────────────────────────

    private int totalClearedLines = 0;
    private int totalClearedSquares = 0;

    // 게임오버 흔들림의 기준 위치. DOShakePosition 은 원위치로 돌아오지만,
    // 연출 도중 리셋·부활이 끼면 어긋난 자리에 멈출 수 있어 복원용으로 기억해 둔다.
    private Vector3 _boardOrigin;
    private bool _boardOriginCaptured;

    // ── 프로퍼티 ──────────────────────────────────────────────────────────────

    public IGBoardModel BoardModel => _igBoardModel;

    /// <summary>마지막 PlaceBlock 호출에서 클리어된 라인+스퀘어 수. 클리어 없으면 0.</summary>
    public int LastClearedCount { get; private set; }

    // ── ControllerBase 구현 ───────────────────────────────────────────────────

    /// <summary>최초 1회 초기화. 보드 타일을 풀에서 생성한다.</summary>
    public override void InitializeController()
    {
        if (ThemeManager.IsValidInstance())
            ThemeManager.Instance.OnThemeChanged += ApplyTheme;

        GenerateBoard();
    }

    public override void UpdateController() { }

    private void OnDestroy()
    {
        if (ThemeManager.IsValidInstance())
            ThemeManager.Instance.OnThemeChanged -= ApplyTheme;
    }

    // ── 외부 주입 ─────────────────────────────────────────────────────────────

    public void SetScoreController(IGScoreController scoreController)
    {
        _scoreController = scoreController;
    }

    // ── 재시작 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 재시작 시 호출. 타일 오브젝트는 그대로 유지하고 상태만 초기화한다.
    /// 풀 반환/재생성 비용 없이 빠르게 리셋된다.
    /// </summary>
    public void Reset()
    {
        // 게임오버 연출 잔재를 먼저 걷어낸다. 흔들림은 원위치로 돌아오지만
        // 도중에 리셋되면 어긋난 자리에 멈출 수 있고, 무채색은 다음 판까지 남는다.
        transform.DOKill();
        if (_boardOriginCaptured) transform.localPosition = _boardOrigin;

        if (_igBoardTileViews != null)
            foreach (var view in _igBoardTileViews)
                view?.CancelGameOverTint();

        _igBoardModel?.ClearAllBoardTiles();
        _igBoardModel?.ClearAllBoardTilesCollide();
        totalClearedLines = 0;
        totalClearedSquares = 0;
    }

    // ── 입력 핸들러 ───────────────────────────────────────────────────────────

    public void HandleBlockOnPointerDown(IGBlockModel _) { }

    public void HandleBlockOnPointerUp(IGBlockModel block, bool canPlace, Vector2Int gridPos)
    {
        if (canPlace)
            PlaceBlock(block, gridPos);
    }

    public void HandleBlockOnPointerDrag(bool canPlace, IGBlockModel selectedBlock, Vector2Int gridPos)
    {
        ClearAllHighlights();

        if (canPlace)
            HighlightCollisionTiles(selectedBlock, gridPos);
    }

    // ── 배치 쿼리 ────────────────────────────────────────────────────────────

    public bool CanPlaceBlockAtPosition(IGBlockModel block, Vector2Int boardPosition)
    {
        if (!EnsureBoardReady()) return false;
        return _igBoardModel.CanPlaceBlock(block, boardPosition);
    }

    public bool IsAnyBlockPlaceable(List<IGBlockModel> availableBlocks)
    {
        if (!EnsureBoardReady()) return true; // 안전 디폴트

        if (availableBlocks == null || availableBlocks.Count == 0)
            return true; // 블록이 없으면 곧 새로 생성될 예정 → 게임오버 아님

        foreach (var block in availableBlocks)
        {
            if (block == null) continue;

            for (int y = 0; y < IGConfig.BOARD_ROW; y++)
                for (int x = 0; x < IGConfig.BOARD_COL; x++)
                    if (_igBoardModel.CanPlaceBlock(block, new Vector2Int(x, y)))
                        return true;
        }

        return false;
    }

    // ── 하이라이트 ───────────────────────────────────────────────────────────

    public void HighlightCollisionTiles(IGBlockModel block, Vector2Int gridPos)
    {
        if (!EnsureBoardReady()) return;
        _igBoardModel.SetColideTilesState(block, gridPos, true);
    }

    public void ClearAllHighlights()
    {
        if (!EnsureBoardReady()) return;
        _igBoardModel.ClearAllBoardTilesCollide();
    }

    // ── 연출 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 게임오버 실패 연출. 보드를 짧게 흔든다.
    ///
    /// 위치만 흔들고 원래 자리로 돌아오므로 **남는 상태 변화가 없다.** 타일 색을 바꾸는
    /// 방식은 테마·팔레트가 색을 관리하고 있어(ApplyTheme / PaletteTint) 다음 판으로
    /// 잔상이 넘어갈 위험이 있어 택하지 않았다.
    ///
    /// `SetUpdate(true)` 로 timeScale 과 무관하게 돌린다 — 일시정지나 광고 표시 중
    /// timeScale 이 0이 되어도 연출이 멈춘 채 방치되지 않는다.
    /// </summary>
    public void PlayGameOverCue()
    {
        // **흔들 대상은 이 컨트롤러의 transform 이다.** 타일·그리드·보드 오브젝트가 모두
        // 여기 자식으로 붙는다(SetParent(transform)). 처음에 _igBoardModel.transform 을
        // 흔들었더니 배경 오브젝트만 움직여 화면상 거의 보이지 않았다.
        if (!_boardOriginCaptured)
        {
            _boardOrigin = transform.localPosition;
            _boardOriginCaptured = true;
        }

        transform.DOKill();
        transform.localPosition = _boardOrigin;
        transform
            .DOShakePosition(IGConfig.GAME_OVER_SHAKE_DURATION, IGConfig.GAME_OVER_SHAKE_STRENGTH,
                             vibrato: 22, randomness: 40f, snapping: false, fadeOut: true)
            .SetUpdate(true);

        // 채워진 타일을 무채색으로 죽인다. 빈 칸과 클리어 애니메이션 중인 타일은 건드리지 않는다.
        if (_igBoardTileViews == null) return;

        foreach (var view in _igBoardTileViews)
            view?.PlayGameOverTint(IGConfig.GAME_OVER_GRAY_DURATION, IGConfig.GAME_OVER_GRAY);
    }

    // ── 유틸 ─────────────────────────────────────────────────────────────────

    public int GetTotalClearedLines() => totalClearedLines;
    public int GetTotalClearedSquares() => totalClearedSquares;
    public int GetGridIndex(Vector2Int gridPosition) => _igBoardModel.GetGridIndex(gridPosition);

    // ── IPhaseDataProvider 구현 ───────────────────────────────────────────

    /// <summary>라인 + 스퀘어 클리어 총합 (단방향 증가)</summary>
    public int TotalClearCount => totalClearedLines + totalClearedSquares;

    /// <summary>현재 채워진 보드 칸 비율 (0..1). 보드 미생성 시 0 반환.</summary>
    public float BoardOccupancyRatio
    {
        get
        {
            if (_igBoardTileModels == null) return 0f;

            int occupied = 0;
            for (int y = 0; y < IGConfig.BOARD_ROW; y++)
                for (int x = 0; x < IGConfig.BOARD_COL; x++)
                    if (_igBoardTileModels[x, y] != null && _igBoardTileModels[x, y].IsPlaceBlock)
                        occupied++;

            return occupied / (float)(IGConfig.BOARD_COL * IGConfig.BOARD_ROW);
        }
    }

    // ── 내부 : 배치 ──────────────────────────────────────────────────────────

    public bool PlaceBlock(IGBlockModel block, Vector2Int boardPosition)
    {
        LastClearedCount = 0;
        if (!EnsureBoardReady() || block == null) return false;
        if (!_igBoardModel.CanPlaceBlock(block, boardPosition)) return false;

        bool isPlaced = _igBoardModel.PlaceBlock(block, boardPosition);
        if (isPlaced)
        {
            Telemetry.NotePlacement();
            CheckAndClearLines();
        }

        return isPlaced;
    }

    // ── 내부 : 라인 클리어 ────────────────────────────────────────────────────

    /// <summary>
    /// 블록 배치 후 완성된 행·열·3×3 스퀘어를 한 번에 처리한다.
    ///
    /// [2-Phase 방식]
    /// Phase 1. 검사만 수행 — 모든 행/열/스퀘어의 완성 여부를 먼저 판정하고 목록을 수집한다.
    /// Phase 2. 일괄 클리어 — 수집된 목록을 실제로 클리어하고 점수를 계산한다.
    ///
    /// 이렇게 분리하지 않으면 행을 먼저 클리어할 때 타일이 리셋되어,
    /// 같은 턴에 동시에 완성된 스퀘어가 IsSquareFull() 검사를 통과하지 못하는 버그가 발생한다.
    /// </summary>
    private void CheckAndClearLines()
    {
        // ── Phase 1: 클리어 전 전체 검사 ─────────────────────────────────────

        var fullRows = new System.Collections.Generic.List<int>();
        var fullCols = new System.Collections.Generic.List<int>();
        var fullSquares = new System.Collections.Generic.List<Vector2Int>();

        for (int y = 0; y < IGConfig.BOARD_ROW; y++)
            if (_igBoardModel.IsLineFull(y, isRow: true))
                fullRows.Add(y);

        for (int x = 0; x < IGConfig.BOARD_COL; x++)
            if (_igBoardModel.IsLineFull(x, isRow: false))
                fullCols.Add(x);

        for (int startY = 0; startY < IGConfig.BOARD_ROW; startY += 3)
            for (int startX = 0; startX < IGConfig.BOARD_COL; startX += 3)
                if (_igBoardModel.IsSquareFull(startX, startY))
                    fullSquares.Add(new Vector2Int(startX, startY));

        // ── Phase 1.5: 클리어 애니메이션 딜레이 예약 ────────────────────────────
        ScheduleClearAnimations(fullRows, fullCols, fullSquares);

        // ── Phase 2: 일괄 클리어 및 점수 계산 ───────────────────────────────

        long totalRawScore = 0;
        int clearedCount = 0;

        foreach (int y in fullRows)
        {
            totalRawScore += ClearLine(y, isRow: true);
            clearedCount++;
        }

        foreach (int x in fullCols)
        {
            totalRawScore += ClearLine(x, isRow: false);
            clearedCount++;
        }

        foreach (var sq in fullSquares)
        {
            totalRawScore += ClearSquare(sq.x, sq.y);
            clearedCount++;
        }

        bool didClear = clearedCount > 0;
        LastClearedCount = clearedCount;

        if (_scoreController != null)
            _scoreController.NotifyTurnResult(totalRawScore, didClear, clearedCount);
        else
            Debug.LogError("IGBoardController: ScoreController not set");

        if (didClear)
        {
            Telemetry.NoteClear();
            SaveManager.Instance.AddLinesCleared(clearedCount);

            if (AudioManager.IsValidInstance())
            {
                int combo = _scoreController?.GetComboCount() ?? 1;
                string clip = combo >= 3 ? "line_clear_x3" : combo == 2 ? "line_clear_x2" : "line_clear_x1";
                AudioManager.Instance.Play(clip);
            }
        }
    }

    private void ScheduleClearAnimations(
        System.Collections.Generic.List<int> fullRows,
        System.Collections.Generic.List<int> fullCols,
        System.Collections.Generic.List<Vector2Int> fullSquares)
    {
        const float step = 0.04f;

        // BoardGrid.ClearLine(y, isRow:true) → _tiles[y, i] for i=0..8 (열 y, 위→아래)
        foreach (int y in fullRows)
            for (int i = 0; i < IGConfig.BOARD_ROW; i++)
                _igBoardTileViews[y, i]?.ScheduleClear(i * step);

        // BoardGrid.ClearLine(x, isRow:false) → _tiles[i, x] for i=0..8 (행 x, 왼→오른)
        foreach (int x in fullCols)
            for (int i = 0; i < IGConfig.BOARD_COL; i++)
                _igBoardTileViews[i, x]?.ScheduleClear(i * step);

        // ClearSquare(sq.x, sq.y) → _tiles[x, y] for x/y in 3×3 range (행 우선)
        foreach (var sq in fullSquares)
            for (int dy = 0; dy < 3; dy++)
                for (int dx = 0; dx < 3; dx++)
                    _igBoardTileViews[sq.x + dx, sq.y + dy]?.ScheduleClear((dy * 3 + dx) * step);
    }

    private long ClearLine(int index, bool isRow)
    {
        long score = _igBoardModel.ClearLine(index, isRow);
        totalClearedLines++;
        return score;
    }

    private long ClearSquare(int startX, int startY)
    {
        long score = _igBoardModel.ClearSquare(startX, startY);
        totalClearedSquares++;
        return score;
    }

    // ── 내부 : 보드 생성 (이전 IGBoardManager.GenerateBoard) ─────────────────

    private void GenerateBoard()
    {
        _igBoardTileModels = new IGBoardTileModel[IGConfig.BOARD_COL, IGConfig.BOARD_ROW];
        _igBoardTileViews  = new IGBoardTileView [IGConfig.BOARD_COL, IGConfig.BOARD_ROW];

        for (int y = 0; y < IGConfig.BOARD_ROW; y++)
        {
            for (int x = 0; x < IGConfig.BOARD_COL; x++)
            {
                var tile = PoolManager.Instance.Pop<IGBoardTileModel>(EPoolType.BoardTile);
                if (tile == null)
                {
                    Debug.LogError($"IGBoardController: Failed to pop BoardTile at ({x},{y})");
                    return;
                }

                tile.Initialize();
                tile.name = $"Tile_{x}_{y}";
                tile.transform.SetParent(transform);

                var tileView = tile.GetComponent<IGBoardTileView>();
                tileView.SetPosition(x, y);
                tileView.Initialize();

                _igBoardTileModels[x, y] = tile;
                _igBoardTileViews [x, y] = tileView;
            }
        }

        _igBoardModel = PoolManager.Instance.Pop<IGBoardModel>(EPoolType.Board);
        if (_igBoardModel == null)
        {
            Debug.LogError("IGBoardController: Failed to pop IGBoardModel from pool");
            return;
        }

        _igBoardModel.transform.SetParent(transform);
        _igBoardModel.Initialize(_igBoardTileModels);
        _igBoardView = _igBoardModel.GetComponent<IGBoardView>();

        Debug.Log($"IGBoard: Created {IGConfig.BOARD_ROW * IGConfig.BOARD_COL} tiles");

        CreateGridLinesOverlay();
    }

    private void CreateGridLinesOverlay()
    {
        float unit = 100f;
        float tileW = IGConfig.TILE_WIDTH / unit;
        float gap = IGConfig.TILE_GAP / unit;
        float boardSize = (9 * tileW) + (8 * gap);

        GameObject gridRoot = new GameObject("GridLinesOverlay");
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.localPosition = Vector3.zero;

        var theme = ThemeManager.IsValidInstance() ? ThemeManager.Instance.CurrentTheme : null;
        Color lineColor = theme != null ? new Color(theme.cyan.r, theme.cyan.g, theme.cyan.b, 0.18f) : new Color(0f, 0.898f, 1.0f, 0.18f);

        // 3x3 Separators: between indices 2&3 and 5&6
        // Position of gap after index 2: 
        // startX + (2 * (tileW + gap)) + tileW + (gap/2)

        float start = -boardSize / 2f;
        float[] lineOffsets = {
            start + (3 * tileW) + (2.5f * gap), // gap after col 2
            start + (6 * tileW) + (5.5f * gap)  // gap after col 5
        };

        foreach (float offset in lineOffsets)
        {
            // Vertical
            var vLine = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vLine.name = "VLine";
            vLine.transform.SetParent(gridRoot.transform);
            vLine.transform.localPosition = new Vector3(offset, 0, 0);
            vLine.transform.localScale = new Vector3(0.01f, boardSize, 1);
            vLine.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = lineColor };
            Object.DestroyImmediate(vLine.GetComponent<MeshCollider>());

            // Horizontal
            var hLine = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hLine.name = "HLine";
            hLine.transform.SetParent(gridRoot.transform);
            hLine.transform.localPosition = new Vector3(0, -offset, 0);
            hLine.transform.localScale = new Vector3(boardSize, 0.01f, 1);
            hLine.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = lineColor };
            Object.DestroyImmediate(hLine.GetComponent<MeshCollider>());
        }
    }

    // ── 세션 저장/복원 ────────────────────────────────────────────────────────

    public IGMain.BoardTileSaveData[] GetSessionBoardData()
    {
        var data = new IGMain.BoardTileSaveData[IGConfig.BOARD_COL * IGConfig.BOARD_ROW];
        for (int x = 0; x < IGConfig.BOARD_COL; x++)
            for (int y = 0; y < IGConfig.BOARD_ROW; y++)
            {
                var tile = _igBoardTileModels[x, y];
                data[x * IGConfig.BOARD_ROW + y] = new IGMain.BoardTileSaveData
                {
                    value = tile != null ? tile.TileData.Value : ""
                };
            }
        return data;
    }

    public void RestoreSessionBoardData(IGMain.BoardTileSaveData[] data)
    {
        if (data == null || data.Length != IGConfig.BOARD_COL * IGConfig.BOARD_ROW) return;

        for (int x = 0; x < IGConfig.BOARD_COL; x++)
            for (int y = 0; y < IGConfig.BOARD_ROW; y++)
            {
                var tile = _igBoardTileModels[x, y];
                if (tile == null) continue;
                var saved = data[x * IGConfig.BOARD_ROW + y];
                // 판정 규칙은 IGMain.TileValueSanitizer에 한 곳으로 모아 두었다.
                // 블록 복원 경로(IGBlockController)도 같은 것을 쓴다 — 한쪽만 막으면
                // BoardGrid.PlaceBlock이 블록 타일 값을 보드로 그대로 복사해 우회된다.
                tile.SetTileData(new IGMain.TileData(IGMain.TileValueSanitizer.Sanitize(saved?.value)));
            }
    }

    // ── 내부 : 유틸 ──────────────────────────────────────────────────────────

    private bool EnsureBoardReady()
    {
        if (_igBoardModel != null) return true;
        Debug.LogError("IGBoardController: Board model is null");
        return false;
    }

    private void ApplyTheme(Theme theme) { }
}
