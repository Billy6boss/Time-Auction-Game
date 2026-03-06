using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TimeAuctionGame.Pages;

public class RoomModel : PageModel
{
    public string RoomId { get; set; } = string.Empty;

    public IActionResult OnGet(string roomId)
    {
        if (!Request.Cookies.ContainsKey("PlayerName"))
        {
            return RedirectToPage("/Index");
        }

        RoomId = roomId;
        return Page();
    }
}
