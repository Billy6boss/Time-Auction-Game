'use strict';

(function () {
    // ── 讀取頁面資料 ──────────────────────────────────────────────────────────
    const app           = document.getElementById('room-app');
    const roomId        = app.dataset.roomId;       // 建立模式時為空字串
    const myPlayerId    = app.dataset.playerId;
    const myPlayerName  = app.dataset.playerName;
    let   maxTimeMs     = parseInt(app.dataset.maxTimeMs, 10);  // 建立後更新

    // ── 建立 SignalR 連線（game.js 共用） ─────────────────────────────────────
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/gamehub')
        .withAutomaticReconnect()
        .build();

    // 供 game.js 使用
    window.hubConnection = connection;

    // ── 可變共享狀態（game.js 讀取） ──────────────────────────────────────────
    window.roomState = {
        isHost: false,
        myPlayerId: myPlayerId,
        currentPlayers: {}   // playerId → PlayerInfo
    };

    // ── Section 切換（game.js 也會使用） ─────────────────────────────────────
    const allSectionIds = [
        'section-loading', 'section-error', 'section-waiting',
        'section-game', 'section-round-end', 'section-game-end'
    ];
    window.showSection = function (sectionId) {
        allSectionIds.forEach(function (id) {
            document.getElementById(id).classList.add('d-none');
        });
        document.getElementById(sectionId).classList.remove('d-none');
    };

    // ── DOM refs ──────────────────────────────────────────────────────────────
    const waitingPlayerList = document.getElementById('waiting-player-list');
    const playerCountBadge  = document.getElementById('player-count-badge');
    const startGameBtn      = document.getElementById('start-game-btn');
    const leaveRoomBtn      = document.getElementById('leave-room-btn');
    const guestHint         = document.getElementById('guest-hint');
    const errorMessage      = document.getElementById('error-message');

    // ── SignalR 事件 ──────────────────────────────────────────────────────────

    /**
     * 建立房間成功（建立模式專用）：更新 URL、填充初始 UI，視同房主加入。
     * 資料：{ roomId, roomName, maxTimeMinutes, totalRounds }
     */
    connection.on('RoomCreated', function (room) {
        // 更新瀏覽器網址列（不重載頁面）
        history.replaceState(null, '', '/Room/' + room.roomId);
        document.title = room.roomName + ' - 時間拍賣';

        // 更新初始時間（renderWaitingPlayerList 會用到）
        maxTimeMs = room.maxTimeMinutes * 60 * 1000;
        app.dataset.roomId = room.roomId;

        // 更新房間資訊顯示元素（create mode 時為空值）
        document.getElementById('room-title').textContent       = room.roomName;
        document.getElementById('room-id-badge').textContent    = room.roomId;
        document.getElementById('room-max-time').textContent    = room.maxTimeMinutes;
        document.getElementById('room-total-rounds').textContent = room.totalRounds;

        // 設為房主，將自己加入玩家列表
        window.roomState.isHost = true;
        window.roomState.currentPlayers = {};
        window.roomState.currentPlayers[myPlayerId] = {
            playerId:        myPlayerId,
            name:            myPlayerName,
            remainingTimeMs: maxTimeMs,
            score:           0,
            isActive:        true
        };

        startGameBtn.classList.remove('d-none');
        startGameBtn.disabled = true;   // 還沒有第二位玩家
        guestHint.classList.add('d-none');
        renderWaitingPlayerList();
        window.showSection('section-waiting');
    });

    /** 加入成功：收到房間完整快照 */
    connection.on('RoomJoined', function (room) {
        window.roomState.isHost = room.isHost;
        window.roomState.currentPlayers = {};
        room.players.forEach(function (p) {
            window.roomState.currentPlayers[p.playerId] = p;
        });

        if (room.isHost) {
            startGameBtn.classList.remove('d-none');
            guestHint.classList.add('d-none');
        } else {
            startGameBtn.classList.add('d-none');
            guestHint.classList.remove('d-none');
        }

        renderWaitingPlayerList();
        window.showSection('section-waiting');
    });

    /** 有新玩家加入 */
    connection.on('PlayerJoined', function (player) {
        window.roomState.currentPlayers[player.playerId] = player;
        renderWaitingPlayerList();
    });

    /** 有玩家離開 */
    connection.on('PlayerLeft', function (playerId) {
        delete window.roomState.currentPlayers[playerId];
        renderWaitingPlayerList();
    });

    /** 房間解散（房主斷線） */
    connection.on('RoomDisbanded', function () {
        alert('房主已離開，房間解散');
        window.location.href = '/Lobby';
    });

    /** Hub 錯誤訊息 */
    connection.on('Error', function (msg) {
        errorMessage.textContent = msg;
        window.showSection('section-error');
    });

    // ── 按鈕事件 ──────────────────────────────────────────────────────────────

    /** 房主：開始遊戲 */
    startGameBtn.addEventListener('click', function () {
        startGameBtn.disabled = true;
        connection.invoke('StartGame').catch(function (err) {
            startGameBtn.disabled = false;
            console.error('[room] StartGame 失敗：', err);
        });
    });

    /** 離開房間 */
    leaveRoomBtn.addEventListener('click', function () {
        leaveRoomBtn.disabled = true;
        connection.invoke('LeaveRoom')
            .finally(function () {
                window.location.href = '/Lobby';
            });
    });

    // ── 渲染等待室玩家列表 ────────────────────────────────────────────────────
    function renderWaitingPlayerList() {
        const players = Object.values(window.roomState.currentPlayers);
        playerCountBadge.textContent = players.length;
        waitingPlayerList.innerHTML = '';

        players.forEach(function (p) {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center';
            li.dataset.playerId = p.playerId;
            const isMe = p.playerId === myPlayerId;
            li.innerHTML = `
                <span>
                    ${esc(p.name)}
                    ${isMe ? '<span class="badge bg-primary ms-1">我</span>' : ''}
                </span>
                <span class="text-muted small font-monospace">${msToDisplay(maxTimeMs)}</span>`;
            waitingPlayerList.appendChild(li);
        });

        // 至少要 1 人才能讓房主開始（防呆）
        if (startGameBtn) {
            startGameBtn.disabled = players.length < 1;
        }
    }

    // ── 連線並建立或加入房間 ──────────────────────────────────────────────────
    connection.start()
        .then(function () {
            if (!roomId) {
                // 建立模式：從 sessionStorage 讀取待建立的設定
                const pendingStr = sessionStorage.getItem('pendingCreate');
                if (!pendingStr) {
                    // sessionStorage 沒有設定 → 直接回大廳
                    window.location.href = '/Lobby';
                    return;
                }
                sessionStorage.removeItem('pendingCreate');
                const p = JSON.parse(pendingStr);
                return connection.invoke('CreateRoom', p.name, p.maxTime, p.rounds);
            }
            return connection.invoke('JoinRoom', roomId);
        })
        .catch(function (err) {
            console.error('[room] SignalR 連線失敗：', err);
            errorMessage.textContent = '無法連線至伺服器，請重新整理頁面';
            window.showSection('section-error');
        });

    // ── 工具函數 ──────────────────────────────────────────────────────────────
    /** ms → MM:SS */
    function msToDisplay(ms) {
        const totalSec = Math.max(0, Math.floor(ms / 1000));
        const m = Math.floor(totalSec / 60);
        const s = totalSec % 60;
        return `${pad2(m)}:${pad2(s)}`;
    }
    function pad2(n) { return String(n).padStart(2, '0'); }
    function esc(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }
}());
