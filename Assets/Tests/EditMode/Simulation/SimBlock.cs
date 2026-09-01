using System.Collections.Generic;
using UnityEngine;
using IGMain;

namespace Simulation
{
    /// <summary>
    /// IBlockData 의 순수 C# 구현. BlockShape + tile values 를 래핑한다.
    /// GetRelativeTilePositions() 와 GetTileData() 는 BlockShape 에 위임한다.
    /// </summary>
    public class SimBlock : IBlockData
    {
        private readonly BlockShape  _shape;
        private readonly string[,]  _values; // [y, x], 빈 셀은 null

        public BlockShape Shape  => _shape;

        public SimBlock(BlockShape shape, string[,] values)
        {
            _shape  = shape;
            _values = values;
        }

        public List<Vector2Int> GetRelativeTilePositions() => _shape.GetRelativeTilePositions();

        public TileData GetTileData(int relX, int relY)
        {
            int x = relX + Mathf.RoundToInt(_shape.VisualPivot.x);
            int y = relY + Mathf.RoundToInt(_shape.VisualPivot.y);

            if (y < 0 || y >= _values.GetLength(0) ||
                x < 0 || x >= _values.GetLength(1))
                return TileData.Empty;

            var v = _values[y, x];
            return string.IsNullOrEmpty(v) ? TileData.Empty : new TileData(v);
        }

        /// <summary>채워진 타일들의 값 목록 (통계 수집용).</summary>
        public IEnumerable<string> FilledValues()
        {
            for (int y = 0; y < _shape.Height; y++)
                for (int x = 0; x < _shape.Width; x++)
                    if (_shape.Shape[y, x] == 1 && !string.IsNullOrEmpty(_values[y, x]))
                        yield return _values[y, x];
        }
    }
}
