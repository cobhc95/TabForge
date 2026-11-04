using TabForge.Platform;

namespace TabForge.Presenters;

// Owns: the aspect-preserving wood tiling of the fretboard: how many tiles cover the board and where each sits,
// so the texture is never stretched (one centred crop when a tile covers the board, else a row of tiles).
// Does not own: the texture pixels or the drawing (InstrumentPanelView).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public static class FretboardWoodTiling
{
    /// <summary>How many tiles cover a board of <paramref name="boardWidth"/> by <paramref name="boardHeight"/>
    /// with a texture of <paramref name="imageWidth"/> by <paramref name="imageHeight"/>: 0 for an empty box,
    /// 1 centred crop when one tile covers the board, else the row of tiles across it. Every tile keeps the
    /// texture's aspect exactly (tile height is the board height).</summary>
    public static int TileCount(double boardWidth, double boardHeight, double imageWidth, double imageHeight)
    {
        if (!(boardWidth > 0) || !(boardHeight > 0) || !(imageWidth > 0) || !(imageHeight > 0)) return 0;
        var tileWidth = boardHeight * imageWidth / imageHeight;
        if (!(tileWidth > 0)) return 0;
        if (tileWidth >= boardWidth) return 1;
        return (int)Math.Ceiling(boardWidth / tileWidth);
    }

    /// <summary>Where tile <paramref name="index"/> sits: same height as the board (aspect preserved), centred
    /// when it covers the board alone, else stepping across it. The caller clips to the board.</summary>
    public static UiRect TileRect(int index, double boardX, double boardY, double boardWidth, double boardHeight, double imageWidth, double imageHeight)
    {
        var tileWidth = boardHeight * imageWidth / imageHeight;
        var x = tileWidth >= boardWidth ? boardX + (boardWidth - tileWidth) / 2 : boardX + index * tileWidth;
        return new UiRect(x, boardY, tileWidth, boardHeight);
    }
}
