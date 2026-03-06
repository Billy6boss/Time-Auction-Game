using Microsoft.AspNetCore.SignalR;
using TimeAuctionGame.Models;
using TimeAuctionGame.Services;

namespace TimeAuctionGame.Hubs;

public class GameHub : Hub
{
    private readonly RoomService _roomService;
    private readonly GameService _gameService;
    private readonly ILogger<GameHub> _logger;

    // Grace period: cancel pending disconnect when player reconnects with new connectionId
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>
        _pendingDisconnects = new();

    public GameHub(RoomService roomService, GameService gameService, ILogger<GameHub> logger)
    {
        _roomService = roomService;
        _gameService = gameService;
        _logger = logger;
    }

    // ─── Lobby ───────────────────────────────────────────────

    public async Task JoinLobby()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
        var rooms = _roomService.GetAllRooms().Select(MapRoom);
        await Clients.Caller.SendAsync("RoomListUpdated", rooms);
    }

    public async Task LeaveLobby()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "lobby");
    }

    public async Task CreateRoom(string roomName, string playerName, string playerId,
        int initialTimeMinutes, int totalRounds)
    {
        var room = _roomService.CreateRoom(roomName, playerId, playerName,
            Context.ConnectionId, initialTimeMinutes, totalRounds);

        await Groups.AddToGroupAsync(Context.ConnectionId, room.Id);
        await Clients.Caller.SendAsync("RoomCreated", MapRoom(room));

        // Notify lobby
        var rooms = _roomService.GetAllRooms().Select(MapRoom);
        await Clients.Group("lobby").SendAsync("RoomListUpdated", rooms);
    }

    // ─── Room ────────────────────────────────────────────────

    public async Task JoinRoom(string roomId, string playerName, string playerId)
    {
        _logger.LogInformation("JoinRoom: player={PlayerId} name={Name} room={RoomId} conn={ConnId}",
            playerId, playerName, roomId, Context.ConnectionId);

        // Cancel pending disconnect for this player's old connection (navigating lobby -> room)
        var existingRoom = _roomService.GetRoom(roomId);
        var oldConnId = existingRoom?.Players.GetValueOrDefault(playerId)?.ConnectionId;
        if (oldConnId != null && _pendingDisconnects.TryRemove(oldConnId, out var cts))
        {
            _logger.LogInformation("JoinRoom: cancelled pending disconnect for oldConn={OldConnId}", oldConnId);
            cts.Cancel();
            cts.Dispose();
        }

        var player = _roomService.AddPlayerToRoom(roomId, playerId, playerName, Context.ConnectionId);
        if (player == null)
        {
            _logger.LogWarning("JoinRoom: failed - player={PlayerId} could not join room={RoomId}", playerId, roomId);
            await Clients.Caller.SendAsync("Error", "無法加入房間");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        var room = _roomService.GetRoom(roomId)!;
        await Clients.Group(roomId).SendAsync("RoomUpdated", MapRoom(room));

        // Notify lobby
        var rooms = _roomService.GetAllRooms().Select(MapRoom);
        await Clients.Group("lobby").SendAsync("RoomListUpdated", rooms);
    }

    public async Task LeaveRoom(string roomId, string playerId)
    {
        _logger.LogInformation("LeaveRoom: player={PlayerId} room={RoomId} conn={ConnId}",
            playerId, roomId, Context.ConnectionId);
        _roomService.RemovePlayerFromRoom(roomId, playerId, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        var room = _roomService.GetRoom(roomId);
        if (room != null)
        {
            await Clients.Group(roomId).SendAsync("RoomUpdated", MapRoom(room));
        }

        // Notify lobby
        var rooms = _roomService.GetAllRooms().Select(MapRoom);
        await Clients.Group("lobby").SendAsync("RoomListUpdated", rooms);
    }

    public async Task GetRoomInfo(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null)
        {
            await Clients.Caller.SendAsync("Error", "房間不存在");
            return;
        }
        await Clients.Caller.SendAsync("RoomUpdated", MapRoom(room));
    }

    // ─── Game Flow ───────────────────────────────────────────

    public async Task StartGame(string roomId, string playerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.HostPlayerId != playerId) return;

        var round = _gameService.StartNewRound(room);
        await Clients.Group(roomId).SendAsync("GameStarted", MapRoom(room));
    }

    public async Task StartNextRound(string roomId, string playerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.HostPlayerId != playerId) return;

        if (_gameService.IsGameOver(room))
        {
            var winnerId = _gameService.GetGameWinner(room);
            var winnerName = winnerId != null ? room.Players[winnerId].Name : null;
            await Clients.Group(roomId).SendAsync("GameOver", winnerId, winnerName, MapRoom(room));
            return;
        }

        var round = _gameService.StartNewRound(room);
        await Clients.Group(roomId).SendAsync("NewRound", MapRoom(room));
    }

    public async Task ButtonDown(string roomId, string playerId)
    {
        _logger.LogDebug("ButtonDown: player={PlayerId} room={RoomId}", playerId, roomId);
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        _gameService.PlayerHold(room, playerId);
        await Clients.Group(roomId).SendAsync("PlayerHolding", playerId);

        // Check if all participants are holding
        if (_gameService.AllParticipantsHolding(room))
        {
            _gameService.StartCountdown(room);
            var countdownStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await Clients.Group(roomId).SendAsync("CountdownStarted", countdownStartMs);

            // Server-side 3-second countdown
            _ = RunCountdown(roomId);
        }
    }

    public async Task ButtonUp(string roomId, string playerId, long clientTimestampMs)
    {
        _logger.LogDebug("ButtonUp: player={PlayerId} room={RoomId} ts={Ts}", playerId, roomId, clientTimestampMs);
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        var round = room.CurrentGameRound;
        if (round == null) return;

        if (round.State == RoundState.Countdown)
        {
            // Released during countdown - cancel and exclude player
            _gameService.CancelCountdown(room, playerId);
            await Clients.Group(roomId).SendAsync("CountdownCancelled", playerId);
        }
        else if (round.State == RoundState.Playing)
        {
            // Released during play
            _gameService.PlayerRelease(room, playerId, clientTimestampMs);

            var playerData = round.PlayerData[playerId];
            await Clients.Group(roomId).SendAsync("PlayerReleased", playerId,
                playerData.TimeSpent.TotalSeconds);

            // Check if round is finished (only 1 or 0 holders left)
            if (_gameService.IsRoundFinished(room))
            {
                var winnerId = _gameService.FinishRound(room);
                var winnerName = winnerId != null ? room.Players[winnerId].Name : null;

                await Clients.Group(roomId).SendAsync("RoundEnded",
                    winnerId, winnerName, MapRoom(room));
            }
        }
        else if (round.State == RoundState.WaitingForPlayers)
        {
            // Released before countdown even started
            if (round.PlayerData.TryGetValue(playerId, out var data))
            {
                data.IsHolding = false;
            }
            await Clients.Group(roomId).SendAsync("PlayerNotHolding", playerId);
        }
    }

    // ─── Connection Lifecycle ────────────────────────────────

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connId = Context.ConnectionId;
        _logger.LogInformation("OnDisconnected: conn={ConnId} reason={Reason}",
            connId, exception?.Message ?? "clean");

        // Grace period: give the client time to reconnect via new connection (e.g., page navigation)
        var cts = new CancellationTokenSource();
        _pendingDisconnects[connId] = cts;

        try
        {
            await Task.Delay(3000, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Player reconnected - JoinRoom already handled cleanup
            _logger.LogInformation("OnDisconnected: grace period cancelled for conn={ConnId} (player reconnected)", connId);
            _pendingDisconnects.TryRemove(connId, out _);
            await base.OnDisconnectedAsync(exception);
            return;
        }

        _pendingDisconnects.TryRemove(connId, out _);

        // Check if mapping still exists (might already be cleaned up by JoinRoom reconnect)
        var mapping = _roomService.GetConnectionMapping(connId);
        if (mapping.HasValue)
        {
            var (roomId, playerId) = mapping.Value;
            _logger.LogInformation("OnDisconnected: removing player={PlayerId} from room={RoomId}", playerId, roomId);
            _roomService.RemovePlayerFromRoom(roomId, playerId, connId);

            var room = _roomService.GetRoom(roomId);
            if (room != null)
            {
                await Clients.Group(roomId).SendAsync("RoomUpdated", MapRoom(room));
            }

            var rooms = _roomService.GetAllRooms().Select(MapRoom);
            await Clients.Group("lobby").SendAsync("RoomListUpdated", rooms);
        }
        else
        {
            _logger.LogInformation("OnDisconnected: conn={ConnId} had no active room mapping (already cleaned up)", connId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ─── Helpers ─────────────────────────────────────────────

    private async Task RunCountdown(string roomId)
    {
        await Task.Delay(3000);

        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        var round = room.CurrentGameRound;
        if (round == null || round.State != RoundState.Countdown) return;

        // Countdown completed, start playing
        _gameService.StartPlaying(room);
        var startTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await Clients.Group(roomId).SendAsync("RoundStarted", startTimeMs);

        // Start server timer broadcast
        _ = BroadcastTimer(roomId);
    }

    private async Task BroadcastTimer(string roomId)
    {
        while (true)
        {
            await Task.Delay(100); // 10 times per second

            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            var round = room.CurrentGameRound;
            if (round == null || round.State != RoundState.Playing || !round.StartTime.HasValue)
                return;

            var elapsed = (DateTimeOffset.UtcNow - round.StartTime.Value).TotalSeconds;
            await Clients.Group(roomId).SendAsync("TimerUpdate", elapsed);
        }
    }

    private static object MapRoom(Room room) => new
    {
        room.Id,
        room.Name,
        room.HostPlayerId,
        room.InitialTimeMinutes,
        room.TotalRounds,
        room.CurrentRound,
        State = room.State.ToString(),
        Players = room.Players.Values.Select(p => new
        {
            p.Id,
            p.Name,
            p.Score,
            RemainingTimeSeconds = p.RemainingTime.TotalSeconds
        }),
        CurrentGameRound = room.CurrentGameRound != null ? new
        {
            room.CurrentGameRound.RoundNumber,
            State = room.CurrentGameRound.State.ToString(),
            room.CurrentGameRound.WinnerId,
            PlayerData = room.CurrentGameRound.PlayerData.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.IsHolding,
                    kvp.Value.IsParticipating,
                    TimeSpentSeconds = kvp.Value.TimeSpent.TotalSeconds
                })
        } : null
    };
}
