document.addEventListener('DOMContentLoaded', function () {
    const loginSection = document.getElementById('login-section');
    const mainContent = document.getElementById('main-content');
    const passwordInput = document.getElementById('password');
    const loginButton = document.getElementById('loginButton');
    const logoutButton = document.getElementById('logoutButton');
    const refreshButton = document.getElementById('refreshStatus');
    const userCardsContainer = document.getElementById('user-cards');
    const toast = document.getElementById('toast');

    let toastTimeout;
    let sessionToken = null;

    function showToast(message, isError = false) {
        toast.textContent = message;
        toast.style.backgroundColor = isError ? '#dc3545' : '#52B54B';
        toast.classList.add('show');
        clearTimeout(toastTimeout);
        toastTimeout = setTimeout(() => toast.classList.remove('show'), 3000);
    }

    function setLoginState(isLoggedIn) {
        sessionToken = isLoggedIn ? sessionToken : null;
        loginSection.classList.toggle('hidden', isLoggedIn);
        mainContent.classList.toggle('hidden', !isLoggedIn);
        if (!isLoggedIn) {
            passwordInput.value = '';
            userCardsContainer.innerHTML = '<p style="text-align: center; margin-top: 2em;">Enter password and click "Login" to see user status and controls.</p>';
        }
    }

    async function apiFetch(endpoint, method = 'POST', body = {}, buttonToDisable = null) {
        if (buttonToDisable) {
            buttonToDisable.disabled = true;
            buttonToDisable.textContent = '...';
        }

        let requestBody;
        if (endpoint === '/api/login') {
            requestBody = { Password: passwordInput.value };
        } else {
            if (!sessionToken) {
                showToast('Not authenticated. Please log in again.', true);
                setLoginState(false);
                return null;
            }
            requestBody = { ...body, Token: sessionToken };
        }

        try {
            const response = await fetch(endpoint, {
                method,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });

            if (response.status === 401) {
                showToast('Unauthorized: Invalid credentials or session expired.', true);
                setLoginState(false);
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
        } finally {
            if (buttonToDisable) {
                buttonToDisable.disabled = false;
                const originalText = buttonToDisable.dataset.originalText || 'Action';
                buttonToDisable.textContent = originalText;
            }
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
                <button class="action-save-limits" data-original-text="Save Limits">Save Limits</button>
                ${isTimedOut ? `<button class="button-secondary action-clear-timeout" data-original-text="Clear Time-Out">Clear Time-Out</button>` : ''}
                <div class="input-group">
                     <input type="number" class="minutes-input" placeholder="Mins" value="30">
                     <button class="action-extend-time" data-original-text="Extend">Extend</button>
                </div>
                <div class="input-group">
                    <input type="number" class="minutes-input-timeout" placeholder="Mins" value="15">
                    <button class="button-danger action-timeout-user" data-original-text="Time-Out">Time-Out</button>
                </div>
            </div>
        </div>
        `;
        }).join('');
    }

    async function handleLogin() {
        const password = passwordInput.value;
        if (!password) {
            showToast('Password is required.', true);
            return;
        }
        const data = await apiFetch('/api/login', 'POST', {}, loginButton);
        if (data && data.token) {
            sessionToken = data.token;
            showToast('Login successful!');
            setLoginState(true);
            await fetchStatus();
        }
    }

    async function fetchStatus() {
        const data = await apiFetch('/api/status', 'POST', {}, refreshButton);
        if (data) {
            renderUserCards(data);
        }
    }

    loginButton.addEventListener('click', handleLogin);
    passwordInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            handleLogin();
        }
    });

    logoutButton.addEventListener('click', (e) => {
        e.preventDefault();
        setLoginState(false);
        showToast('You have been logged out.');
    });

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
            }, button);
            if (result) showToast(result.message);
        }

        if (button.classList.contains('action-clear-timeout')) {
            const result = await apiFetch('/api/cleartimeout', 'POST', { UserId: userId }, button);
            if (result) {
                showToast(result.message);
                await fetchStatus();
            }
        }

        if (button.classList.contains('action-extend-time')) {
            const minutes = card.querySelector('.minutes-input').value;
            const result = await apiFetch('/api/extend', 'POST', { UserId: userId, Minutes: parseInt(minutes) }, button);
            if (result) {
                showToast(result.message);
                await fetchStatus();
            }
        }

        if (button.classList.contains('action-timeout-user')) {
            const minutes = card.querySelector('.minutes-input-timeout').value;
            const result = await apiFetch('/api/timeout', 'POST', { UserId: userId, Minutes: parseInt(minutes) }, button);
            if (result) {
                showToast(result.message);
                await fetchStatus();
            }
        }
    });
});