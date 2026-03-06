namespace TimeAuctionGame.Models;

public class Room
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string HostPlayerId { get; set; } = string.Empty;
    public int InitialTimeMinutes { get; set; }
    public int TotalRounds { get; set; }
    public int CurrentRound { get; set; }
    public RoomState State { get; set; } = RoomState.Waiting;
    public Dictionary<string, Player> Players { get; set; } = new();
    public GameRound? CurrentGameRound { get; set; }
}

public enum RoomState
{
    Waiting,
    Countdown,
    Playing,
    RoundResult,
    GameOver
}
