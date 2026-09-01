using System.Collections.Generic;
using UnityEngine;

namespace IGMain
{
    public interface IBlockData
    {
        List<Vector2Int> GetRelativeTilePositions();
        TileData GetTileData(int relX, int relY);
    }
}
