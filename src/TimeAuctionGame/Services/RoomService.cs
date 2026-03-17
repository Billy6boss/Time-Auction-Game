using System.Collections.Concurrent;
using TimeAuctionGame.Models;

namespace TimeAuctionGame.Services;

public class RoomService
{
    private const int MaxPlayersPerRoom = 30;
    private const string ShortIdChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 去掉易混淆的 I、O、0、1

    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly Random _random = Random.Shared;
    private readonly ILogger<RoomService> _logger;

    public RoomService(ILogger<RoomService> logger)
    {
        _logger = logger;
    }

    // ── 房間 CRUD ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 創建新房間，回傳建立的 Room；失敗時回傳 null 並帶出錯誤訊息。
    /// </summary>
    public (Room? Room, string? Error) CreateRoom(
        string roomName,
        int maxTimeMinutes,
        int totalRounds,
        string hostPlayerId,
        string hostConnectionId,
        string hostName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
        {
            _logger.LogWarning("[CreateRoom] 失敗：房間名稱為空，Player={PlayerId}", hostPlayerId);
            return (null, "房間名稱不可為空");
        }

        if (!new[] { 1, 5, 10, 15, 20 }.Contains(maxTimeMinutes))
        {
            _logger.LogWarning("[CreateRoom] 失敗：無效時間 {Time}，Player={PlayerId}", maxTimeMinutes, hostPlayerId);
            return (null, "時間必須是 1/5/10/15/20 分鐘");
        }

        if (!new[] { 5, 10, 15, 20, 25 }.Contains(totalRounds))
        {
            _logger.LogWarning("[CreateRoom] 失敗：無效回合數 {Rounds}，Player={PlayerId}", totalRounds, hostPlayerId);
            return (null, "回合數必須是 5/10/15/20/25");
        }

        var roomId = GenerateShortId();
        var host = new Player
        {
            PlayerId = hostPlayerId,
            ConnectionId = hostConnectionId,
            Name = hostName,
            RemainingTimeMs = (long)maxTimeMinutes * 60 * 1000
        };

        var room = new Room
        {
            RoomId = roomId,
            RoomName = roomName.Trim(),
            HostConnectionId = hostConnectionId,
            MaxTimeMinutes = maxTimeMinutes,
            TotalRounds = totalRounds,
            CurrentRound = 0,
            State = RoomState.Waiting
        };
        room.Players.TryAdd(hostPlayerId, host);

        _rooms.TryAdd(roomId, room);

        _logger.LogInformation("[CreateRoom] 房間 {RoomId}（{RoomName}）已建立，房主={HostName}，時間={Time}分，回合={Rounds}",
            roomId, roomName.Trim(), hostName, maxTimeMinutes, totalRounds);

        return (room, null);
    }

    /// <summary>
    /// 玩家加入房間，回傳房間；失敗時回傳 null 並帶出錯誤訊息。
    /// </summary>
    public (Room? Room, string? Error) JoinRoom(
        string roomId,
        string playerId,
        string connectionId,
        string playerName)
    {
        if (!_rooms.TryGetValue(roomId.ToUpperInvariant(), out var room))
        {
            _logger.LogWarning("[JoinRoom] 失敗：找不到房間 {RoomId}，Player={PlayerId}", roomId, playerId);
            return (null, "找不到該房間");
        }

        if (room.State != RoomState.Waiting)
        {
            _logger.LogWarning("[JoinRoom] 失敗：房間 {RoomId} 狀態為 {State}，不允許加入，Player={PlayerId}",
                roomId, room.State, playerId);
            return (null, "遊戲已開始，無法加入");
        }

        if (room.PlayerCount >= MaxPlayersPerRoom)
        {
            _logger.LogWarning("[JoinRoom] 失敗：房間 {RoomId} 已滿（{Count}/{Max}），Player={PlayerId}",
                roomId, room.PlayerCount, MaxPlayersPerRoom, playerId);
            return (null, $"房間已滿（上限 {MaxPlayersPerRoom} 人）");
        }

        // 若同一 PlayerId 重連，更新 ConnectionId
        if (room.Players.TryGetValue(playerId, out var existing))
        {
            _logger.LogInformation("[JoinRoom] 玩家 {PlayerName}({PlayerId}) 重連至房間 {RoomId}",
                playerName, playerId, roomId);
            existing.ConnectionId = connectionId;
            return (room, null);
        }

        var player = new Player
        {
            PlayerId = playerId,
            ConnectionId = connectionId,
            Name = playerName,
            RemainingTimeMs = (long)room.MaxTimeMinutes * 60 * 1000
        };
        room.Players.TryAdd(playerId, player);

        _logger.LogInformation("[JoinRoom] 玩家 {PlayerName}({PlayerId}) 加入房間 {RoomId}，目前 {Count} 人",
            playerName, playerId, roomId, room.PlayerCount);

        return (room, null);
    }

    /// <summary>
    /// 玩家離開房間。若為房主，解散整個房間並回傳 disbanded = true。
    /// </summary>
    public (bool Disbanded, Room? Room) LeaveRoom(string playerId, string connectionId)
    {
        var room = FindRoomByConnectionId(connectionId);
        if (room == null)
        {
            _logger.LogDebug("[LeaveRoom] ConnectionId={ConnectionId} 不在任何房間", connectionId);
            return (false, null);
        }

        // 房主離線 → 解散房間
        if (room.HostConnectionId == connectionId)
        {
            _rooms.TryRemove(room.RoomId, out _);
            room.CountdownCts?.Cancel();
            _logger.LogInformation("[LeaveRoom] 房主離開，房間 {RoomId} 已解散", room.RoomId);
            return (true, room);
        }

        // 先用 connectionId 找到玩家，再移除（避免 playerId 為空字串時找不到）
        var leaving = room.Players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
        if (leaving != null)
        {
            room.Players.TryRemove(leaving.PlayerId, out _);
            _logger.LogInformation("[LeaveRoom] 玩家 {PlayerName}({PlayerId}) 離開房間 {RoomId}，剩餘 {Count} 人",
                leaving.Name, leaving.PlayerId, room.RoomId, room.PlayerCount);
        }

        return (false, room);
    }

    /// <summary>依 ConnectionId 找玩家所在房間（用於斷線處理）</summary>
    public Room? FindRoomByConnectionId(string connectionId)
    {
        return _rooms.Values.FirstOrDefault(r =>
            r.Players.Values.Any(p => p.ConnectionId == connectionId));
    }

    /// <summary>以短碼查詢房間</summary>
    public Room? GetRoom(string roomId) =>
        _rooms.TryGetValue(roomId.ToUpperInvariant(), out var room) ? room : null;

    /// <summary>取得所有房間摘要（供大廳顯示）</summary>
    public IEnumerable<RoomSummary> GetAllRooms() =>
        _rooms.Values.Select(r => new RoomSummary
        {
            RoomId = r.RoomId,
            RoomName = r.RoomName,
            PlayerCount = r.PlayerCount,
            MaxTimeMinutes = r.MaxTimeMinutes,
            TotalRounds = r.TotalRounds,
            CurrentRound = r.CurrentRound,
            State = r.State
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GenerateShortId()
    {
        string id;
        do
        {
            id = new string(Enumerable.Range(0, 6)
                .Select(_ => ShortIdChars[_random.Next(ShortIdChars.Length)])
                .ToArray());
        } while (_rooms.ContainsKey(id));
        return id;
    }
}
