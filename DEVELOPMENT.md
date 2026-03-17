# Time-Auction-Game 開發計畫

## SignalR 事件文件

### Client → Server（Hub Methods）

| 方法名稱 | 參數 | 說明 |
|---|---|---|
| `CreateRoom` | `roomName: string, maxTimeMinutes: int, totalRounds: int` | 創建房間 |
| `JoinRoom` | `roomId: string` | 加入房間（6碼短碼） |
| `LeaveRoom` | — | 離開房間 |
| `StartGame` | — | 開始遊戲（僅限房主） |
| `PressButton` | — | 按下按鈕 |
| `ReleaseButton` | — | 放開按鈕 |

### Server → Client（Broadcast Events）

| 事件名稱 | 參數 | 說明 |
|---|---|---|
| `RoomListUpdated` | `rooms: RoomSummary[]` | 大廳房間列表更新 |
| `PlayerJoined` | `player: PlayerInfo` | 有玩家加入房間 |
| `PlayerLeft` | `playerId: string` | 有玩家離開房間 |
| `RoomDisbanded` | — | 房間解散（房主離線） |
| `CountdownStarted` | — | 3 秒倒數開始 |
| `CountdownCancelled` | — | 倒數中斷（有玩家放開按鈕） |
| `RoundStarted` | `roundNumber: int, roundStartTime: long`（Unix ms）| 回合開始 |
| `RoundEnded` | `winnerIds: string[], winnerName: string, scores: ScoreEntry[]` | 回合結束（平手時 winnerIds 有多人） |
| `PlayerTimeUpdated` | `playerId: string, remainingTimeMs: long` | 玩家剩餘時間更新 |
| `GameEnded` | `finalScores: ScoreEntry[]` | 遊戲結束，最終排名 |

---

## 開發任務

> 建議開發順序：一 → 二 → 三 → 四 → 五（Index + Lobby）→ 六（lobby.js）→ 五（Room）→ 六（room.js + game.js）→ 七 → 八

---

### 一、專案基礎建設

- [ ] 新增 `TimeAuctionGame.csproj`（`Microsoft.NET.Sdk.Web`，目標 .NET 10）
- [ ] 新增 `Program.cs`，設定 Razor Pages、SignalR、Session、Cookie 中介層
- [ ] 新增 `Pages/Shared/_Layout.cshtml`，引入 Bootstrap、DSEG7 字型、全域 CSS
- [ ] 新增 `appsettings.json` / `appsettings.Development.json`

---

### 二、資料模型

- [ ] 新增 `Models/Player.cs`
  - 欄位：`PlayerId`、`ConnectionId`、`Name`、`RemainingTimeMs`、`Score`、`IsPressingButton`、`IsActive`（時間是否歸零）
- [ ] 新增 `Models/Room.cs`
  - 欄位：`RoomId`（6碼短碼）、`RoomName`、`HostConnectionId`、`Players`、`MaxTimeMinutes`、`TotalRounds`、`CurrentRound`、`State`、`RoundStartTime`
- [ ] 新增 `Models/RoomState.cs`（Enum）
  - 值：`Waiting`、`Countdown`、`InProgress`、`RoundEnd`、`GameEnd`

---

### 三、後端服務

- [ ] 新增 `Services/RoomService.cs`（Singleton）
  - `CreateRoom`：驗證名稱、生成 6 碼短碼、建立房間
  - `JoinRoom`：驗證房間存在、人數上限（30人）、狀態是否允許加入
  - `LeaveRoom`：移除玩家，若為房主則解散房間
  - `GetAllRooms`：回傳所有房間摘要（供大廳顯示）
  - `GetRoom`：以短碼查詢房間
- [ ] 新增 `Services/GameService.cs`（Singleton）
  - `StartRound`：初始化回合，廣播倒數開始
  - `HandleButtonRelease`：記錄放開時間、扣除玩家剩餘時間、判定回合結束
  - `HandleDisconnect`：玩家斷線處理（視為放開按鈕）
  - `CheckRoundEnd`：判斷是否只剩一人按住（或全部放開）
  - `EndRound`：結算勝負、廣播結果、更新分數
  - `EndGame`：廣播最終結果

---

### 四、SignalR Hub

- [ ] 新增 `Hubs/GameHub.cs`，映射路徑 `/gamehub`
  - **連線管理**：`OnConnectedAsync`（從 Cookie 載入玩家資訊）、`OnDisconnectedAsync`（呼叫 `HandleDisconnect`）
  - **大廳**：`CreateRoom`、`JoinRoom`（6碼短碼）；廣播 `RoomListUpdated`
  - **房間**：`LeaveRoom`；廣播 `PlayerJoined` / `PlayerLeft` / `RoomDisbanded`
  - **遊戲**：`StartGame`（僅限房主）、`PressButton`、`ReleaseButton`
  - **廣播事件**：`CountdownStarted`、`CountdownCancelled`、`RoundStarted`、`RoundEnded`、`PlayerTimeUpdated`、`GameEnded`

---

### 五、Razor Pages

- [ ] 新增 `Pages/Index.cshtml` + `Index.cshtml.cs`
  - 無 Cookie 時顯示輸入姓名 UI；寫入 Cookie 後 Redirect 至 `/Lobby`
  - 有 Cookie 直接 Redirect 至 `/Lobby`
- [ ] 新增 `Pages/Lobby.cshtml` + `Lobby.cshtml.cs`
  - 顯示所有房間列表（卡片格式：房間名、人數、狀態、加入按鈕）
  - 創建房間 Modal（設定房間名稱、初始時間、回合數）
  - 輸入 6 碼短碼手動加入房間的輸入框
  - SignalR 即時更新房間列表
- [ ] 新增 `Pages/Room.cshtml` + `Room.cshtml.cs`
  - 單一頁面，依 `RoomState` 切換顯示區塊：
    - `Waiting`：玩家列表、房間設定資訊、（房主顯示）開始遊戲按鈕
    - `Countdown` / `InProgress`：大按鈕 + 七段顯示器
    - `RoundEnd`：本回合結果 + 目前分數板 + （房主顯示）下一回合按鈕
    - `GameEnd`：最終排名 + 分數 + （房主顯示）再玩一次按鈕

---

### 六、前端 JavaScript

- [ ] 新增 `wwwroot/js/lobby.js`
  - SignalR 連線至 `/gamehub`
  - 處理 `RoomListUpdated` → 重新渲染房間列表
  - 觸發 `CreateRoom`、`JoinRoom`（短碼輸入框 + 列表按鈕兩種方式）
- [ ] 新增 `wwwroot/js/room.js`
  - 進入房間時呼叫 `JoinRoom`
  - 處理 `PlayerJoined` / `PlayerLeft` / `RoomDisbanded` → 更新玩家列表
  - 房主顯示開始按鈕，觸發 `StartGame`
- [ ] 新增 `wwwroot/js/game.js`
  - 監聽 `mousedown` / `mouseup` / `touchstart` / `touchend` → 觸發 `PressButton` / `ReleaseButton`
  - 收到 `CountdownStarted` → 七段顯示器倒數 3、2、1
  - 收到 `CountdownCancelled` → 重置倒數顯示
  - 收到 `RoundStarted(roundStartTime)` → Client 端 `setInterval` 正向計時（`Date.now() - roundStartTime`）
  - 收到 `RoundEnded` → 停止計時、顯示結果
  - 收到 `PlayerTimeUpdated` → 更新玩家剩餘時間顯示
  - 收到 `GameEnded` → 顯示最終排名

---

### 七、樣式

- [ ] 新增 `wwwroot/css/site.css`：全域樣式、大廳卡片、玩家列表
- [ ] 新增 `wwwroot/css/game.css`：大按鈕樣式（按下/放開狀態）、七段顯示器字型套用（DSEG7 Classic）、各遊戲狀態的版面配置
- [ ] 在 `Pages/Shared/_Layout.cshtml` 引入 DSEG7 Classic Web Font

---

### 八、部署

- [ ] 新增 `Dockerfile`（multi-stage build：`sdk:10` build → `aspnet:10` runtime）
- [ ] 新增 `.dockerignore`
- [ ] 在 Render 設定 Web Service，環境變數設定 `ASPNETCORE_ENVIRONMENT=Production`
