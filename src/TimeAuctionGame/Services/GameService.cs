using Microsoft.AspNetCore.SignalR;
using TimeAuctionGame.Hubs;
using TimeAuctionGame.Models;

namespace TimeAuctionGame.Services;

public class GameService
{
    private readonly RoomService _roomService;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<GameService> _logger;

    public GameService(
        RoomService roomService,
        IHubContext<GameHub> hubContext,
        ILogger<GameService> logger)
    {
        _roomService = roomService;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ── 回合流程 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 開始新回合：重置所有玩家按鈕狀態，等待玩家全部按住按鈕。
    /// （實際倒數由 PressButton 觸發 TryStartCountdown 啟動）
    /// </summary>
    public async Task StartRound(Room room)
    {
        room.CurrentRound++;
        room.State = RoomState.Waiting;

        // 重置所有玩家按鈕狀態
        foreach (var player in room.Players.Values)
        {
            player.IsPressingButton = false;
            player.IsSittingOutRound = false;
        }

        room.LastReleasedActivePlayer = null;

        _logger.LogInformation("[Room {RoomId}] 回合 {Round}/{Total} 準備開始",
            room.RoomId, room.CurrentRound, room.TotalRounds);

        await _hubContext.Clients.Group(room.RoomId)
            .SendAsync("RoundPrepare", new
            {
                round = room.CurrentRound,
                totalRounds = room.TotalRounds,
                scores = BuildScoreEntries(room)
            });
    }

    /// <summary>
    /// 玩家按下按鈕後，檢查是否所有有效玩家都已按住 → 啟動倒數。
    /// </summary>
    public async Task TryStartCountdown(Room room, string playerId)
    {
        if (room.State != RoomState.Waiting) return;

        var activePlayers = room.ActivePlayers.ToList();
        if (activePlayers.Count == 0) return;

        var pressing = activePlayers.Count(p => p.IsPressingButton);
        _logger.LogDebug("[Room {RoomId}] 玩家 {PlayerId} 按下按鈕，有效玩家按住 {Pressing}/{Total}",
            room.RoomId, playerId, pressing, activePlayers.Count);

        // 所有有效玩家都按住了
        if (activePlayers.All(p => p.IsPressingButton))
        {
            _logger.LogInformation("[Room {RoomId}] 所有有效玩家已按住，啟動 5 秒倒數", room.RoomId);
            room.State = RoomState.Countdown;
            room.CountdownCts?.Cancel();
            room.CountdownCts = new CancellationTokenSource();
            room.CountdownStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var cts = room.CountdownCts;

            await _hubContext.Clients.Group(room.RoomId)
                .SendAsync("CountdownStarted");

            _ = RunCountdown(room, cts);
        }
    }

    private async Task RunCountdown(Room room, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(5000, cts.Token);

            // 倒數完成，開始回合
            if (room.State != RoomState.Countdown) return;

            // 安全檢查：若所有玩家均已放棄（皆在倒數中放開），直接結束回合
            if (!room.PressedActivePlayers.Any())
            {
                _logger.LogInformation("[Room {RoomId}] 倒數結束但所有玩家均已放棄，直接結束回合",
                    room.RoomId);
                await EndRound(room, new List<Player>());
                return;
            }

            room.State = RoomState.InProgress;
            room.RoundStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _logger.LogInformation("[Room {RoomId}] 回合 {Round} 正式開始，RoundStartTime={StartTime}",
                room.RoomId, room.CurrentRound, room.RoundStartTime);

            await _hubContext.Clients.Group(room.RoomId)
                .SendAsync("RoundStarted", new
                {
                    roundNumber = room.CurrentRound,
                    roundStartTime = room.RoundStartTime
                });
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("[Room {RoomId}] 倒數被取消", room.RoomId);
        }
    }

    // ── 按鈕事件 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 玩家放開按鈕：扣除時間、判定回合是否結束。
    /// </summary>
    public async Task HandleButtonRelease(Room room, string playerId)
    {
        if (!room.Players.TryGetValue(playerId, out var player)) return;

        player.IsPressingButton = false;

        // 倒數期間放開→ 2s 內取消倒數；2s 後則放棄本回合且倒數繼續
        if (room.State == RoomState.Countdown)
        {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - room.CountdownStartTime;

            if (elapsed < 2000)
            {
                _logger.LogInformation(
                    "[Room {RoomId}] 玩家 {PlayerName}({PlayerId}) 在倒數 {Elapsed}ms 放開（< 2s），取消倒數",
                    room.RoomId, player.Name, playerId, elapsed);
                room.CountdownCts?.Cancel();
                room.State = RoomState.Waiting;
                await _hubContext.Clients.Group(room.RoomId).SendAsync("CountdownCancelled");
            }
            else
            {
                player.IsSittingOutRound = true;
                _logger.LogInformation(
                    "[Room {RoomId}] 玩家 {PlayerName}({PlayerId}) 在倒數 {Elapsed}ms 放開（≥ 2s），放棄本回合",
                    room.RoomId, player.Name, playerId, elapsed);

                // 僅通知該玩家本回合放棄
                await _hubContext.Clients.Client(player.ConnectionId)
                    .SendAsync("SittingOutRound");

                // 若所有有效玩家均已放棄本回合，立即取消倒數並結束回合
                if (!room.PressedActivePlayers.Any())
                {
                    _logger.LogInformation("[Room {RoomId}] 所有玩家已放棄本回合，取消倒數，結束回合",
                        room.RoomId);
                    room.CountdownCts?.Cancel();
                    await EndRound(room, new List<Player>());
                }
            }
            return;
        }

        if (room.State != RoomState.InProgress) return;

        // 扣除玩家時間
        if (player.IsActive)
        {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - room.RoundStartTime;
            var before = player.RemainingTimeMs;
            player.RemainingTimeMs = Math.Max(0, player.RemainingTimeMs - elapsed);

            _logger.LogInformation(
                "[Room {RoomId}] 玩家 {PlayerName}({PlayerId}) 放開按鈕，耗時 {Elapsed}ms，剩餘 {Before}ms → {After}ms",
                room.RoomId, player.Name, playerId, elapsed, before, player.RemainingTimeMs);

            room.LastReleasedActivePlayer = player;

            await _hubContext.Clients.Group(room.RoomId)
                .SendAsync("PlayerTimeUpdated", new
                {
                    playerId = player.PlayerId,
                    remainingTimeMs = player.RemainingTimeMs
                });
        }
        else
        {
            _logger.LogDebug("[Room {RoomId}] 玩家 {PlayerName}({PlayerId}) 放開按鈕（時間已歸零，無效）",
                room.RoomId, player.Name, playerId);
        }

        await CheckRoundEnd(room);
    }

    // ── 斷線處理 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 玩家斷線：視為放開按鈕，從房間移除，並觸發後續判定。
    /// 若為房主，解散房間。
    /// </summary>
    public async Task HandleDisconnect(string connectionId)
    {
        var (disbanded, room) = _roomService.LeaveRoom(string.Empty, connectionId);

        if (room == null)
        {
            _logger.LogDebug("[Disconnect] ConnectionId={ConnectionId} 不在任何房間，忽略", connectionId);
            return;
        }

        if (disbanded)
        {
            _logger.LogWarning("[Room {RoomId}] 房主斷線（ConnectionId={ConnectionId}），房間已解散",
                room.RoomId, connectionId);
            await _hubContext.Clients.Group(room.RoomId)
                .SendAsync("RoomDisbanded");

            await _hubContext.Clients.Group(room.RoomId)
                .SendAsync("RoomListUpdated", _roomService.GetAllRooms());
            return;
        }

        _logger.LogInformation("[Room {RoomId}] 玩家斷線（ConnectionId={ConnectionId}），State={State}",
            room.RoomId, connectionId, room.State);

        if (room.State == RoomState.Countdown)
        {
            _logger.LogInformation("[Room {RoomId}] 玩家斷線於倒數中，取消倒數", room.RoomId);
            room.CountdownCts?.Cancel();
            room.State = RoomState.Waiting;
            await _hubContext.Clients.Group(room.RoomId)
                .SendAsync("CountdownCancelled");
        }
        else if (room.State == RoomState.InProgress)
        {
            _logger.LogInformation("[Room {RoomId}] 玩家斷線於回合進行中，觸發回合結束判定", room.RoomId);
            await CheckRoundEnd(room);
        }

        await _hubContext.Clients.Group(room.RoomId)
            .SendAsync("RoomListUpdated", _roomService.GetAllRooms());
    }

    // ── 回合結束判定 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 判斷回合是否應結束：所有有效玩家均已放開按鈕。
    /// </summary>
    public async Task CheckRoundEnd(Room room)
    {
        if (room.State != RoomState.InProgress) return;

        var stillPressing = room.PressedActivePlayers.ToList();

        _logger.LogDebug("[Room {RoomId}] 回合結束判定：仍按住 {Count} 人",
            room.RoomId, stillPressing.Count);

        // 還有玩家按住 → 繼續
        if (stillPressing.Count >= 1) return;

        // 0 人按住 → 最後放開按鈕的有效玩家為勝者（時間已在放開時扣除）
        var roundWinners = room.LastReleasedActivePlayer != null
            ? new List<Player> { room.LastReleasedActivePlayer }
            : new List<Player>();

        await EndRound(room, roundWinners, skipWinnerTimeDeduction: true);
    }

    private async Task EndRound(Room room, List<Player> winners, bool skipWinnerTimeDeduction = false)
    {
        room.State = RoomState.RoundEnd;

        // 競標規則：勝者同樣需扣除本回合持續按住的時間
        // （若勝者已在放開按鈕時扣除過，則跳過）
        if (!skipWinnerTimeDeduction && winners.Count > 0 && room.RoundStartTime > 0)
        {
            var winElapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - room.RoundStartTime;
            foreach (var w in winners)
            {
                if (w.IsActive)
                {
                    var before = w.RemainingTimeMs;
                    w.RemainingTimeMs = Math.Max(0, w.RemainingTimeMs - winElapsed);
                    _logger.LogInformation(
                        "[Room {RoomId}] 勝者 {PlayerName} 競標時間扣除：{Before}ms → {After}ms（耗時 {WinElapsed}ms）",
                        room.RoomId, w.Name, before, w.RemainingTimeMs, winElapsed);
                    await _hubContext.Clients.Group(room.RoomId)
                        .SendAsync("PlayerTimeUpdated", new
                        {
                            playerId = w.PlayerId,
                            remainingTimeMs = w.RemainingTimeMs
                        });
                }
            }
        }

        // 給勝者加分
        foreach (var w in winners)
            w.Score++;

        var winnerIds = winners.Select(w => w.PlayerId).ToList();
        var winnerNames = winners.Select(w => w.Name).ToList();

        if (winners.Count == 0)
            _logger.LogInformation("[Room {RoomId}] 回合 {Round} 結束：平手，無人得分",
                room.RoomId, room.CurrentRound);
        else
            _logger.LogInformation("[Room {RoomId}] 回合 {Round} 結束：勝者 {Winners}",
                room.RoomId, room.CurrentRound, string.Join(", ", winnerNames));

        await _hubContext.Clients.Group(room.RoomId)
            .SendAsync("RoundEnded", new
            {
                winnerIds,
                winnerNames,
                scores = BuildScoreEntries(room)
            });

        // 所有玩家時間歸零 → 直接結束遊戲
        if (!room.ActivePlayers.Any())
        {
            _logger.LogInformation("[Room {RoomId}] 所有玩家時間歸零，提前結束遊戲", room.RoomId);
            await EndGame(room);
            return;
        }

        // 所有回合結束 → 結束遊戲
        if (room.CurrentRound >= room.TotalRounds)
        {
            _logger.LogInformation("[Room {RoomId}] 已達 {Total} 回合上限，遊戲結束",
                room.RoomId, room.TotalRounds);
            await EndGame(room);
        }
    }

    public async Task EndGame(Room room)
    {
        room.State = RoomState.GameEnd;

        var top = BuildScoreEntries(room).FirstOrDefault();
        _logger.LogInformation("[Room {RoomId}] 遊戲結束，最高分：{Winner} {Score} 分",
            room.RoomId, top?.Name ?? "無", top?.Score ?? 0);

        await _hubContext.Clients.Group(room.RoomId)
            .SendAsync("GameEnded", new
            {
                finalScores = BuildScoreEntries(room)
            });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<ScoreEntry> BuildScoreEntries(Room room) =>
        room.Players.Values
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.RemainingTimeMs)
            .Select(p => new ScoreEntry
            {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Score = p.Score,
                RemainingTimeMs = p.RemainingTimeMs
            })
            .ToList();
}
