using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TimeAuctionGame.Pages;

public class LobbyModel : PageModel
{
    public IActionResult OnGet()
    {
        if (!Request.Cookies.ContainsKey("PlayerName"))
        {
            return RedirectToPage("/Index");
        }
        return Page();
    }
}
