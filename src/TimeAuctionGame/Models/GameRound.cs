namespace TimeAuctionGame.Models;

public class GameRound
{
    public int RoundNumber { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? CountdownStartTime { get; set; }
    public RoundState State { get; set; } = RoundState.WaitingForPlayers;
    public string? WinnerId { get; set; }
    public Dictionary<string, PlayerRoundData> PlayerData { get; set; } = new();
}

public class PlayerRoundData
{
    public bool IsHolding { get; set; }
    public bool IsParticipating { get; set; } = true;
    public DateTimeOffset? ReleaseTime { get; set; }
    public TimeSpan TimeSpent { get; set; }
}

public enum RoundState
{
    WaitingForPlayers,
    Countdown,
    Playing,
    Finished
}
