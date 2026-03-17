using System.Collections.Concurrent;

namespace TimeAuctionGame.Models;

public class Room
{
    /// <summary>房間識別碼（6 碼隨機英數字短碼）</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>房間名稱</summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>房主的 SignalR 連線 ID</summary>
    public string HostConnectionId { get; set; } = string.Empty;

    /// <summary>房間內的玩家（Key: PlayerId）</summary>
    public ConcurrentDictionary<string, Player> Players { get; set; } = new();

    /// <summary>每位玩家一開始擁有的時間（分鐘）</summary>
    public int MaxTimeMinutes { get; set; }

    /// <summary>總回合數</summary>
    public int TotalRounds { get; set; }

    /// <summary>當前進行的回合（從 1 開始）</summary>
    public int CurrentRound { get; set; }

    /// <summary>房間目前的遊戲狀態</summary>
    public RoomState State { get; set; } = RoomState.Waiting;

    /// <summary>
    /// 本回合開始的 Unix 時間戳記（毫秒）。
    /// 倒數結束、計時開始時設定，由 Client 端計算顯示用。
    /// </summary>
    public long RoundStartTime { get; set; }

    /// <summary>倒數開始時，用來取消倒數的 CancellationTokenSource</summary>
    public CancellationTokenSource? CountdownCts { get; set; }

    /// <summary>
    /// 倒數啟動的 Unix 時間戳記（毫秒）。
    /// 用於判斷玩家在倒數期間放開是否已過 2 秒。
    /// </summary>
    public long CountdownStartTime { get; set; }

    // ── Computed helpers ──────────────────────────────────────────────────────

    /// <summary>目前在房間內的玩家數</summary>
    public int PlayerCount => Players.Count;

    /// <summary>仍有剩餘時間、可參與競標的玩家</summary>
    public IEnumerable<Player> ActivePlayers => Players.Values.Where(p => p.IsActive);

    /// <summary>目前按住按鈕且有效且未放棄本回合的玩家</summary>
    public IEnumerable<Player> PressedActivePlayers =>
        Players.Values.Where(p => p.IsActive && p.IsPressingButton && !p.IsSittingOutRound);

    /// <summary>本回合最後一個放開按鈕的有效玩家（用於決定勝者）</summary>
    public Player? LastReleasedActivePlayer { get; set; }
}
