using System.Collections.Generic;
using UnityEngine;
using IGMain;

namespace Simulation
{
    /// <summary>
    /// 시뮬레이터용 9×9 보드.
    /// BoardGrid(순수 로직) + SimBoardTile(순수 데이터) 를 묶는다.
    /// IPhaseDataProvider 를 구현하여 TileValueGenerator 에 주입된다.
    /// </summary>
    public class SimulationBoard : IPhaseDataProvider
    {
        private readonly SimBoardTile[,] _tiles;
        private readonly BoardGrid       _grid;

        // ── IPhaseDataProvider ────────────────────────────────────────────────────

        public int   TotalClearCount     { get; private set; }
        public float BoardOccupancyRatio => _grid.OccupancyRatio;

        // ── 생성 / 초기화 ─────────────────────────────────────────────────────────

        public SimulationBoard()
        {
            _tiles = new SimBoardTile[IGConfig.BOARD_COL, IGConfig.BOARD_ROW];
            for (int x = 0; x < IGConfig.BOARD_COL; x++)
                for (int y = 0; y < IGConfig.BOARD_ROW; y++)
                    _tiles[x, y] = new SimBoardTile();

            _grid = new BoardGrid(_tiles);
        }

        public void Reset()
        {
            _grid.ClearAll();
            TotalClearCount = 0;
        }

        // ── 배치 / 클리어 ─────────────────────────────────────────────────────────

        public bool CanPlace(IBlockData block, Vector2Int pos) => _grid.CanPlaceBlock(block, pos);

        /// <summary>블록을 배치하고 클리어 결과를 반환한다.</summary>
        public (long rawScore, int clearedCount) Place(IBlockData block, Vector2Int pos)
        {
            _grid.PlaceBlock(block, pos);
            var (score, count) = _grid.CheckAndClearLines();
            TotalClearCount += count;
            return (score, count);
        }

        public bool IsAnyPlaceable(List<SimBlock> blocks)
        {
            var casted = new List<IBlockData>(blocks.Count);
            foreach (var b in blocks) casted.Add(b);
            return _grid.IsAnyBlockPlaceable(casted);
        }

        public bool WouldCauseClear(IBlockData block, Vector2Int pos) => _grid.WouldCauseClear(block, pos);

        // ── 유틸 ─────────────────────────────────────────────────────────────────

        /// <summary>배치 가능한 모든 위치를 반환한다.</summary>
        public List<Vector2Int> GetValidPositions(IBlockData block)
        {
            var result = new List<Vector2Int>();
            for (int y = 0; y < IGConfig.BOARD_ROW; y++)
                for (int x = 0; x < IGConfig.BOARD_COL; x++)
                {
                    var pos = new Vector2Int(x, y);
                    if (_grid.CanPlaceBlock(block, pos)) result.Add(pos);
                }
            return result;
        }

        /// <summary>현재 보드 상태를 딥카피한다. GreedyStrategy 에서 "최선 위치 평가" 시 사용.</summary>
        public SimulationBoard Clone()
        {
            var clone = new SimulationBoard();
            clone.TotalClearCount = TotalClearCount;
            for (int x = 0; x < IGConfig.BOARD_COL; x++)
                for (int y = 0; y < IGConfig.BOARD_ROW; y++)
                    clone._tiles[x, y].SetTileData(_tiles[x, y].Data);
            return clone;
        }
    }
}
