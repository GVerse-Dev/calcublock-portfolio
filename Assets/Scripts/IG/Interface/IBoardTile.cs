namespace IGMain
{
    public interface IBoardTile
    {
        bool IsPlaceBlock { get; }
        string GetTileValue();
        void SetTileData(TileData data);
        void ResetTile();
    }
}
