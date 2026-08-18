using System.Collections.Generic;

namespace ChessGame_PJ
{
    // Dữ liệu 1 phòng chơi, được đồng bộ qua Firebase Realtime Database
    // tại đường dẫn "rooms/{roomId}".
    public class RoomData
    {
        public string player1 { get; set; } = "";   // tên người tạo phòng (quân trắng)
        public string player2 { get; set; } = "";   // tên người tham gia (quân đen)

        public string turn { get; set; } = "white";      // "white" | "black"
        public string status { get; set; } = "waiting";  // waiting | playing | checkmate
        public string winner { get; set; } = "";
        public string check { get; set; } = "";           // "white" | "black" | ""

        // Trạng thái bàn cờ: key "row_col" -> mã quân (vd "W_Pawn"), "" là ô trống
        public Dictionary<string, string> board { get; set; } = new Dictionary<string, string>();

        // Quyền nhập thành còn hiệu lực hay không
        public bool wKingMoved { get; set; } = false;
        public bool bKingMoved { get; set; } = false;
        public bool wRookAMoved { get; set; } = false; // xe cột a (queenside)
        public bool wRookHMoved { get; set; } = false; // xe cột h (kingside)
        public bool bRookAMoved { get; set; } = false;
        public bool bRookHMoved { get; set; } = false;

        // Ô đang được chọn (đồng bộ để cả 2 người đều thấy viền vàng + chấm gợi ý)
        public int selRow { get; set; } = -1;
        public int selCol { get; set; } = -1;
        public List<string> legalMoves { get; set; } = new List<string>(); // "row_col"

        public long updatedAt { get; set; } = 0; // dùng để tránh xử lý trùng dữ liệu cũ
    }
}
