'use strict';

// game.js 依賴 room.js 先執行（共用 window.hubConnection / window.roomState / window.showSection）
(function () {
    const connection = window.hubConnection;
    const roomState  = window.roomState;

    // ── DOM refs ──────────────────────────────────────────────────────────────
    const roundLabel       = document.getElementById('round-label');
    const timerDisplay     = document.getElementById('timer-display');
    const gameStatus       = document.getElementById('game-status');
    const bigBtn           = document.getElementById('big-btn');
    const bigBtnLabel      = document.getElementById('big-btn-label');
    const playerTimeList   = document.getElementById('player-time-list');
    const roundResultMsg   = document.getElementById('round-result-msg');
    const roundScoresBody  = document.getElementById('round-scores-body');
    const nextRoundBtn     = document.getElementById('next-round-btn');
    const waitingNextMsg   = document.getElementById('waiting-next-msg');
    const finalScoresBody  = document.getElementById('final-scores-body');

    // ── 執行時狀態 ────────────────────────────────────────────────────────────
    let timerInterval          = null;   // 回合計時器
    let countdownInterval      = null;   // 5 秒視覺倒數
    let roundStartTime         = 0;      // Unix ms，來自 RoundStarted 事件
    let inCountdown            = false;  // 目前是否在 5s 倒數中
    let releasedDuringCountdown = false; // 倒數期間放開過按鈕（不知是否 >= 2s）

    // ── SignalR 事件 ──────────────────────────────────────────────────────────

    /**
     * 回合準備：重置按鈕、渲染玩家時間列表、切換到遊戲區塊。
     * 資料：{ round, totalRounds, scores: ScoreEntry[] }
     */
    connection.on('RoundPrepare', function (data) {
        stopTimer();
        stopCountdownAnimation();
        inCountdown = false;
        releasedDuringCountdown = false;

        roundLabel.textContent = `第 ${data.round} 回合 ／ 共 ${data.totalRounds} 回合`;
        timerDisplay.textContent = '00:00.0';
        gameStatus.textContent = '';

        // 外地往 → 按鈕 enabled，等待玩家按住以啟動倒數
        bigBtn.disabled = false;
        resetBtnAppearance();

        renderPlayerTimeList(data.scores);
        window.showSection('section-game');
    });

    /**
     * 倒數開始：顯示 3→2→1 視覺倒數。
     */
    connection.on('CountdownStarted', function () {
        inCountdown = true;
        gameStatus.textContent = '所有玩家就緒，即將開始...';
        // 不 disable 按鈕：玩家需繼續按住，可隨時放開以觸發放棄（≥2s）或取消（<2s）邏輯

        let count = 5;
        timerDisplay.textContent = msToShortDisplay(count * 1000);  // 00:05

        stopCountdownAnimation();
        countdownInterval = setInterval(function () {
            count--;
            if (count > 0) {
                timerDisplay.textContent = msToShortDisplay(count * 1000);
            } else {
                stopCountdownAnimation();
            }
        }, 1000);
    });

    /**
     * 倒數取消（有人在倒數中放開按鈕）：隱藏倒數、恢復按鈕。
     */
    connection.on('CountdownCancelled', function () {
        stopCountdownAnimation();
        inCountdown = false;
        releasedDuringCountdown = false;   // < 2s 放開，重置可再次嘗試
        gameStatus.textContent = '';
        timerDisplay.textContent = '00:00.0';
        bigBtn.disabled = false;
        resetBtnAppearance();
    });

    /**
     * 伺服器判定此玩家在倒數 ≥ 2s 後放開 → 本回合放棄（僅傳給當事人）。
     * 倒數仍繼續，其他玩家照常進行。
     */
    connection.on('SittingOutRound', function () {
        bigBtn.disabled = true;
        bigBtnLabel.textContent = '本回合放棄';
    });

    /**
     * 回合正式開始：啟動 client-side 計時器。
     * 資料：{ roundNumber, roundStartTime }
     */
    connection.on('RoundStarted', function (data) {
        roundStartTime = data.roundStartTime;
        stopCountdownAnimation();
        inCountdown = false;
        gameStatus.textContent = '';

        if (!releasedDuringCountdown) {
            // 持續按住中 — 保留 pressed 狀態，更新標籤
            bigBtn.disabled = false;
            bigBtnLabel.textContent = '持續按住...';
        } else {
            // 倒數中已放開（放棄本回合）：確保顯示放棄狀態
            bigBtn.disabled = true;
            bigBtnLabel.textContent = '本回合放棄';
        }
        releasedDuringCountdown = false;

        stopTimer();
        timerInterval = setInterval(function () {
            const elapsed = Date.now() - roundStartTime;
            timerDisplay.textContent = msToTimerDisplay(elapsed);
        }, 100);
    });

    /**
     * 玩家剩餘時間更新（有人放開按鈕後）。
     * 資料：{ playerId, remainingTimeMs }
     */
    connection.on('PlayerTimeUpdated', function (data) {
        updatePlayerTimeCard(data.playerId, data.remainingTimeMs);
    });

    /**
     * 回合結束：停止計時、顯示回合結果。
     * 資料：{ winnerIds, winnerNames, scores: ScoreEntry[] }
     */
    connection.on('RoundEnded', function (data) {
        stopTimer();
        bigBtn.disabled = true;
        resetBtnAppearance();

        if (data.winnerIds && data.winnerIds.length > 0) {
            roundResultMsg.textContent = `🏆 本回合勝者：${data.winnerNames.join('、')}`;
        } else {
            roundResultMsg.textContent = '本回合平手，無人得分';
        }

        renderScoreTable(roundScoresBody, data.scores, false);

        if (roomState.isHost) {
            nextRoundBtn.classList.remove('d-none');
            nextRoundBtn.disabled = false;
            waitingNextMsg.classList.add('d-none');
        } else {
            nextRoundBtn.classList.add('d-none');
            waitingNextMsg.classList.remove('d-none');
        }

        window.showSection('section-round-end');
    });

    /**
     * 遊戲結束：顯示最終排名。
     * 資料：{ finalScores: ScoreEntry[] }
     */
    connection.on('GameEnded', function (data) {
        stopTimer();
        renderScoreTable(finalScoresBody, data.finalScores, true);
        window.showSection('section-game-end');
    });

    // ── 大按鈕互動 ────────────────────────────────────────────────────────────
    bigBtn.addEventListener('mousedown', onPress);
    bigBtn.addEventListener('touchstart', function (e) {
        e.preventDefault();   // 防止 300ms 延遲 & 觸發 mousedown
        onPress();
    }, { passive: false });

    document.addEventListener('mouseup', onRelease);
    document.addEventListener('touchend', onRelease);
    document.addEventListener('touchcancel', onRelease);

    function onPress() {
        if (bigBtn.disabled || bigBtn.classList.contains('pressed')) return;
        bigBtn.classList.add('pressed');
        bigBtnLabel.textContent = '持續按住...';
        connection.invoke('PressButton').catch(function (err) {
            console.error('[game] PressButton 失敗：', err);
        });
    }

    function onRelease() {
        if (!bigBtn.classList.contains('pressed')) return;
        bigBtn.classList.remove('pressed');
        connection.invoke('ReleaseButton').catch(function (err) {
            console.error('[game] ReleaseButton 失敗：', err);
        });

        if (inCountdown) {
            // 倒數期間放開：暫時禁用，等待伺服器判定
            // < 2s → 收到 CountdownCancelled → 重新啟用，可再次按
            // ≥ 2s → 收到 SittingOutRound  → 維持停用，本回合放棄
            releasedDuringCountdown = true;
            bigBtn.disabled = true;
            bigBtnLabel.textContent = '放開中...';
        } else {
            bigBtnLabel.textContent = '按住';
        }
    }

    function resetBtnAppearance() {
        bigBtn.classList.remove('pressed');
        bigBtnLabel.textContent = '按住';
    }

    // ── 下一回合（房主） ──────────────────────────────────────────────────────
    nextRoundBtn.addEventListener('click', function () {
        nextRoundBtn.disabled = true;
        connection.invoke('StartNextRound').catch(function (err) {
            nextRoundBtn.disabled = false;
            console.error('[game] StartNextRound 失敗：', err);
        });
    });

    // ── 渲染輔助 ──────────────────────────────────────────────────────────────

    /** 只渲染自己的剩餘時間卡片 */
    function renderPlayerTimeList(scores) {
        playerTimeList.innerHTML = '';
        const mine = scores.find(function (s) { return s.playerId === roomState.myPlayerId; });
        if (!mine) return;
        const col = document.createElement('div');
        col.className = 'col-auto';
        col.id = `ptime-${mine.playerId}`;
        col.innerHTML = `
            <div class="card text-center px-3 py-2 border-primary border-2
                 ${mine.remainingTimeMs <= 0 ? 'opacity-50' : ''}">
                <div class="small fw-semibold mb-1">我的剩餘時間 👤</div>
                <div class="DSEG7Classic player-time-display">${msToShortDisplay(mine.remainingTimeMs)}</div>
            </div>`;
        playerTimeList.appendChild(col);
    }

    /** 更新單一玩家剩餘時間卡片 */
    function updatePlayerTimeCard(playerId, remainingTimeMs) {
        const card = document.getElementById(`ptime-${playerId}`);
        if (!card) return;
        const display = card.querySelector('.player-time-display');
        if (display) display.textContent = msToShortDisplay(remainingTimeMs);
        if (remainingTimeMs <= 0) {
            card.querySelector('.card').classList.add('opacity-50');
        }
    }

    /**
     * 渲染分數表格。
     * @param {HTMLElement} tbody
     * @param {Array} scores  ScoreEntry[]
     * @param {boolean} showRank  是否顯示名次欄（遊戲結束用）
     */
    function renderScoreTable(tbody, scores, showRank) {
        tbody.innerHTML = '';
        scores.forEach(function (s, i) {
            const isMe = s.playerId === roomState.myPlayerId;
            const tr   = document.createElement('tr');
            if (isMe) tr.classList.add('table-primary');

            const medal = i === 0 ? '🥇' : i === 1 ? '🥈' : i === 2 ? '🥉' : `${i + 1}`;
            const rankCell = showRank ? `<td>${medal}</td>` : '';

            tr.innerHTML = `
                ${rankCell}
                <td>${esc(s.name)}${isMe ? ' <span class="badge bg-primary ms-1">我</span>' : ''}</td>
                <td class="text-center fw-bold">${s.score}</td>
                <td class="text-end font-monospace">${msToShortDisplay(s.remainingTimeMs)}</td>`;
            tbody.appendChild(tr);
        });
    }

    // ── 計時 / 倒數輔助 ──────────────────────────────────────────────────────

    function stopTimer() {
        if (timerInterval !== null) {
            clearInterval(timerInterval);
            timerInterval = null;
        }
    }

    function stopCountdownAnimation() {
        if (countdownInterval !== null) {
            clearInterval(countdownInterval);
            countdownInterval = null;
        }
    }

    // ── 時間格式化 ────────────────────────────────────────────────────────────

    /** ms → MM:SS.d（七段顯示器大計時器用） */
    function msToTimerDisplay(ms) {
        const clamped   = Math.max(0, ms);
        const tenths    = Math.floor(clamped / 100) % 10;
        const totalSec  = Math.floor(clamped / 1000);
        const sec       = totalSec % 60;
        const min       = Math.floor(totalSec / 60);
        return `${pad2(min)}:${pad2(sec)}.${tenths}`;
    }

    /** ms → MM:SS（玩家剩餘時間卡片用） */
    function msToShortDisplay(ms) {
        const totalSec = Math.max(0, Math.floor(ms / 1000));
        return `${pad2(Math.floor(totalSec / 60))}:${pad2(totalSec % 60)}`;
    }

    function pad2(n) { return String(n).padStart(2, '0'); }

    function esc(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }
}());
