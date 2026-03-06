// room.js - 房間等待室邏輯
const player = getPlayerInfo();
if (!player.id || !player.name) {
    window.location.href = '/';
}

const roomId = document.getElementById('roomId').value;
let currentRoom = null;
let hasJoined = false;

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/gamehub')
    .withAutomaticReconnect()
    .build();

// ─── UI Elements ─────────────────────────────────────────
const roomTitle = document.getElementById('roomTitle');
const roomTime = document.getElementById('roomTime');
const roomRounds = document.getElementById('roomRounds');
const currentRound = document.getElementById('currentRound');
const playerList = document.getElementById('playerList');
const hostControls = document.getElementById('hostControls');
const startGameBtn = document.getElementById('startGameBtn');
const nextRoundBtn = document.getElementById('nextRoundBtn');
const leaveRoomBtn = document.getElementById('leaveRoomBtn');
const roundResult = document.getElementById('roundResult');
const roundWinnerText = document.getElementById('roundWinnerText');
const gameOverResult = document.getElementById('gameOverResult');
const gameOverText = document.getElementById('gameOverText');

// ─── SignalR Events ──────────────────────────────────────
connection.on('RoomUpdated', (room) => {
    currentRoom = room;
    renderRoom(room);
});

connection.on('GameStarted', (room) => {
    // Navigate to game page
    window.location.href = `/Game/${roomId}`;
});

connection.on('NewRound', (room) => {
    window.location.href = `/Game/${roomId}`;
});

connection.on('RoundEnded', (winnerId, winnerName, room) => {
    currentRoom = room;
    renderRoom(room);
    roundResult.style.display = 'block';
    roundWinnerText.textContent = `🏆 第 ${room.currentRound} 回合勝者：${winnerName || '無'}`;

    if (room.hostPlayerId === player.id) {
        startGameBtn.style.display = 'none';
        nextRoundBtn.style.display = 'inline-block';
    }
});

connection.on('GameOver', (winnerId, winnerName, room) => {
    currentRoom = room;
    renderRoom(room);
    gameOverResult.style.display = 'block';
    gameOverText.textContent = `🎉 遊戲結束！最終勝者：${winnerName || '無人獲勝'}`;
    startGameBtn.style.display = 'none';
    nextRoundBtn.style.display = 'none';
});

connection.on('Error', (msg) => {
    alert(msg);
    window.location.href = '/Lobby';
});

// ─── Actions ─────────────────────────────────────────────
startGameBtn.addEventListener('click', () => {
    connection.invoke('StartGame', roomId, player.id)
        .catch(err => console.error('StartGame error:', err));
});

nextRoundBtn.addEventListener('click', () => {
    connection.invoke('StartNextRound', roomId, player.id)
        .catch(err => console.error('StartNextRound error:', err));
});

leaveRoomBtn.addEventListener('click', () => {
    connection.invoke('LeaveRoom', roomId, player.id)
        .then(() => {
            window.location.href = '/Lobby';
        })
        .catch(err => console.error('LeaveRoom error:', err));
});

// ─── Rendering ───────────────────────────────────────────
function renderRoom(room) {
    roomTitle.textContent = `🏠 ${room.name}`;
    roomTime.textContent = room.initialTimeMinutes;
    roomRounds.textContent = room.totalRounds;
    currentRound.textContent = room.currentRound;

    // Player list
    let html = '';
    for (const p of room.players) {
        const isHost = p.id === room.hostPlayerId;
        const isMe = p.id === player.id;
        html += `
            <div class="d-flex justify-content-between align-items-center border-bottom py-2">
                <div>
                    ${isHost ? '👑' : '👤'}
                    <strong>${escapeHtml(p.name)}</strong>
                    ${isMe ? '<span class="badge bg-primary ms-1">你</span>' : ''}
                </div>
                <div>
                    <span class="badge bg-info">⏱ ${formatTime(p.remainingTimeSeconds)}</span>
                    <span class="badge bg-warning text-dark ms-1">🏆 ${p.score}</span>
                </div>
            </div>`;
    }
    playerList.innerHTML = html;

    // Host controls
    if (room.hostPlayerId === player.id && room.state === 'Waiting' && room.currentRound === 0) {
        hostControls.style.display = 'block';
        startGameBtn.style.display = 'inline-block';
        nextRoundBtn.style.display = 'none';
    } else if (room.hostPlayerId === player.id && room.state === 'RoundResult') {
        hostControls.style.display = 'block';
        startGameBtn.style.display = 'none';
        nextRoundBtn.style.display = 'inline-block';
    } else if (room.hostPlayerId !== player.id) {
        hostControls.style.display = 'none';
    }
}

function formatTime(totalSeconds) {
    const mins = Math.floor(totalSeconds / 60);
    const secs = Math.floor(totalSeconds % 60);
    return `${mins}:${secs.toString().padStart(2, '0')}`;
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
        // Join the room
        connection.invoke('JoinRoom', roomId, player.name, player.id)
            .then(() => {
                hasJoined = true;
            })
            .catch(err => {
                console.error('JoinRoom error:', err);
                window.location.href = '/Lobby';
            });
    })
    .catch(err => {
        console.error('SignalR connection error:', err);
    });

// Cleanup on page leave
window.addEventListener('beforeunload', () => {
    if (hasJoined) {
        connection.invoke('LeaveRoom', roomId, player.id).catch(() => {});
    }
});
