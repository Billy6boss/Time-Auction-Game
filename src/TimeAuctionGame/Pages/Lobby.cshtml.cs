using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeAuctionGame.Models;
using TimeAuctionGame.Services;

namespace TimeAuctionGame.Pages;

public class LobbyModel : PageModel
{
    private readonly RoomService _roomService;

    public IEnumerable<RoomSummary> Rooms { get; private set; } = [];

    public LobbyModel(RoomService roomService)
    {
        _roomService = roomService;
    }

    public IActionResult OnGet()
    {
        var playerId = Request.Cookies["PlayerId"];
        var playerName = Request.Cookies["PlayerName"];

        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(playerName))
            return RedirectToPage("/Index");

        Rooms = _roomService.GetAllRooms();
        return Page();
    }
}
