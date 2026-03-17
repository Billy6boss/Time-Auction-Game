using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeAuctionGame.Services;

namespace TimeAuctionGame.Pages;

public class RoomModel : PageModel
{
    private readonly RoomService _roomService;

    public string RoomId { get; private set; } = string.Empty;
    public string RoomName { get; private set; } = string.Empty;
    public int MaxTimeMinutes { get; private set; }
    public int TotalRounds { get; private set; }
    public string PlayerId { get; private set; } = string.Empty;
    public string PlayerName { get; private set; } = string.Empty;

    public RoomModel(RoomService roomService)
    {
        _roomService = roomService;
    }

    public IActionResult OnGet(string? roomId)
    {
        var playerId = Request.Cookies["PlayerId"];
        var playerName = Request.Cookies["PlayerName"];

        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(playerName))
            return RedirectToPage("/Index");

        PlayerId = playerId;
        PlayerName = playerName;

        // 建立模式：roomId 為空，room.js 會從 sessionStorage 讀取設定後呼叫 CreateRoom
        if (string.IsNullOrEmpty(roomId))
            return Page();

        var room = _roomService.GetRoom(roomId);
        if (room == null)
        {
            TempData["Error"] = "找不到該房間";
            return RedirectToPage("/Lobby");
        }

        RoomId = room.RoomId;
        RoomName = room.RoomName;
        MaxTimeMinutes = room.MaxTimeMinutes;
        TotalRounds = room.TotalRounds;

        return Page();
    }
}
