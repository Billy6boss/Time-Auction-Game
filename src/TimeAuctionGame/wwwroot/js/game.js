// game.js - 遊戲畫面邏輯
const player = getPlayerInfo();
if (!player.id || !player.name) {
    window.location.href = '/';
}

const roomId = document.getElementById('roomId').value;

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/gamehub')
    .withAutomaticReconnect()
    .build();

// ─── State ───────────────────────────────────────────────
let isHolding = false;
let gameState = 'waiting'; // waiting, countdown, playing, finished
let countdownInterval = null;
let localTimerInterval = null;
let roundStartTimeMs = 0;
let currentRoom = null;

// ─── UI Elements ─────────────────────────────────────────
const gameButton = document.getElementById('gameButton');
const buttonText = gameButton.querySelector('.button-text');
const gameStatus = document.getElementById('gameStatus');
const playerInfoCards = document.getElementById('playerInfoCards');
const roundNum = document.getElementById('roundNum');
const totalRoundsEl = document.getElementById('totalRounds');
const myRemainingTime = document.getElementById('myRemainingTime');

// Digit elements
const digitM1 = document.getElementById('digit-m1');
const digitS1 = document.getElementById('digit-s1');
const digitS2 = document.getElementById('digit-s2');
const digitMs = document.getElementById('digit-ms');

// ─── SignalR Events ──────────────────────────────────────
connection.on('RoomUpdated', (room) => {
    currentRoom = room;
    updateRoomInfo(room);
});

connection.on('PlayerHolding', (playerId) => {
    highlightPlayer(playerId, true);
});

connection.on('PlayerNotHolding', (playerId) => {
    highlightPlayer(playerId, false);
});

connection.on('CountdownStarted', (countdownStartMs) => {
    gameState = 'countdown';
    gameStatus.textContent = '倒數中...';
    gameButton.classList.add('countdown');

    let count = 3;
    updateDisplay(count, 0, 0, 0);

    countdownInterval = setInterval(() => {
        count--;
        if (count > 0) {
            updateDisplay(count, 0, 0, 0);
        } else {
            clearInterval(countdownInterval);
            countdownInterval = null;
        }
    }, 1000);
});

connection.on('CountdownCancelled', (playerId) => {
    gameState = 'waiting';
    gameStatus.textContent = '有玩家放開了按鈕，等待所有玩家準備...';
    gameButton.classList.remove('countdown');

    if (countdownInterval) {
        clearInterval(countdownInterval);
        countdownInterval = null;
    }
    updateDisplay(0, 0, 0, 0);
});

connection.on('RoundStarted', (startTimeMs) => {
    gameState = 'playing';
    roundStartTimeMs = startTimeMs;
    gameStatus.textContent = '🔥 遊戲進行中！';
    gameButton.classList.remove('countdown');
    gameButton.classList.add('playing');

    // Start local timer for smooth display
    startLocalTimer();
});

connection.on('TimerUpdate', (elapsedSeconds) => {
    // Server sync - only use if local timer drifts significantly
    // For now we rely on local timer for smoothness
});

connection.on('PlayerReleased', (playerId, timeSpentSeconds) => {
    highlightPlayer(playerId, false);
    markPlayerReleased(playerId, timeSpentSeconds);
});

connection.on('RoundEnded', (winnerId, winnerName, room) => {
    gameState = 'finished';
    stopLocalTimer();

    gameButton.classList.remove('playing');
    gameButton.classList.add('finished');
    buttonText.textContent = '回合結束';
    gameStatus.textContent = `🏆 勝者：${winnerName || '無'}`;

    currentRoom = room;
    updateRoomInfo(room);

    // Redirect back to room after 3 seconds
    setTimeout(() => {
        window.location.href = `/Room/${roomId}`;
    }, 3000);
});

connection.on('GameOver', (winnerId, winnerName, room) => {
    gameState = 'finished';
    stopLocalTimer();
    gameStatus.textContent = `🎉 遊戲結束！最終勝者：${winnerName || '無'}`;

    setTimeout(() => {
        window.location.href = `/Room/${roomId}`;
    }, 5000);
});

connection.on('Error', (msg) => {
    alert(msg);
});

// ─── Button Events ───────────────────────────────────────
function onButtonDown(e) {
    e.preventDefault();
    if (gameState === 'finished') return;
    if (isHolding) return;

    isHolding = true;
    gameButton.classList.add('holding');
    buttonText.textContent = '按住中...';

    connection.invoke('ButtonDown', roomId, player.id)
        .catch(err => console.error('ButtonDown error:', err));
}

function onButtonUp(e) {
    e.preventDefault();
    if (!isHolding) return;

    isHolding = false;
    gameButton.classList.remove('holding');
    buttonText.textContent = '按住';

    const clientTimestampMs = Date.now();
    connection.invoke('ButtonUp', roomId, player.id, clientTimestampMs)
        .catch(err => console.error('ButtonUp error:', err));
}

// Mouse events
gameButton.addEventListener('mousedown', onButtonDown);
gameButton.addEventListener('mouseup', onButtonUp);
gameButton.addEventListener('mouseleave', (e) => {
    if (isHolding) onButtonUp(e);
});

// Touch events (mobile)
gameButton.addEventListener('touchstart', onButtonDown, { passive: false });
gameButton.addEventListener('touchend', onButtonUp, { passive: false });
gameButton.addEventListener('touchcancel', onButtonUp, { passive: false });

// Prevent context menu on long press
gameButton.addEventListener('contextmenu', (e) => e.preventDefault());

// ─── Timer ───────────────────────────────────────────────
function startLocalTimer() {
    stopLocalTimer();
    localTimerInterval = setInterval(() => {
        if (gameState !== 'playing') return;
        const elapsed = (Date.now() - roundStartTimeMs) / 1000;
        displayTime(elapsed);
    }, 50); // Update 20 times per second for smoothness
}

function stopLocalTimer() {
    if (localTimerInterval) {
        clearInterval(localTimerInterval);
        localTimerInterval = null;
    }
}

function displayTime(totalSeconds) {
    if (totalSeconds < 0) totalSeconds = 0;
    const mins = Math.floor(totalSeconds / 60);
    const secs = Math.floor(totalSeconds % 60);
    const ms = Math.floor((totalSeconds % 1) * 10);

    updateDisplay(mins, Math.floor(secs / 10), secs % 10, ms);
}

function updateDisplay(d1, d2, d3, d4) {
    digitM1.textContent = d1;
    digitS1.textContent = d2;
    digitS2.textContent = d3;
    digitMs.textContent = d4;
}

// ─── Room Info ───────────────────────────────────────────
function updateRoomInfo(room) {
    roundNum.textContent = room.currentRound;
    totalRoundsEl.textContent = room.totalRounds;

    // My remaining time
    const me = room.players.find(p => p.id === player.id);
    if (me) {
        myRemainingTime.textContent = formatTime(me.remainingTimeSeconds);
    }

    renderPlayerCards(room);
}

function renderPlayerCards(room) {
    let html = '';
    for (const p of room.players) {
        const isMe = p.id === player.id;
        const roundData = room.currentGameRound?.playerData?.[p.id];
        const participating = roundData ? roundData.isParticipating : true;
        const holding = roundData ? roundData.isHolding : false;

        let statusClass = '';
        if (!participating) statusClass = 'player-out';
        else if (holding) statusClass = 'player-holding';

        html += `
            <div class="col-6 col-md-3">
                <div class="card player-card ${statusClass} ${isMe ? 'border-primary' : ''}" id="player-card-${p.id}">
                    <div class="card-body p-2 text-center">
                        <div class="fw-bold small">${escapeHtml(p.name)}</div>
                        <div class="small text-muted">⏱ ${formatTime(p.remainingTimeSeconds)}</div>
                        <div class="small">🏆 ${p.score}</div>
                    </div>
                </div>
            </div>`;
    }
    playerInfoCards.innerHTML = html;
}

function highlightPlayer(playerId, holding) {
    const card = document.getElementById(`player-card-${playerId}`);
    if (card) {
        if (holding) {
            card.classList.add('player-holding');
        } else {
            card.classList.remove('player-holding');
        }
    }
}

function markPlayerReleased(playerId, timeSpentSeconds) {
    const card = document.getElementById(`player-card-${playerId}`);
    if (card) {
        card.classList.remove('player-holding');
        card.classList.add('player-released');
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
        console.log('SignalR connected (game)');
        connection.invoke('GetRoomInfo', roomId);
    })
    .catch(err => {
        console.error('SignalR connection error:', err);
        gameStatus.textContent = '連線失敗，請重新整理頁面';
    });
