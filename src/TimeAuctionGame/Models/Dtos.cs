namespace TimeAuctionGame.Models;

/// <summary>大廳顯示用的房間摘要</summary>
public class RoomSummary
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxTimeMinutes { get; set; }
    public int TotalRounds { get; set; }
    public int CurrentRound { get; set; }
    public RoomState State { get; set; }
}

/// <summary>前端玩家資訊（廣播用）</summary>
public class PlayerInfo
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long RemainingTimeMs { get; set; }
    public int Score { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>回合/遊戲結束分數條目</summary>
public class ScoreEntry
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public long RemainingTimeMs { get; set; }
}
