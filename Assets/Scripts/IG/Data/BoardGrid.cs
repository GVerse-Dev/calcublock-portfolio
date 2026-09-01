using System.Collections.Generic;
using UnityEngine;

namespace IGMain
{
    /// <summary>
    /// 9×9 보드의 배치/클리어 판정 순수 C# 구현.
    /// IGBoardModel 은 이 클래스에 위임하고, 시뮬레이터도 직접 이 클래스를 사용한다.
    /// </summary>
    public class BoardGrid
    {
        private readonly IBoardTile[,] _tiles;
        private readonly int _cols;
        private readonly int _rows;

        public BoardGrid(IBoardTile[,] tiles)
        {
            _tiles = tiles;
            _cols  = tiles.GetLength(0);
            _rows  = tiles.GetLength(1);
        }

        // ── 프로퍼티 ─────────────────────────────────────────────────────────────

        public float OccupancyRatio
        {
            get
            {
                int occupied = 0;
                for (int x = 0; x < _cols; x++)
                    for (int y = 0; y < _rows; y++)
                        if (_tiles[x, y].IsPlaceBlock) occupied++;
                return occupied / (float)(_cols * _rows);
            }
        }

        // ── 배치 판정 ────────────────────────────────────────────────────────────

        public bool IsTileOccupied(int x, int y)
        {
            if (x < 0 || x >= _cols || y < 0 || y >= _rows) return true;
            return _tiles[x, y].IsPlaceBlock;
        }

        public bool CanPlaceBlock(IBlockData block, Vector2Int pos)
        {
            var positions = block.GetRelativeTilePositions();
            if (positions == null || positions.Count == 0) return false;
            foreach (var o in positions)
                if (IsTileOccupied(pos.x + o.x, pos.y + o.y)) return false;
            return true;
        }

        public bool PlaceBlock(IBlockData block, Vector2Int pos)
        {
            if (!CanPlaceBlock(block, pos)) return false;
            foreach (var o in block.GetRelativeTilePositions())
                _tiles[pos.x + o.x, pos.y + o.y].SetTileData(block.GetTileData(o.x, o.y));
            return true;
        }

        public bool IsAnyBlockPlaceable(List<IBlockData> blocks)
        {
            foreach (var block in blocks)
                for (int y = 0; y < _rows; y++)
                    for (int x = 0; x < _cols; x++)
                        if (CanPlaceBlock(block, new Vector2Int(x, y))) return true;
            return false;
        }

        // ── 라인 클리어 판정 ─────────────────────────────────────────────────────
        // IGBoardModel과 동일한 인덱싱 규칙 유지 (boardTiles[index, i] when isRow=true)

        public bool IsLineFull(int index, bool isRow)
        {
            int len = isRow ? _rows : _cols;
            for (int i = 0; i < len; i++)
            {
                int tx = isRow ? index : i;
                int ty = isRow ? i     : index;
                if (!_tiles[tx, ty].IsPlaceBlock) return false;
            }
            return true;
        }

        public bool IsSquareFull(int startX, int startY)
        {
            for (int y = startY; y < startY + 3; y++)
                for (int x = startX; x < startX + 3; x++)
                    if (!_tiles[x, y].IsPlaceBlock) return false;
            return true;
        }

        /// <summary>
        /// 블록 배치 후 완성된 모든 행·열·스퀘어를 판정(2-Phase)하고 클리어 결과를 반환한다.
        /// IGBoardController.CheckAndClearLines 와 동일한 로직.
        /// </summary>
        public (long rawScore, int clearedCount) CheckAndClearLines()
        {
            var fullRows    = new List<int>();
            var fullCols    = new List<int>();
            var fullSquares = new List<Vector2Int>();

            for (int y = 0; y < _rows; y++)
                if (IsLineFull(y, isRow: true))  fullRows.Add(y);
            for (int x = 0; x < _cols; x++)
                if (IsLineFull(x, isRow: false)) fullCols.Add(x);
            for (int sy = 0; sy < _rows; sy += 3)
                for (int sx = 0; sx < _cols; sx += 3)
                    if (IsSquareFull(sx, sy)) fullSquares.Add(new Vector2Int(sx, sy));

            long totalScore = 0;
            int  cleared    = 0;

            foreach (int y  in fullRows)    { totalScore += ClearLine(y,  isRow: true);  cleared++; }
            foreach (int x  in fullCols)    { totalScore += ClearLine(x,  isRow: false); cleared++; }
            foreach (var sq in fullSquares) { totalScore += ClearSquare(sq.x, sq.y);     cleared++; }

            return (totalScore, cleared);
        }

        public long ClearLine(int index, bool isRow)
        {
            string value = string.Empty;
            int    len   = isRow ? _rows : _cols;
            for (int i = 0; i < len; i++)
            {
                int tx = isRow ? index : i;
                int ty = isRow ? i     : index;
                value += _tiles[tx, ty].GetTileValue();
                _tiles[tx, ty].ResetTile();
            }
            return ExpressionEvaluator.Evaluate(value);
        }

        public long ClearSquare(int startX, int startY)
        {
            string value = string.Empty;
            for (int y = startY; y < startY + 3; y++)
                for (int x = startX; x < startX + 3; x++)
                {
                    value += _tiles[x, y].GetTileValue();
                    _tiles[x, y].ResetTile();
                }
            return ExpressionEvaluator.Evaluate(value);
        }

        public void ClearAll()
        {
            for (int x = 0; x < _cols; x++)
                for (int y = 0; y < _rows; y++)
                    _tiles[x, y].ResetTile();
        }

        // ── GreedyStrategy 지원 ──────────────────────────────────────────────────

        /// <summary>
        /// 실제로 배치하지 않고, 배치 시 클리어가 발생하는지 판정한다.
        /// 클론 없이 수학적으로 체크한다.
        /// </summary>
        public bool WouldCauseClear(IBlockData block, Vector2Int pos)
        {
            var offsets  = block.GetRelativeTilePositions();
            var addedSet = new HashSet<(int, int)>(offsets.Count);
            foreach (var o in offsets) addedSet.Add((pos.x + o.x, pos.y + o.y));

            var checkedLineFirst = new HashSet<int>();
            var checkedLineSec   = new HashSet<int>();
            var checkedSquares   = new HashSet<(int, int)>();

            foreach (var (nx, ny) in addedSet)
            {
                // "row" (isRow=true): fixed nx, varying y
                if (checkedLineFirst.Add(nx) && IsLineFullWith(nx, isRow: true, addedSet))
                    return true;
                // "col" (isRow=false): fixed ny, varying x
                if (checkedLineSec.Add(ny) && IsLineFullWith(ny, isRow: false, addedSet))
                    return true;
                // square
                int sqX = (nx / 3) * 3;
                int sqY = (ny / 3) * 3;
                if (checkedSquares.Add((sqX, sqY)) && IsSquareFullWith(sqX, sqY, addedSet))
                    return true;
            }
            return false;
        }

        private bool IsLineFullWith(int index, bool isRow, HashSet<(int, int)> extra)
        {
            int len = isRow ? _rows : _cols;
            for (int i = 0; i < len; i++)
            {
                int tx = isRow ? index : i;
                int ty = isRow ? i     : index;
                if (!_tiles[tx, ty].IsPlaceBlock && !extra.Contains((tx, ty))) return false;
            }
            return true;
        }

        private bool IsSquareFullWith(int sx, int sy, HashSet<(int, int)> extra)
        {
            for (int y = sy; y < sy + 3; y++)
                for (int x = sx; x < sx + 3; x++)
                    if (!_tiles[x, y].IsPlaceBlock && !extra.Contains((x, y))) return false;
            return true;
        }
    }
}
