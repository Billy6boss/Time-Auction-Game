using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TimeAuctionGame.Pages;

public class IndexModel : PageModel
{
    [BindProperty]
    public string PlayerName { get; set; } = string.Empty;

    public IActionResult OnGet()
    {
        var existingId = Request.Cookies["PlayerId"];
        var existingName = Request.Cookies["PlayerName"];

        if (!string.IsNullOrEmpty(existingId) && !string.IsNullOrEmpty(existingName))
            return RedirectToPage("/Lobby");

        return Page();
    }

    public IActionResult OnPost()
    {
        var trimmed = PlayerName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmed))
        {
            ModelState.AddModelError(nameof(PlayerName), "請輸入玩家名稱");
            return Page();
        }

        if (trimmed.Length > 20)
        {
            ModelState.AddModelError(nameof(PlayerName), "名稱最多 20 字");
            return Page();
        }

        var options = new CookieOptions
        {
            HttpOnly = false,       // 允許 JavaScript 讀取（_Layout.cshtml 的 getPlayerInfo()）
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(1)
        };

        Response.Cookies.Append("PlayerId", Guid.NewGuid().ToString(), options);
        Response.Cookies.Append("PlayerName", trimmed, options);

        return RedirectToPage("/Lobby");
    }
}
