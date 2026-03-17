namespace TimeAuctionGame.Models;

public enum RoomState
{
    /// <summary>等待室：玩家等待，房主尚未開始</summary>
    Waiting,

    /// <summary>倒數中：所有玩家按住按鈕，3 秒倒數進行中</summary>
    Countdown,

    /// <summary>回合進行中：計時器正在計數</summary>
    InProgress,

    /// <summary>回合結束：顯示本回合結果與分數板</summary>
    RoundEnd,

    /// <summary>遊戲結束：所有回合完成或所有玩家時間歸零</summary>
    GameEnd
}
