# ♟️ Chess Game PJ

An online 2-player chess game for Windows, built with **C# / .NET 10 (WinForms)** and synchronized in real time through **Firebase Realtime Database**. Two players on two different machines — no shared LAN required — connect through a simple 6-digit room code and play a full, rules-accurate game of chess.

**Repository:** https://github.com/Long9boy/ChessGame

---

## ✨ Features

- **Complete standard chess rules**, implemented and verified from scratch:
  - Legal move generation for all pieces (pawn, knight, bishop, rook, queen, king)
  - Check and checkmate detection
  - Castling (kingside & queenside) with full legality checks
  - Pawn promotion with a piece-selection dialog (Queen / Rook / Bishop / Knight)
- **Real online multiplayer** over the internet via Firebase Realtime Database (REST API) — no dedicated game server needed
- **Room-based matchmaking**: create a room (become White) or join one with a code (become Black)
- **Live visual feedback**: selected-square highlight, move-hint dots, red king highlight when in check, and real-time player name/turn updates
- **Self-contained build**: all 12 piece images are embedded resources baked into the `.exe`, so the app ships as a single file with no external asset folder

## 🖥️ Screens

| Screen | Description |
|---|---|
| **Form2 — Lobby** | Enter your name, then **Create Room** (generates a random room code, you play White) or **Join Room** (enter an existing code, you play Black) |
| **Form1 — Board** | An 8x8 grid of buttons representing the board; click a piece to see legal moves, click a highlighted square to move |

## 🏗️ Tech Stack

| Component | Technology | Role |
|---|---|---|
| Language | C# (.NET 10) | Core application language |
| UI | Windows Forms (WinForms) | Desktop windows, 8x8 board, promotion dialog |
| Online data | Firebase Realtime Database (REST API) | Stores room state, syncs moves between players |
| Networking | `HttpClient` + `System.Text.Json` | Sends/receives JSON to Firebase (`GET`, `PUT`, `PATCH`) |
| Assets | Embedded Resource (PNG) | 12 chess piece images embedded directly into the executable |

## 🔁 How Online Sync Works

1. Player 1 creates a room → a random 6-digit **Room ID** is generated and the initial game state is written to Firebase with `PUT`.
2. Player 2 enters that Room ID to join → a `PATCH` updates `player2` in the room data.
3. While playing, a `Timer` **polls** the room every **1.2 seconds** (`GetAsync`) to refresh the board, current turn, and check status for both players.
4. Every legal move performed locally is written back to Firebase with `PATCH`, carrying the full updated board state; the opponent picks it up on their next poll.

```csharp
RoomData room = await FirebaseClient.GetAsync<RoomData>($"rooms/{roomId}");

await FirebaseClient.PatchAsync($"rooms/{roomId}", patch); // send the new move
```

## 🧠 Core Algorithm

The rules engine lives in `ChessGame.cs`. The board is a `string[8,8]` array, where each cell holds a piece code such as `"W_Pawn"` or `"B_King"`, or an empty string for an empty square.

1. **`GetPseudoMoves()`** — generates every geometrically valid move per piece type (pawn pushes/captures, knight L-shapes, sliding pieces via `Slide()`, king moves + castling), without yet checking for check.
2. **`GetLegalMoves()`** — for each pseudo-move, simulates it on a cloned board (`CloneBoard`) and keeps it only if it doesn't leave the mover's own king in check (`IsInCheck`).
3. **`IsSquareAttacked()` / `IsInCheck()` / `IsCheckmate()`** — determine whether a square is under attack, whether the king is in check, and whether that check is inescapable (no legal moves remain).
4. **Castling & promotion** are handled as special cases with their own condition checks and, for promotion, a two-step flow (`TryMove()` flags `NeedsPromotion`, then is called again once the player picks a piece).

## 📁 Project Structure

```
ChessGame PJ/
├── ChessGame PJ.slnx              # Solution file
└── ChessGame PJ/
    ├── ChessGame.cs                # Chess rules engine (moves, check, checkmate, castling, promotion)
    ├── FirebaseClient.cs           # REST client for Firebase Realtime Database (GET/PUT/PATCH)
    ├── RoomData.cs                 # Data model for a game room, synced via Firebase
    ├── ImageResources.cs           # Loads embedded piece images
    ├── Form1.cs / Form1.Designer.cs   # Main board screen
    ├── Form2.cs / Form2.Designer.cs   # Lobby screen (create/join room)
    ├── PromotionForm.cs            # Pawn promotion piece-picker dialog
    ├── Program.cs                  # Application entry point
    ├── assets/textures/            # Embedded piece artwork (12 PNGs)
    └── Resources/                  # Additional bundled resources
```

## 🚀 Getting Started

### Prerequisites

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) with the Windows Desktop workload
- A Firebase project with a **Realtime Database** in test mode (open read/write rules), since the app talks to it via plain REST calls without an auth token

### Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/Long9boy/ChessGame.git
   cd ChessGame
   ```
2. In `FirebaseClient.cs`, set `DatabaseUrl` to your own Firebase Realtime Database URL:
   ```csharp
   private const string DatabaseUrl = "https://<your-project>-default-rtdb.<region>.firebasedatabase.app";
   ```
3. Build and run:
   ```bash
   dotnet build "ChessGame PJ.slnx"
   dotnet run --project "ChessGame PJ"
   ```

### Playing

1. Launch the app on two machines (or two instances for local testing).
2. On the first instance, enter a name and click **Create Room** — note the generated room code.
3. On the second instance, enter a name, click **Join Room**, and enter that code.
4. Play chess! The board, turns, and check status stay in sync automatically.

## ⚠️ Known Limitations

- Sync uses periodic polling (1.2s) rather than a real-time listener/WebSocket, so there can be a small delay
- En passant capture is not implemented yet
- Special draw rules (threefold repetition, 50-move rule) are not implemented yet
- No user authentication or access control on the Firebase room data

## 🔭 Roadmap

- [ ] Real-time listener (Firebase Streaming) instead of polling
- [ ] En passant and draw-rule support
- [ ] Play-vs-AI mode
- [ ] Match history logging
- [ ] In-room chat
- [ ] Game clock / timer

## 📄 License

No license has been specified yet for this repository. Add a `LICENSE` file if you'd like to define how others may use this code.
