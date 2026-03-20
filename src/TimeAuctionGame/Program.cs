using TimeAuctionGame.Hubs;
using TimeAuctionGame.Services;

var builder = WebApplication.CreateBuilder(args);

// 停用設定檔的 inotify 監聽，避免在資源受限的主機（如 Render 免費方案）
// 因 inotify 實例數量耗盡而造成 IOException 啟動失敗。
// 生產環境不需要 hot-reload 設定，停用此功能不影響正常運作。
foreach (var source in builder.Configuration.Sources
    .OfType<Microsoft.Extensions.Configuration.FileConfigurationSource>())
{
    source.ReloadOnChange = false;
}

// Add services
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

// Session（玩家資訊暫存用）
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<GameService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapHub<GameHub>("/gamehub");

app.Run();
