using TimeAuctionGame.Models;

namespace TimeAuctionGame.Services;

public class GameService
{
    public GameRound StartNewRound(Room room)
    {
        room.CurrentRound++;
        var round = new GameRound
        {
            RoundNumber = room.CurrentRound,
            State = RoundState.WaitingForPlayers
        };

        foreach (var player in room.Players.Values)
        {
            if (player.RemainingTime > TimeSpan.Zero)
            {
                round.PlayerData[player.Id] = new PlayerRoundData();
            }
        }

        room.CurrentGameRound = round;
        room.State = RoomState.Waiting;
        return round;
    }

    public bool AllParticipantsHolding(Room room)
    {
        var round = room.CurrentGameRound;
        if (round == null) return false;

        var participants = round.PlayerData.Where(p => p.Value.IsParticipating);
        return participants.All(p => p.Value.IsHolding);
    }

    public void StartCountdown(Room room)
    {
        var round = room.CurrentGameRound;
        if (round == null) return;

        round.State = RoundState.Countdown;
        round.CountdownStartTime = DateTimeOffset.UtcNow;
        room.State = RoomState.Countdown;
    }

    public void CancelCountdown(Room room, string playerId)
    {
        var round = room.CurrentGameRound;
        if (round == null) return;

        // Player who released during countdown does not participate
        if (round.PlayerData.TryGetValue(playerId, out var data))
        {
            data.IsHolding = false;
            data.IsParticipating = false;
        }

        round.State = RoundState.WaitingForPlayers;
        round.CountdownStartTime = null;
        room.State = RoomState.Waiting;
    }

    public void StartPlaying(Room room)
    {
        var round = room.CurrentGameRound;
        if (round == null) return;

        round.State = RoundState.Playing;
        round.StartTime = DateTimeOffset.UtcNow;
        room.State = RoomState.Playing;
    }

    public void PlayerHold(Room room, string playerId)
    {
        var round = room.CurrentGameRound;
        if (round == null) return;

        if (round.PlayerData.TryGetValue(playerId, out var data) && data.IsParticipating)
        {
            data.IsHolding = true;
        }
    }

    public void PlayerRelease(Room room, string playerId, long clientTimestampMs)
    {
        var round = room.CurrentGameRound;
        if (round == null || !round.PlayerData.ContainsKey(playerId)) return;

        var playerData = round.PlayerData[playerId];
        playerData.IsHolding = false;

        var clientTime = DateTimeOffset.FromUnixTimeMilliseconds(clientTimestampMs);
        playerData.ReleaseTime = clientTime;

        if (round.StartTime.HasValue)
        {
            playerData.TimeSpent = clientTime - round.StartTime.Value;
            if (playerData.TimeSpent < TimeSpan.Zero)
                playerData.TimeSpent = TimeSpan.Zero;

            // Deduct time from player
            var player = room.Players[playerId];
            player.RemainingTime -= playerData.TimeSpent;
            if (player.RemainingTime < TimeSpan.Zero)
                player.RemainingTime = TimeSpan.Zero;
        }
    }

    public int CountActiveHolders(Room room)
    {
        var round = room.CurrentGameRound;
        if (round == null) return 0;

        return round.PlayerData.Count(p => p.Value.IsParticipating && p.Value.IsHolding);
    }

    public bool IsRoundFinished(Room room)
    {
        var round = room.CurrentGameRound;
        if (round == null) return false;

        // Round finishes when at most 1 player is still holding
        var holdingCount = CountActiveHolders(room);
        return holdingCount <= 1;
    }

    public string? FinishRound(Room room)
    {
        var round = room.CurrentGameRound;
        if (round == null) return null;

        // The last holder (or last to release) wins
        string? winnerId = null;

        // Check if someone is still holding - they are the winner
        var stillHolding = round.PlayerData
            .FirstOrDefault(p => p.Value.IsParticipating && p.Value.IsHolding);

        if (stillHolding.Key != null)
        {
            winnerId = stillHolding.Key;
            // Record their release time as now
            stillHolding.Value.IsHolding = false;
            stillHolding.Value.ReleaseTime = DateTimeOffset.UtcNow;
            if (round.StartTime.HasValue)
            {
                stillHolding.Value.TimeSpent = DateTimeOffset.UtcNow - round.StartTime.Value;
                var player = room.Players[winnerId];
                player.RemainingTime -= stillHolding.Value.TimeSpent;
                if (player.RemainingTime < TimeSpan.Zero)
                    player.RemainingTime = TimeSpan.Zero;
            }
        }
        else
        {
            // All released - last one to release wins
            var lastRelease = round.PlayerData
                .Where(p => p.Value.IsParticipating && p.Value.ReleaseTime.HasValue)
                .OrderByDescending(p => p.Value.ReleaseTime)
                .FirstOrDefault();

            winnerId = lastRelease.Key;
        }

        if (winnerId != null)
        {
            round.WinnerId = winnerId;
            room.Players[winnerId].Score++;
        }

        round.State = RoundState.Finished;
        room.State = RoomState.RoundResult;

        return winnerId;
    }

    public bool IsGameOver(Room room)
    {
        return room.CurrentRound >= room.TotalRounds;
    }

    public string? GetGameWinner(Room room)
    {
        room.State = RoomState.GameOver;
        return room.Players.Values
            .OrderByDescending(p => p.Score)
            .FirstOrDefault()?.Id;
    }
}
