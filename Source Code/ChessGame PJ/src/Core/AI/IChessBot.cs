using System.Threading.Tasks;

namespace ChessGame_PJ.Core.AI
{
    public interface IChessBot
    {
        Task<(int fromRow, int fromCol, int toRow, int toCol, string? promotion)> GetBestMoveAsync(ChessGame game, string botColor);
    }
}
