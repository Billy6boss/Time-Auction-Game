namespace TimeAuctionGame.Models;

public class Player
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public TimeSpan RemainingTime { get; set; }
    public int Score { get; set; }
}
