using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TimeAuctionGame.Models;

namespace TimeAuctionGame.Services;

public class RoomService
{
    private readonly ILogger<RoomService> _logger;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    // connectionId -> (roomId, playerId)
    private readonly ConcurrentDictionary<string, (string RoomId, string PlayerId)> _connectionMap = new();

    public RoomService(ILogger<RoomService> logger)
    {
        _logger = logger;
    }

    public IEnumerable<Room> GetAllRooms() => _rooms.Values;

    public Room? GetRoom(string roomId) => _rooms.GetValueOrDefault(roomId);

    public Room CreateRoom(string name, string hostPlayerId, string hostPlayerName,
        string hostConnectionId, int initialTimeMinutes, int totalRounds)
    {
        var room = new Room
        {
            Name = name,
            HostPlayerId = hostPlayerId,
            InitialTimeMinutes = initialTimeMinutes,
            TotalRounds = totalRounds
        };

        var player = new Player
        {
            Id = hostPlayerId,
            Name = hostPlayerName,
            ConnectionId = hostConnectionId,
            RemainingTime = TimeSpan.FromMinutes(initialTimeMinutes)
        };

        room.Players[player.Id] = player;
        _rooms[room.Id] = room;
        _connectionMap[hostConnectionId] = (room.Id, player.Id);

        _logger.LogInformation("CreateRoom: room={RoomId} name={Name} host={HostPlayerId} conn={ConnId}",
            room.Id, room.Name, hostPlayerId, hostConnectionId);

        return room;
    }

    public bool RemoveRoom(string roomId) => _rooms.TryRemove(roomId, out _);

    public Player? AddPlayerToRoom(string roomId, string playerId, string playerName, string connectionId)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
        {
            _logger.LogWarning("AddPlayerToRoom: room={RoomId} not found", roomId);
            return null;
        }

        if (room.State != RoomState.Waiting)
        {
            _logger.LogWarning("AddPlayerToRoom: room={RoomId} state={State} is not Waiting", roomId, room.State);
            return null;
        }

        // If player already exists (e.g. host navigating lobby → room page), update connectionId
        if (room.Players.TryGetValue(playerId, out var existingPlayer))
        {
            var oldConnId = existingPlayer.ConnectionId;
            _connectionMap.TryRemove(oldConnId, out _);
            existingPlayer.ConnectionId = connectionId;
            _connectionMap[connectionId] = (roomId, playerId);
            _logger.LogInformation(
                "AddPlayerToRoom: player={PlayerId} rejoined room={RoomId} oldConn={OldConn} newConn={NewConn}",
                playerId, roomId, oldConnId, connectionId);
            return existingPlayer;
        }

        var player = new Player
        {
            Id = playerId,
            Name = playerName,
            ConnectionId = connectionId,
            RemainingTime = TimeSpan.FromMinutes(room.InitialTimeMinutes)
        };

        room.Players[player.Id] = player;
        _connectionMap[connectionId] = (room.Id, player.Id);

        _logger.LogInformation("AddPlayerToRoom: player={PlayerId} name={Name} joined room={RoomId} conn={ConnId}",
            playerId, playerName, roomId, connectionId);

        return player;
    }

    public bool RemovePlayerFromRoom(string roomId, string playerId, string connectionId)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
        {
            _logger.LogWarning("RemovePlayerFromRoom: room={RoomId} not found", roomId);
            return false;
        }

        _connectionMap.TryRemove(connectionId, out _);
        var removed = room.Players.Remove(playerId);

        _logger.LogInformation(
            "RemovePlayerFromRoom: player={PlayerId} removed={Removed} from room={RoomId} remaining={Count}",
            playerId, removed, roomId, room.Players.Count);

        // If host left or room is empty, remove the room
        if (room.Players.Count == 0)
        {
            _logger.LogInformation("RemovePlayerFromRoom: room={RoomId} is now empty, deleting", roomId);
            RemoveRoom(roomId);
        }
        else if (room.HostPlayerId == playerId)
        {
            // Transfer host to next player
            var newHost = room.Players.Values.First();
            room.HostPlayerId = newHost.Id;
            _logger.LogInformation("RemovePlayerFromRoom: host transferred to player={NewHostId} in room={RoomId}",
                newHost.Id, roomId);
        }

        return removed;
    }

    public (string RoomId, string PlayerId)? GetConnectionMapping(string connectionId)
    {
        return _connectionMap.TryGetValue(connectionId, out var mapping) ? mapping : null;
    }

    public void RemoveConnectionMapping(string connectionId)
    {
        _connectionMap.TryRemove(connectionId, out _);
    }

    public void UpdateConnectionId(string playerId, string roomId, string newConnectionId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        if (!room.Players.TryGetValue(playerId, out var player)) return;

        // Remove old mapping
        var oldConnId = player.ConnectionId;
        _connectionMap.TryRemove(oldConnId, out _);

        // Update
        player.ConnectionId = newConnectionId;
        _connectionMap[newConnectionId] = (roomId, playerId);
    }
}
