    document.addEventListener('DOMContentLoaded', function () {
        const passwordInput = document.getElementById('password');
        const refreshButton = document.getElementById('refreshStatus');
        const userCardsContainer = document.getElementById('user-cards');
        const toast = document.getElementById('toast');
        let toastTimeout;

        function showToast(message, isError = false) {
            toast.textContent = message;
            toast.style.backgroundColor = isError ? '#dc3545' : '#52B54B';
            toast.classList.add('show');
            clearTimeout(toastTimeout);
            toastTimeout = setTimeout(() => toast.classList.remove('show'), 3000);
        }

        async function apiFetch(endpoint, method = 'POST', body = {}) {
            const password = passwordInput.value;
            if (!password) {
                showToast('Password is required.', true);
                return null;
            }

            try {
                const response = await fetch(endpoint, {
                    method,
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ ...body, Password: password })
                });

                if (response.status === 401) {
                    showToast('Unauthorized: Invalid password.', true);
                    return null;
                }
                const responseData = await response.json();
                if (!response.ok) {
                    throw new Error(responseData.error || 'API request failed');
                }
                return responseData;

            } catch (error) {
                showToast(error.message, true);
                return null;
            }
        }

        function formatSeconds(seconds) {
            if (seconds < 60) return `${Math.round(seconds)}s`;
            const minutes = Math.floor(seconds / 60);
            const hours = Math.floor(minutes / 60);
            const remainingMinutes = minutes % 60;
            return hours > 0 ? `${hours}h ${remainingMinutes}m` : `${minutes}m`;
        }

        function renderUserCards(users) {
            if (!users || users.length === 0) {
                userCardsContainer.innerHTML = '<p style="text-align: center; margin-top: 2em;">No limited users are configured in the plugin.</p>';
                return;
            }

            userCardsContainer.innerHTML = users.map(user => {
                const isTimedOut = new Date(user.TimeOutUntil) > new Date();
                const statusHtml = isTimedOut
                    ? `<span class="status-timeout">TIMED OUT</span>`
                    : `<span class="status-ok">OK</span>`;

                return `
            <div class="user-card" data-userid="${user.UserId}">
                <h2>
                    <span>${user.Username}</span>
                    ${statusHtml}
                </h2>
                <div class="stats-grid">
                    <div class="stat-item">
                        <span class="label">Daily Watched</span>
                        <span class="value">${formatSeconds(user.SecondsWatchedDaily)}</span>
                    </div>
                     <div class="stat-item">
                        <span class="label">Weekly Watched</span>
                        <span class="value">${formatSeconds(user.SecondsWatchedWeekly)}</span>
                    </div>
                     <div class="stat-item">
                        <span class="label">Monthly Watched</span>
                        <span class="value">${formatSeconds(user.SecondsWatchedMonthly)}</span>
                    </div>
                </div>

                <div class="controls-grid">
                    <div class="control-group">
                        <label for="daily-${user.UserId}">Daily Limit (Minutes)</label>
                        <input type="number" id="daily-${user.UserId}" class="limit-daily" value="${user.DailyLimitMinutes}">
                    </div>
                    <div class="control-group">
                        <label for="weekly-${user.UserId}">Weekly Limit (Hours)</label>
                        <input type="number" id="weekly-${user.UserId}" class="limit-weekly" value="${user.WeeklyLimitHours}">
                    </div>
                     <div class="control-group">
                        <label for="monthly-${user.UserId}">Monthly Limit (Hours)</label>
                        <input type="number" id="monthly-${user.UserId}" class="limit-monthly" value="${user.MonthlyLimitHours}">
                    </div>
                </div>
                 <div class="actions-group">
                    <button class="action-save-limits">Save Limits</button>
                    ${isTimedOut ? `<button class="button-secondary action-clear-timeout">Clear Time-Out</button>` : ''}
                    <div class="input-group">
                         <input type="number" class="minutes-input" placeholder="Mins" value="30">
                         <button class="action-extend-time">Extend</button>
                    </div>
                    <div class="input-group">
                        <input type="number" class="minutes-input-timeout" placeholder="Mins" value="15">
                        <button class="button-danger action-timeout-user">Time-Out</button>
                    </div>
                </div>
            </div>
            `;
            }).join('');
        }

        async function fetchStatus() {
            const data = await apiFetch('/api/status');
            if (data) {
                renderUserCards(data);
            }
        }

        refreshButton.addEventListener('click', fetchStatus);

        userCardsContainer.addEventListener('click', async (e) => {
            const button = e.target.closest('button');
            if (!button) return;

            const card = button.closest('.user-card');
            const userId = card.dataset.userid;

            if (button.classList.contains('action-save-limits')) {
                const daily = card.querySelector('.limit-daily').value;
                const weekly = card.querySelector('.limit-weekly').value;
                const monthly = card.querySelector('.limit-monthly').value;
                const result = await apiFetch('/api/updatelimits', 'POST', {
                    UserId: userId,
                    DailyLimitMinutes: parseInt(daily),
                    WeeklyLimitHours: parseInt(weekly),
                    MonthlyLimitHours: parseInt(monthly)
                });
                if (result) showToast(result.message);
            }

            if (button.classList.contains('action-clear-timeout')) {
                const result = await apiFetch('/api/cleartimeout', 'POST', { UserId: userId });
                if (result) {
                    showToast(result.message);
                    fetchStatus();
                }
            }

            if (button.classList.contains('action-extend-time')) {
                const minutes = card.querySelector('.minutes-input').value;
                const result = await apiFetch('/api/extend', 'POST', { UserId: userId, Minutes: parseInt(minutes) });
                if (result) {
                    showToast(result.message);
                    fetchStatus();
                }
            }

            if (button.classList.contains('action-timeout-user')) {
                const minutes = card.querySelector('.minutes-input-timeout').value;
                const result = await apiFetch('/api/timeout', 'POST', { UserId: userId, Minutes: parseInt(minutes) });
                if (result) {
                    showToast(result.message);
                    fetchStatus();
                }
            }
        });
    });