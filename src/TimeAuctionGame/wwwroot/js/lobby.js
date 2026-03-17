'use strict';

(function () {
    // ── DOM refs ──────────────────────────────────────────────────────────────
    const roomListEl       = document.getElementById('roomList');
    const noRoomsMsg       = document.getElementById('noRoomsMsg');
    const shortcodeInput   = document.getElementById('shortcodeInput');
    const shortcodeJoinBtn = document.getElementById('shortcodeJoinBtn');
    const shortcodeError   = document.getElementById('shortcodeError');
    const newRoomName      = document.getElementById('newRoomName');
    const newMaxTime       = document.getElementById('newMaxTime');
    const newTotalRounds   = document.getElementById('newTotalRounds');
    const confirmCreateBtn = document.getElementById('confirmCreateRoom');
    const createRoomError  = document.getElementById('createRoomError');

    // ── SignalR 連線 ──────────────────────────────────────────────────────────
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/gamehub')
        .withAutomaticReconnect()
        .build();

    // RoomListUpdated → 重新渲染列表
    connection.on('RoomListUpdated', renderRoomList);

    connection.start().catch(err => console.error('[lobby] SignalR 連線失敗：', err));

    // ── 建立房間 Modal ────────────────────────────────────────────────────────
    // 建立房間不在 lobby 的 SignalR 連線中呼叫，以避免導航時連線關閉觸發斷線解散房間。
    // 改為儲存設定到 sessionStorage，由 room.js 在新頁面的連線中呼叫 CreateRoom。
    confirmCreateBtn.addEventListener('click', function () {
        const name = newRoomName.value.trim();
        if (!name) {
            showCreateRoomError('請輸入房間名稱');
            return;
        }
        hideCreateRoomError();

        sessionStorage.setItem('pendingCreate', JSON.stringify({
            name:    name,
            maxTime: parseInt(newMaxTime.value, 10),
            rounds:  parseInt(newTotalRounds.value, 10)
        }));
        window.location.href = '/Room';
    });

    // 關閉 Modal → 清除狀態
    document.getElementById('createRoomModal').addEventListener('hidden.bs.modal', function () {
        newRoomName.value = '';
        hideCreateRoomError();
        confirmCreateBtn.disabled = false;
    });

    // ── 短碼加入（導向，實際 JoinRoom 由 room.js 呼叫）────────────────────────
    shortcodeJoinBtn.addEventListener('click', joinByShortcode);
    shortcodeInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') joinByShortcode();
    });
    shortcodeInput.addEventListener('input', function () {
        this.value = this.value.toUpperCase();
    });

    function joinByShortcode() {
        const code = shortcodeInput.value.trim().toUpperCase();
        if (code.length !== 6) {
            showShortcodeError('請輸入 6 碼房間代碼');
            return;
        }
        hideShortcodeError();
        window.location.href = `/Room/${code}`;
    }

    // ── 列表加入按鈕（事件委派）──────────────────────────────────────────────
    roomListEl.addEventListener('click', function (e) {
        const btn = e.target.closest('.join-room-btn');
        if (!btn || btn.disabled) return;
        window.location.href = `/Room/${btn.dataset.roomId}`;
    });

    // ── 渲染房間列表 ──────────────────────────────────────────────────────────
    // RoomSummary.State 由 STJ 序列化為整數：Waiting=0, Countdown=1, InProgress=2, ...
    function renderRoomList(rooms) {
        roomListEl.innerHTML = '';

        if (!rooms || rooms.length === 0) {
            noRoomsMsg.classList.remove('d-none');
            return;
        }
        noRoomsMsg.classList.add('d-none');

        rooms.forEach(function (room) {
            const isWaiting  = room.state === 0;
            const stateText  = isWaiting ? '等待中' : '遊戲中';
            const stateBadge = isWaiting ? 'bg-success' : 'bg-secondary';

            const col = document.createElement('div');
            col.className = 'col-md-6 col-lg-4';
            col.innerHTML = `
                <div class="card h-100">
                    <div class="card-body">
                        <h5 class="card-title mb-1">${esc(room.roomName)}</h5>
                        <p class="card-text text-muted small mb-2">
                            ${room.playerCount} 人 ／ 初始 ${room.maxTimeMinutes} 分 ／ ${room.totalRounds} 回合
                        </p>
                        <span class="badge ${stateBadge}">${stateText}</span>
                    </div>
                    <div class="card-footer d-flex justify-content-between align-items-center">
                        <button class="btn btn-sm btn-primary join-room-btn"
                                data-room-id="${room.roomId}"
                                ${!isWaiting ? 'disabled' : ''}>加入</button>
                        <span class="text-muted small font-monospace">${room.roomId}</span>
                    </div>
                </div>`;
            roomListEl.appendChild(col);
        });
    }

    // ── 工具函數 ──────────────────────────────────────────────────────────────
    function showCreateRoomError(msg) {
        createRoomError.textContent = msg;
        createRoomError.classList.remove('d-none');
    }
    function hideCreateRoomError() {
        createRoomError.classList.add('d-none');
    }
    function showShortcodeError(msg) {
        shortcodeError.textContent = msg;
        shortcodeError.classList.remove('d-none');
    }
    function hideShortcodeError() {
        shortcodeError.classList.add('d-none');
    }
    function esc(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }
}());
