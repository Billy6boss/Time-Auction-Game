namespace TimeAuctionGame.Models;

public class Player
{
    /// <summary>玩家唯一識別碼（來自 Cookie，跨連線持久）</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>當前 SignalR 連線 ID（每次重連會變動）</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>玩家名稱</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>玩家剩餘時間（毫秒）</summary>
    public long RemainingTimeMs { get; set; }

    /// <summary>目前累積分數</summary>
    public int Score { get; set; }

    /// <summary>玩家是否正在按住按鈕</summary>
    public bool IsPressingButton { get; set; }

    /// <summary>
    /// 本回合是否已放棄（倒數期間 >= 2s 放開按鈕）。
    /// true 就算按住按鈕也不參與本回合；下一回合開始時重置為 false。
    /// </summary>
    public bool IsSittingOutRound { get; set; }

    /// <summary>
    /// 玩家是否仍有時間可以參與競標（RemainingTimeMs > 0）。
    /// false 表示時間已歸零，按鈕可按但無效，僅留在房間觀戰。
    /// </summary>
    public bool IsActive => RemainingTimeMs > 0;
}
