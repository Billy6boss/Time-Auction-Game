// lobby.js - 大廳頁面邏輯
const player = getPlayerInfo();
if (!player.id || !player.name) {
    window.location.href = '/';
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/gamehub')
    .withAutomaticReconnect()
    .build();

// ─── UI Elements ─────────────────────────────────────────
const roomListEl = document.getElementById('roomList');
const createRoomBtn = document.getElementById('createRoomBtn');
const roomNameInput = document.getElementById('roomName');
const initialTimeSelect = document.getElementById('initialTime');
const totalRoundsSelect = document.getElementById('totalRounds');

// ─── SignalR Events ──────────────────────────────────────
connection.on('RoomListUpdated', (rooms) => {
    renderRoomList(rooms);
});

connection.on('RoomCreated', (room) => {
    window.location.href = `/Room/${room.id}`;
});

connection.on('Error', (msg) => {
    alert(msg);
});

// ─── Actions ─────────────────────────────────────────────
createRoomBtn.addEventListener('click', () => {
    const name = roomNameInput.value.trim();
    if (!name) {
        alert('請輸入房間名稱');
        return;
    }

    const initialTime = parseInt(initialTimeSelect.value);
    const totalRounds = parseInt(totalRoundsSelect.value);

    connection.invoke('CreateRoom', name, player.name, player.id, initialTime, totalRounds)
        .catch(err => console.error('CreateRoom error:', err));
});

// ─── Rendering ───────────────────────────────────────────
function renderRoomList(rooms) {
    if (!rooms || rooms.length === 0) {
        roomListEl.innerHTML = '<div class="alert alert-secondary">目前沒有房間，建立一個吧！</div>';
        return;
    }

    let html = '<div class="list-group">';
    for (const room of rooms) {
        const playerCount = room.players ? room.players.length : 0;
        const stateText = getStateText(room.state);
        const canJoin = room.state === 'Waiting';

        html += `
            <div class="list-group-item d-flex justify-content-between align-items-center">
                <div>
                    <h6 class="mb-1">${escapeHtml(room.name)}</h6>
                    <small class="text-muted">
                        ${room.initialTimeMinutes} 分鐘 / ${room.totalRounds} 回合 /
                        ${playerCount} 位玩家 /
                        <span class="badge bg-${canJoin ? 'success' : 'secondary'}">${stateText}</span>
                    </small>
                </div>
                ${canJoin
                    ? `<button class="btn btn-primary btn-sm" onclick="joinRoom('${room.id}')">加入</button>`
                    : '<span class="text-muted">遊戲中</span>'}
            </div>`;
    }
    html += '</div>';
    roomListEl.innerHTML = html;
}

function joinRoom(roomId) {
    window.location.href = `/Room/${roomId}`;
}

function getStateText(state) {
    const map = {
        'Waiting': '等待中',
        'Countdown': '倒數中',
        'Playing': '遊戲中',
        'RoundResult': '回合結算',
        'GameOver': '已結束'
    };
    return map[state] || state;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ─── Start Connection ────────────────────────────────────
connection.start()
    .then(() => {
        console.log('SignalR connected');
        connection.invoke('JoinLobby');
    })
    .catch(err => {
        console.error('SignalR connection error:', err);
        roomListEl.innerHTML = '<div class="alert alert-danger">連線失敗，請重新整理頁面</div>';
    });
