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
    /// [per-round] 本回合是否已放棄（倒數期間 >= 2s 放開按鈕）。
    /// true 就算按住按鈕也不參與本回合；由 <see cref="ResetForRound"/> 重置。
    /// </summary>
    public bool IsSittingOutRound { get; set; }

    /// <summary>
    /// [per-round] 本回合實際出價時間（毫秒）。
    /// = min(放開時經過時間, 放開前剩餘時間)，用於回合結束時決定勝者。
    /// 由 <see cref="ResetForRound"/> 重置。
    /// </summary>
    public long EffectiveBid { get; set; }

    /// <summary>玩家是否仍有時間可以參與競標（RemainingTimeMs > 0）。</summary>
    public bool IsActive => RemainingTimeMs > 0;

    /// <summary>
    /// 重置所有 [per-round] 狀態欄位。
    /// 應於每回合開始（<see cref="TimeAuctionGame.Services.GameService.StartRound"/>）時呼叫。
    /// 新增每回合狀態欄位時，請在此一併加入重置邏輯。
    /// </summary>
    public void ResetForRound()
    {
        IsPressingButton  = false;
        IsSittingOutRound = false;
        EffectiveBid      = 0;
    }
}
