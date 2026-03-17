using Microsoft.AspNetCore.SignalR;
using TimeAuctionGame.Models;
using TimeAuctionGame.Services;

namespace TimeAuctionGame.Hubs;

public class GameHub : Hub
{
    private readonly RoomService _roomService;
    private readonly GameService _gameService;
    private readonly ILogger<GameHub> _logger;

    public GameHub(RoomService roomService, GameService gameService, ILogger<GameHub> logger)
    {
        _roomService = roomService;
        _gameService = gameService;
        _logger = logger;
    }

    // ── 連線管理 ──────────────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var (valid, playerId, playerName) = TryGetPlayer();

        if (!valid)
        {
            _logger.LogWarning("[Hub] 連線但無 Cookie，ConnectionId={ConnectionId}", Context.ConnectionId);
            Context.Abort();
            return;
        }

        _logger.LogInformation("[Hub] 玩家 {PlayerName}({PlayerId}) 連線，ConnectionId={ConnectionId}",
            playerName, playerId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
            _logger.LogWarning(exception, "[Hub] 玩家異常斷線，ConnectionId={ConnectionId}", Context.ConnectionId);
        else
            _logger.LogInformation("[Hub] 玩家正常斷線，ConnectionId={ConnectionId}", Context.ConnectionId);

        await _gameService.HandleDisconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ── 大廳 ──────────────────────────────────────────────────────────────────

    /// <summary>創建房間並加入對應 SignalR Group</summary>
    public async Task CreateRoom(string roomName, int maxTimeMinutes, int totalRounds)
    {
        var (valid, playerId, playerName) = TryGetPlayer();
        if (!valid) return;

        var (room, error) = _roomService.CreateRoom(
            roomName, maxTimeMinutes, totalRounds,
            playerId, Context.ConnectionId, playerName);

        if (room == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);
        await Clients.Caller.SendAsync("RoomCreated", new
        {
            roomId = room.RoomId,
            roomName = room.RoomName,
            maxTimeMinutes = room.MaxTimeMinutes,
            totalRounds = room.TotalRounds
        });

        // 廣播大廳列表更新給所有人
        await Clients.All.SendAsync("RoomListUpdated", _roomService.GetAllRooms());
    }

    /// <summary>加入指定房間（6 碼短碼）</summary>
    public async Task JoinRoom(string roomId)
    {
        var (valid, playerId, playerName) = TryGetPlayer();
        if (!valid) return;

        var (room, error) = _roomService.JoinRoom(roomId, playerId, Context.ConnectionId, playerName);

        if (room == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);

        // 回傳房間完整狀態給新加入者
        await Clients.Caller.SendAsync("RoomJoined", new
        {
            roomId = room.RoomId,
            roomName = room.RoomName,
            maxTimeMinutes = room.MaxTimeMinutes,
            totalRounds = room.TotalRounds,
            currentRound = room.CurrentRound,
            state = room.State.ToString(),
            isHost = room.HostConnectionId == Context.ConnectionId,
            players = room.Players.Values.Select(ToPlayerInfo)
        });

        // 通知房間內其他人
        await Clients.OthersInGroup(room.RoomId).SendAsync("PlayerJoined", ToPlayerInfo(
            room.Players[playerId]));

        // 廣播大廳列表更新
        await Clients.All.SendAsync("RoomListUpdated", _roomService.GetAllRooms());
    }

    /// <summary>主動離開房間</summary>
    public async Task LeaveRoom()
    {
        var (valid, playerId, _) = TryGetPlayer();
        if (!valid) return;

        var (disbanded, room) = _roomService.LeaveRoom(playerId, Context.ConnectionId);

        if (room == null) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.RoomId);

        if (disbanded)
        {
            await Clients.Group(room.RoomId).SendAsync("RoomDisbanded");
        }
        else
        {
            await Clients.Group(room.RoomId).SendAsync("PlayerLeft", playerId);
        }

        await Clients.All.SendAsync("RoomListUpdated", _roomService.GetAllRooms());
    }

    // ── 遊戲 ──────────────────────────────────────────────────────────────────

    /// <summary>房主開始遊戲</summary>
    public async Task StartGame()
    {
        var room = _roomService.FindRoomByConnectionId(Context.ConnectionId);
        if (room == null)
        {
            await Clients.Caller.SendAsync("Error", "找不到房間");
            return;
        }

        if (room.HostConnectionId != Context.ConnectionId)
        {
            await Clients.Caller.SendAsync("Error", "只有房主可以開始遊戲");
            return;
        }

        if (room.State != RoomState.Waiting)
        {
            await Clients.Caller.SendAsync("Error", "遊戲已在進行中");
            return;
        }

        _logger.LogInformation("[Hub] 房主啟動遊戲，Room={RoomId}", room.RoomId);
        await _gameService.StartRound(room);
    }

    /// <summary>房主開始下一回合</summary>
    public async Task StartNextRound()
    {
        var room = _roomService.FindRoomByConnectionId(Context.ConnectionId);
        if (room == null) return;

        if (room.HostConnectionId != Context.ConnectionId)
        {
            await Clients.Caller.SendAsync("Error", "只有房主可以開始下一回合");
            return;
        }

        if (room.State != RoomState.RoundEnd)
        {
            await Clients.Caller.SendAsync("Error", "目前不是回合結束狀態");
            return;
        }

        _logger.LogInformation("[Hub] 房主啟動下一回合，Room={RoomId}", room.RoomId);
        await _gameService.StartRound(room);
    }

    /// <summary>玩家按下按鈕</summary>
    public async Task PressButton()
    {
        var (valid, playerId, _) = TryGetPlayer();
        if (!valid) return;

        var room = _roomService.FindRoomByConnectionId(Context.ConnectionId);
        if (room == null) return;

        if (!room.Players.TryGetValue(playerId, out var player)) return;

        // 時間歸零、已按住、本回合放棄 → 忽略
        if (!player.IsActive || player.IsPressingButton || player.IsSittingOutRound) return;

        player.IsPressingButton = true;
        _logger.LogDebug("[Hub] 玩家 {PlayerName}({PlayerId}) 按下按鈕，Room={RoomId}",
            player.Name, playerId, room.RoomId);

        await _gameService.TryStartCountdown(room, playerId);
    }

    /// <summary>玩家放開按鈕</summary>
    public async Task ReleaseButton()
    {
        var (valid, playerId, _) = TryGetPlayer();
        if (!valid) return;

        var room = _roomService.FindRoomByConnectionId(Context.ConnectionId);
        if (room == null) return;

        _logger.LogDebug("[Hub] 玩家 {PlayerId} 放開按鈕，Room={RoomId}", playerId, room.RoomId);
        await _gameService.HandleButtonRelease(room, playerId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 從 Cookie 讀取玩家資訊，並驗證是否有效。
    /// 回傳 (true, playerId, playerName) 代表成功；
    /// 回傳 (false, "", "") 代表 Cookie 缺失，並已發送 Error 給 Caller。
    /// </summary>
    private (bool Valid, string PlayerId, string PlayerName) TryGetPlayer()
    {
        var httpContext = Context.GetHttpContext();
        var playerId = httpContext?.Request.Cookies["PlayerId"] ?? string.Empty;
        var playerName = httpContext?.Request.Cookies["PlayerName"] ?? string.Empty;

        if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(playerName))
            return (true, playerId, playerName);

        _logger.LogWarning("[Hub] 操作被拒絕：Cookie 缺失，ConnectionId={ConnectionId}", Context.ConnectionId);
        Clients.Caller.SendAsync("Error", "請先設定玩家名稱");
        return (false, string.Empty, string.Empty);
    }

    private static PlayerInfo ToPlayerInfo(Player p) => new()
    {
        PlayerId = p.PlayerId,
        Name = p.Name,
        RemainingTimeMs = p.RemainingTimeMs,
        Score = p.Score,
        IsActive = p.IsActive
    };
}
