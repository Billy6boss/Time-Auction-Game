using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TimeAuctionGame.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        // If already logged in, redirect to lobby
        if (Request.Cookies.ContainsKey("PlayerName"))
        {
            return RedirectToPage("/Lobby");
        }
        return Page();
    }

    public IActionResult OnPost(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return Page();
        }

        var playerId = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions
        {
            HttpOnly = false, // JS needs to read it
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            SameSite = SameSiteMode.Lax
        };

        Response.Cookies.Append("PlayerId", playerId, cookieOptions);
        Response.Cookies.Append("PlayerName", playerName.Trim(), cookieOptions);

        return RedirectToPage("/Lobby");
    }
}
