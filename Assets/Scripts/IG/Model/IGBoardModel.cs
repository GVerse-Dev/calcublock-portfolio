using System.Collections.Generic;
using UnityEngine;
using UniRx;
using System;

namespace IGMain
{
    public class IGBoardModel : IGObject
    {
        // 비주얼/View 용 원본 타일 참조 (SetCollide, GetGridIndex 등)
        private IGBoardTileModel[,] boardTiles;
        private Dictionary<int, List<IGBoardTileModel>> Rows;
        private Dictionary<int, List<IGBoardTileModel>> Cols;

        // 현재 하이라이트된 타일만 추적해 ClearAllBoardTilesCollide 비용 최소화
        private readonly List<IGBoardTileModel> _highlightedTiles = new List<IGBoardTileModel>();

        private Subject<IGBoardModel> OnBoardUpdated = new Subject<IGBoardModel>();
        public IObservable<IGBoardModel> OnBoardUpdatedObservable => OnBoardUpdated.AsObservable();

        // 순수 로직 위임 대상
        private BoardGrid _boardGrid;

        // ── 초기화 ───────────────────────────────────────────────────────────────

        public void Initialize(IGBoardTileModel[,] tiles)
        {
            IGLog.Verbose("IGBoard Initialize - Starting");

            if (Camera.main != null)
            {
                float aspectRatio = (float)Screen.width / Screen.height;
                float verticalSize = IGConfig.BOARD_COL * IGConfig.TILE_WIDTH * 0.5f;
                float horizontalSize = IGConfig.BOARD_ROW * IGConfig.TILE_HEIGHT * 0.5f / aspectRatio;
                Camera.main.orthographicSize = Mathf.Max(verticalSize, horizontalSize);
            }

            boardTiles = tiles;

            // IBoardTile[,] 배열을 만들어 BoardGrid에 넘긴다.
            var interfaceTiles = new IBoardTile[IGConfig.BOARD_COL, IGConfig.BOARD_ROW];
            for (int x = 0; x < IGConfig.BOARD_COL; x++)
                for (int y = 0; y < IGConfig.BOARD_ROW; y++)
                    interfaceTiles[x, y] = tiles[x, y];

            _boardGrid = new BoardGrid(interfaceTiles);
        }

        // ── 배치 판정 ────────────────────────────────────────────────────────────

        public bool IsTileOccupied(int x, int y) => _boardGrid.IsTileOccupied(x, y);

        public bool CanPlaceBlock(IGBlockModel block, Vector2Int boardPosition)
        {
            if (block == null) return false;
            return _boardGrid.CanPlaceBlock(block, boardPosition);
        }

        public bool PlaceBlock(IGBlockModel block, Vector2Int boardPosition)
        {
            if (block == null) return false;
            return _boardGrid.PlaceBlock(block, boardPosition);
        }

        // ── 라인 클리어 ──────────────────────────────────────────────────────────

        public bool IsLineFull(int index, bool isRow) => _boardGrid.IsLineFull(index, isRow);

        public bool IsSquareFull(int startX, int startY) => _boardGrid.IsSquareFull(startX, startY);

        public long ClearLine(int index, bool isRow) => _boardGrid.ClearLine(index, isRow);

        public long ClearSquare(int startX, int startY) => _boardGrid.ClearSquare(startX, startY);

        public void ClearAllBoardTiles() => _boardGrid.ClearAll();

        // ── View 전용 (BoardGrid에 포함되지 않음) ────────────────────────────────

        public void SetColideTilesState(IGBlockModel block, Vector2Int boardPosition, bool isCollide)
        {
            if (block == null) return;
            foreach (var tilePosition in block.GetRelativeTilePositions())
            {
                int boardX = boardPosition.x + tilePosition.x;
                int boardY = boardPosition.y + tilePosition.y;
                if (!IsTileOccupied(boardX, boardY))
                {
                    var tile = boardTiles[boardX, boardY];
                    tile.SetCollide(isCollide);
                    if (isCollide)
                        _highlightedTiles.Add(tile);
                }
            }
        }

        public void ClearAllBoardTilesCollide()
        {
            foreach (var tile in _highlightedTiles)
                tile.SetCollide(false);
            _highlightedTiles.Clear();
        }

        public bool IsFilledRow(int row)
        {
            if (Rows == null || !Rows.ContainsKey(row)) return false;
            return Rows[row].TrueForAll(t => t.IsPlaceBlock);
        }

        public bool IsFilledColumn(int col)
        {
            if (Cols == null || !Cols.ContainsKey(col)) return false;
            return Cols[col].TrueForAll(t => t.IsPlaceBlock);
        }

        public int GetGridIndex(Vector2Int gridPosition)
        {
            if (gridPosition.x < 0 || gridPosition.x >= IGConfig.BOARD_COL) return -1;
            if (gridPosition.y < 0 || gridPosition.y >= IGConfig.BOARD_ROW) return -1;
            return boardTiles[gridPosition.x, gridPosition.y].Index;
        }
    }
}
