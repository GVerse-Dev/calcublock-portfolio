using IGMain;

namespace Simulation
{
    /// <summary>IBoardTile 의 순수 C# 구현. 시뮬레이터 전용 보드 타일.</summary>
    public class SimBoardTile : IBoardTile
    {
        private TileData _data = TileData.Empty;

        public bool   IsPlaceBlock    => _data.IsValid;
        public string GetTileValue()  => _data.Value;
        public void   SetTileData(TileData data) { _data = data; }
        public void   ResetTile()                { _data = TileData.Empty; }

        public TileData Data => _data;
    }
}
