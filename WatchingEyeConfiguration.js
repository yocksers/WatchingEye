define(['baseView', 'loading', 'dialogHelper', 'toast', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-select'], function (BaseView, loading, dialogHelper, toast) {
    'use strict';

    const pluginId = "e8c3b1b3-4f56-4f38-a28a-2e6c5a043007";
    const newUserId = 'new_user_temp_id';

    function getPluginConfiguration() {
        return ApiClient.getPluginConfiguration(pluginId);
    }

    function updatePluginConfiguration(config) {
        return ApiClient.updatePluginConfiguration(pluginId, config);
    }

    function getLimitedUsersStatus() {
        return ApiClient.getJSON(ApiClient.getUrl("WatchingEye/LimitedUsersStatus"));
    }

    function getClientList() {
        return ApiClient.getJSON(ApiClient.getUrl("WatchingEye/GetClientList"));
    }

    function getLogEvents() {
        return ApiClient.getJSON(ApiClient.getUrl("WatchingEye/GetLogEvents"));
    }

    function getWebServerStatus() {
        return ApiClient.getJSON(ApiClient.getUrl("WatchingEye/WebServerStatus"));
    }

    function clearTimeOut(userId) {
        return ApiClient.ajax({
            type: "POST",
            url: ApiClient.getUrl("WatchingEye/ClearTimeOut"),
            data: JSON.stringify({ UserId: userId }),
            contentType: 'application/json'
        });
    }

    function clearLogs() {
        return ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("WatchingEye/ClearLogs") });
    }

    function renderWebServerStatus(view) {
        const container = view.querySelector('#webServerStatusContainer');
        getWebServerStatus().then(response => {
            const status = response.Status;
            let html = '';
            if (status.startsWith('Error:')) {
                const commandMatch = status.match(/netsh http add urlacl url=http:\/\/\*:\d+\/ user="Everyone"/);
                const command = commandMatch ? commandMatch[0] : 'See Emby server logs for the exact command.';
                html = `
                <div class="webserver-status-box webserver-status-box-error">
                    <h3>Web Server Failed to Start</h3>
                    <p>The external web server could not start due to an "Access Denied" error. This is a standard Windows security feature.</p>
                    <p><strong>To fix this</strong>, you must run the following command in Command Prompt (cmd.exe) as an **Administrator**, and then restart the Emby server:</p>
                    <code>${command}</code>
                </div>`;
            } else if (status.startsWith('Running')) {
                html = `
                 <div class="webserver-status-box webserver-status-box-success">
                    <p><strong>External Web Server is running.</strong> You can access it at http://[Your-Emby-IP]:${view.querySelector('#numExternalWebServerPort').value}</p>
                 </div>`;
            }
            container.innerHTML = html;
        });
    }

    function renderLogs(view) {
        const container = view.querySelector('#logContainer');
        getLogEvents().then(events => {
            if (events.length === 0) {
                container.innerHTML = '<p>The event log is currently empty.</p>';
                return;
            }

            const html = events.map(entry => {
                const eventDate = new Date(entry.Timestamp).toLocaleString();
                const icon = entry.EventType === 'Transcode' ? 'sync_problem' : 'timer_off';

                return `
                <div class="listItem" style="display:flex; align-items: center; padding: 0.8em 0;">
                     <i class="md-icon" style="color:#52B54B; margin-right: 1em;">${icon}</i>
                     <div class="listItemBody">
                         <h3 class="listItemTitle">${entry.EventType} - ${entry.Username || 'Unknown User'}</h3>
                         <div class="listItemText">${entry.Message}</div>
                         <div class="listItemText secondary">${eventDate} on ${entry.ClientName || 'Unknown Client'}</div>
                     </div>
                </div>
            `;
            }).join('');
            container.innerHTML = html;
        });
    }

    function formatTime(hoursDouble) {
        if (hoursDouble === null || typeof hoursDouble === 'undefined') return '';
        const hours = Math.floor(hoursDouble);
        const minutes = Math.round((hoursDouble % 1) * 60);
        return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`;
    }

    function generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }


    return class extends BaseView {
        constructor(view, params) {
            super(view, params);

            this.config = {};
            this.watchStatusInterval = null;
            this.editingUserId = null;
            this.allUsers = [];
            this.allClients = [];
            this.allLibraries = [];

            view.querySelector('#numExternalWebServerPort').addEventListener('input', (e) => {
                const port = e.target.value || 9988;
                view.querySelector('#netshCommand').textContent = `netsh http add urlacl url=http://*:${port}/ user="Everyone"`;
            });

            view.querySelector('.watchingEyeForm').addEventListener('submit', (e) => {
                e.preventDefault();
                if (this.editingUserId) {
                    toast({ type: 'error', text: 'Please save or cancel the user you are currently editing.' });
                    return;
                }
                this.saveData(view);
                return false;
            });

            view.querySelector('.localnav').addEventListener('click', (e) => {
                e.preventDefault();
                const target = e.target.closest('.nav-button');
                if (target) {
                    const targetId = target.getAttribute('data-target');
                    view.querySelectorAll('.localnav .nav-button').forEach(b => b.classList.remove('ui-btn-active'));
                    target.classList.add('ui-btn-active');

                    view.querySelectorAll('#notificationsPage, #limiterPage, #loggingPage, #remoteAccessPage').forEach(page => {
                        page.classList.toggle('hide', page.id !== targetId);
                    });

                    if (targetId === 'loggingPage') {
                        renderLogs(view);
                    }
                    if (targetId === 'remoteAccessPage' && this.config.EnableExternalWebServer) {
                        renderWebServerStatus(this.view);
                    }
                }
            });

            view.querySelector('#btnAddLimitedUser').addEventListener('click', () => {
                if (this.editingUserId) return;
                this.editingUserId = newUserId;
                this.renderLimitedUsers(this.view, this.config);
            });

            view.querySelector('#btnGenerateApiKey').addEventListener('click', () => {
                this.view.querySelector('#txtApiKey').value = generateGuid();
                toast('New API Key generated. Click Save to apply.');
            });

            view.querySelector('#btnResetAll').addEventListener('click', () => {
                ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("WatchingEye/ResetAllUsersTime") }).then(() => {
                    toast('Reset time for all users.');
                    this.renderLimitedUsers(this.view, this.config);
                });
            });

            view.querySelector('#btnClearLog').addEventListener('click', () => {
                clearLogs().then(() => {
                    toast('Event log has been cleared.');
                    renderLogs(view);
                });
            });

            view.querySelector('#limitedUsersContainer').addEventListener('click', (e) => {
                const buttonTarget = e.target.closest('button');
                if (!buttonTarget) return;

                const userId = buttonTarget.getAttribute('data-userid');

                if (buttonTarget.classList.contains('user-editor-tab-button')) {
                    e.preventDefault();
                    const tabId = buttonTarget.getAttribute('data-tab-id');
                    const editor = buttonTarget.closest('.user-editor');
                    editor.querySelectorAll('.user-editor-tab-button').forEach(btn => btn.classList.remove('is-active'));
                    buttonTarget.classList.add('is-active');
                    editor.querySelectorAll('.user-editor-tab-content').forEach(content => content.classList.toggle('hide', content.id !== tabId));
                    return;
                }

                if (buttonTarget.classList.contains('btnTimeOutUser')) {
                    const input = this.view.querySelector(`.timeOutMinutes[data-userid="${userId}"]`);
                    const minutes = parseInt(input.value);

                    if (!minutes || minutes <= 0) {
                        toast({ type: 'error', text: 'Please enter a valid number of minutes for the time-out.' });
                        return;
                    }

                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("WatchingEye/TimeOutUser"),
                        data: JSON.stringify({ UserId: userId, Minutes: minutes }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`User placed in time-out.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error placing user in time-out.' }));
                    return;
                }

                if (buttonTarget.classList.contains('btnClearTimeOut')) {
                    clearTimeOut(userId).then(() => {
                        toast('Time-out cleared for user.');
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error clearing time-out.' }));
                    return;
                }

                if (!userId) return;

                if (buttonTarget.classList.contains('btnEditUser')) {
                    if (this.editingUserId) return;
                    this.editingUserId = userId;
                    this.renderLimitedUsers(this.view, this.config);
                    return;
                }

                if (buttonTarget.classList.contains('btn-cancel-edit-user')) {
                    this.editingUserId = null;
                    this.renderLimitedUsers(this.view, this.config);
                    return;
                }

                if (buttonTarget.classList.contains('btn-save-user-inline')) {
                    this.saveUserInline(buttonTarget, userId);
                    return;
                }

                if (this.editingUserId) {
                    toast({ type: 'error', text: 'Please save or cancel the user you are currently editing before performing other actions.' });
                    return;
                }

                if (buttonTarget.classList.contains('btnRemoveUser')) {
                    const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                    if (user) {
                        this.config.LimitedUsers = this.config.LimitedUsers.filter(u => u.UserId !== userId);
                        toast(`User ${user.Username} removed. Please click Save to apply changes.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }
                    return;
                }

                if (buttonTarget.classList.contains('btnExtendTime')) {
                    const input = this.view.querySelector(`.extendTimeMinutes[data-userid="${userId}"]`);
                    const minutes = parseInt(input.value);

                    if (!minutes || minutes <= 0) {
                        toast({ type: 'error', text: 'Please enter a valid number of minutes to extend.' });
                        return;
                    }

                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("WatchingEye/ExtendTime"),
                        data: JSON.stringify({ UserId: userId, Minutes: minutes }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`Time extended for user.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error extending time.' }));
                    return;
                }

                if (buttonTarget.classList.contains('btnToggleUserLimit')) {
                    const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                    if (user) {
                        user.IsEnabled = !user.IsEnabled;
                        toast(`Limit for ${user.Username} has been ${user.IsEnabled ? 'enabled' : 'disabled'}. Remember to save your changes.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }
                    return;
                }

                if (buttonTarget.classList.contains('btnResetUser')) {
                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("WatchingEye/ResetUserTime"),
                        data: JSON.stringify({ UserId: userId }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`Time reset for user.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error resetting time.' }));
                }
            });
        }

        getLimitedUserDisplayHtml(user, status) {
            const isEnabled = user.IsEnabled !== false;
            const disabledClass = isEnabled ? '' : 'disabled-item';
            const statusText = isEnabled ? '' : ' (Disabled)';
            const toggleTitle = isEnabled ? 'Disable Limit' : 'Enable Limit';
            const toggleIcon = isEnabled ? 'power_settings_new' : 'block';

            const isTimedOut = status && new Date(status.TimeOutUntil) > new Date();
            let timeOutHtml = '';
            if (isTimedOut) {
                const timeOutUntilString = new Date(status.TimeOutUntil).toLocaleTimeString();
                timeOutHtml = `<div class="listItemText secondary" style="color: #f44336;">Timed Out until ${timeOutUntilString}</div>`;
            }

            let remainingTimeText = 'No active limits.';
            if (status) {
                const parts = [];
                if (user.EnableDailyLimit) {
                    const watched = Math.floor(status.SecondsWatchedDaily / 60);
                    parts.push(`Daily: ${watched}/${status.DailyLimitMinutes}m`);
                }
                if (user.EnableWeeklyLimit) {
                    const watched = Math.floor(status.SecondsWatchedWeekly / 3600);
                    parts.push(`Weekly: ${watched}/${status.WeeklyLimitHours}h`);
                }
                if (user.EnableMonthlyLimit) {
                    const watched = Math.floor(status.SecondsWatchedMonthly / 3600);
                    parts.push(`Monthly: ${watched}/${status.MonthlyLimitHours}h`);
                }
                if (user.EnableYearlyLimit) {
                    const watched = Math.floor(status.SecondsWatchedYearly / 3600);
                    parts.push(`Yearly: ${watched}/${status.YearlyLimitHours}h`);
                }
                if (parts.length > 0) {
                    remainingTimeText = parts.join(' | ');
                }
            }

            const timeWindowText = user.EnableTimeWindow ? `Plays between ${formatTime(user.WatchWindowStartHour || 0)} and ${formatTime(user.WatchWindowEndHour || 0)}` : '';

            return `
            <div class="listItem ${disabledClass}" style="display:flex; align-items: center; padding: 0.5em 0;">
                <div class="listItemBody" style="flex-grow: 1;">
                    <h3 class="listItemTitle">${user.Username}${statusText}</h3>
                    ${timeOutHtml}
                    <div class="listItemText secondary">${remainingTimeText}</div>
                    <div class="listItemText">${timeWindowText}</div>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5em; margin-left: 1em; flex-wrap: wrap; justify-content: flex-end;">
                    <input is="emby-input" type="number" class="extendTimeMinutes" placeholder="Mins" value="30" style="width: 80px;" data-userid="${user.UserId}" />
                    <button is="emby-button" type="button" class="raised mini btnExtendTime" data-userid="${user.UserId}" title="Extend Time">
                        <span>Extend</span>
                    </button>
                    <input is="emby-input" type="number" class="timeOutMinutes" placeholder="Mins" value="15" style="width: 80px;" data-userid="${user.UserId}" />
                    <button is="emby-button" type="button" class="raised mini button-cancel btnTimeOutUser" data-userid="${user.UserId}" title="Time-out User">
                        <span>Time-Out</span>
                    </button>
                    ${isTimedOut ? `
                    <button is="emby-button" type="button" class="raised mini button-accent btnClearTimeOut" data-userid="${user.UserId}" title="Clear Time-Out">
                        <span>Clear TO</span>
                    </button>
                    ` : ''}
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnResetUser" data-userid="${user.UserId}" title="Reset Time">
                        <i class="md-icon">refresh</i>
                    </button>
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnEditUser" data-userid="${user.UserId}" title="Edit Limit">
                        <i class="md-icon">edit</i>
                    </button>
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnToggleUserLimit" data-userid="${user.UserId}" title="${toggleTitle}">
                        <i class="md-icon">${toggleIcon}</i>
                    </button>
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnRemoveUser" data-userid="${user.UserId}" title="Remove User">
                        <i class="md-icon">delete</i>
                    </button>
                </div>
            </div>`;
        }

        getLimitedUserEditorHtml(user, isNew = false) {
            const currentLimitedUserIds = new Set(this.config.LimitedUsers.map(u => u.UserId));
            const availableUsers = this.allUsers.filter(u => !currentLimitedUserIds.has(u.Id));
            let userSelectHtml = '';
            if (isNew) {
                if (availableUsers.length > 0) {
                    userSelectHtml = `<select is="emby-select" id="selectUser" class="edit-user-select" label="Select User:" required>${availableUsers.map(u => `<option value="${u.Id}" data-username="${u.Name}">${u.Name}</option>`).join('')}</select>`;
                } else {
                    userSelectHtml = '<p>No more users available to add.</p>';
                }
            } else {
                userSelectHtml = `<input is="emby-input" id="txtUsername" type="text" label="User:" value="${user.Username}" readonly />`;
            }

            const timeOptions = Array.from({ length: 48 }, (_, i) => `<option value="${i / 2}">${String(Math.floor(i / 2)).padStart(2, '0')}:${String((i % 2) * 30).padStart(2, '0')}</option>`).join('');
            const weekDayOptions = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((day, i) => `<option value="${i}">${day}</option>`).join('');
            const monthDayOptions = Array.from({ length: 28 }, (_, i) => `<option value="${i + 1}">${i + 1}</option>`).join('');
            const monthOptions = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'].map((m, i) => `<option value="${i + 1}">${m}</option>`).join('');
            const yearDayOptions = Array.from({ length: 31 }, (_, i) => `<option value="${i + 1}">${i + 1}</option>`).join('');


            return `
            <div class="user-editor" data-userid="${user.UserId}">
                <h3>${isNew ? 'Add New Limited User' : 'Editing: ' + user.Username}</h3>
                 <div class="inputContainer">${userSelectHtml}</div>

                <div class="emby-tabs-slider" style="margin: 1.5em 0 1em;">
                    <div class="emby-tabs">
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button is-active" data-tab-id="tab-limits">Limits</button>
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-schedule">Reset Schedule</button>
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-window">Time Window</button>
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-notifications">Notifications</button>
                    </div>
                </div>

                <!-- Limits Tab -->
                <div id="tab-limits" class="user-editor-tab-content">
                    <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-daily" ${user.EnableDailyLimit ? 'checked' : ''} /><span>Enable Daily Limit</span></label>
                         <input is="emby-input" class="edit-daily-minutes" type="number" label="Daily Limit (Minutes):" value="${user.DailyLimitMinutes || 120}" />
                    </div>
                    <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-weekly" ${user.EnableWeeklyLimit ? 'checked' : ''} /><span>Enable Weekly Limit</span></label>
                         <input is="emby-input" class="edit-weekly-hours" type="number" label="Weekly Limit (Hours):" value="${user.WeeklyLimitHours || 20}" />
                    </div>
                     <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-monthly" ${user.EnableMonthlyLimit ? 'checked' : ''} /><span>Enable Monthly Limit</span></label>
                         <input is="emby-input" class="edit-monthly-hours" type="number" label="Monthly Limit (Hours):" value="${user.MonthlyLimitHours || 80}" />
                    </div>
                     <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-yearly" ${user.EnableYearlyLimit ? 'checked' : ''} /><span>Enable Yearly Limit</span></label>
                         <input is="emby-input" class="edit-yearly-hours" type="number" label="Yearly Limit (Hours):" value="${user.YearlyLimitHours || 0}" />
                    </div>
                </div>

                <!-- Reset Schedule Tab -->
                <div id="tab-schedule" class="user-editor-tab-content hide">
                    <div class="inputContainer">
                        <select is="emby-select" class="edit-reset-time" label="Reset Time of Day:">${timeOptions}</select>
                        <div class="fieldDescription">This time applies to all daily, weekly, monthly, and yearly resets.</div>
                    </div>
                    <hr style="margin: 1.5em 0; border-color: #444;" />
                    <div class="inputContainer"><select is="emby-select" class="edit-weekly-reset-day" label="Weekly Reset Day:">${weekDayOptions}</select></div>
                    <div class="inputContainer"><select is="emby-select" class="edit-monthly-reset-day" label="Monthly Reset Day:">${monthDayOptions}</select></div>
                    <div style="display: flex; gap: 1em;">
                        <div class="inputContainer" style="flex-grow:1;"><select is="emby-select" class="edit-yearly-reset-month" label="Yearly Reset Month:">${monthOptions}</select></div>
                        <div class="inputContainer" style="flex-grow:1;"><select is="emby-select" class="edit-yearly-reset-day" label="Yearly Reset Day:">${yearDayOptions}</select></div>
                    </div>
                </div>

                <!-- Time Window Tab -->
                <div id="tab-window" class="user-editor-tab-content hide">
                    <div class="checkboxContainer">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-time-window" ${user.EnableTimeWindow ? 'checked' : ''} /><span>Restrict playback to a specific time window</span></label>
                    </div>
                    <div class="time-window-container">
                        <div style="display: flex; gap: 1em;">
                            <div class="inputContainer" style="flex-grow: 1;"><select is="emby-select" class="edit-window-start" label="From:">${timeOptions}</select></div>
                            <div class="inputContainer" style="flex-grow: 1;"><select is="emby-select" class="edit-window-end" label="To:">${timeOptions}</select></div>
                        </div>
                        <h3 style="margin-top: 1.5em; margin-bottom: 0.5em;">Allowed Days:</h3>
                        <div class="allowed-days-container" style="display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 0.5em;">
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="0" /><span>Sunday</span></label>
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="1" /><span>Monday</span></label>
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="2" /><span>Tuesday</span></label>
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="3" /><span>Wednesday</span></label>
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="4" /><span>Thursday</span></label>
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="5" /><span>Friday</span></label>
                            <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-allowed-day" data-day="6" /><span>Saturday</span></label>
                        </div>
                    </div>
                </div>

                <!-- Notifications Tab -->
                <div id="tab-notifications" class="user-editor-tab-content hide">
                     <div class="checkboxContainer">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-threshold-notifications" ${user.EnableThresholdNotifications ? 'checked' : ''} /><span>Enable notifications when nearing a limit</span></label>
                     </div>
                     <div class="inputContainer">
                        <input is="emby-input" class="edit-notification-thresholds" type="text" label="Notification Thresholds (%):" value="${user.NotificationThresholds || '80,95'}" />
                        <div class="fieldDescription">Comma-separated list of percentages (e.g., 50, 80, 95).</div>
                     </div>
                </div>

                <div class="user-editor-buttons">
                   <button is="emby-button" type="button" class="raised button-submit btn-save-user-inline" data-userid="${user.UserId}"><span>Save User</span></button>
                   <button is="emby-button" type="button" class="raised button-cancel btn-cancel-edit-user" data-userid="${user.UserId}"><span>Cancel</span></button>
                </div>
            </div>`;
        }

        saveUserInline(saveButton, userId) {
            const editorContainer = saveButton.closest('.user-editor');
            if (!editorContainer) return;

            const userData = {
                EnableDailyLimit: editorContainer.querySelector('.edit-enable-daily').checked,
                DailyLimitMinutes: parseInt(editorContainer.querySelector('.edit-daily-minutes').value) || 0,
                EnableWeeklyLimit: editorContainer.querySelector('.edit-enable-weekly').checked,
                WeeklyLimitHours: parseInt(editorContainer.querySelector('.edit-weekly-hours').value) || 0,
                EnableMonthlyLimit: editorContainer.querySelector('.edit-enable-monthly').checked,
                MonthlyLimitHours: parseInt(editorContainer.querySelector('.edit-monthly-hours').value) || 0,
                EnableYearlyLimit: editorContainer.querySelector('.edit-enable-yearly').checked,
                YearlyLimitHours: parseInt(editorContainer.querySelector('.edit-yearly-hours').value) || 0,

                ResetTimeOfDayHours: parseFloat(editorContainer.querySelector('.edit-reset-time').value),
                WeeklyResetDay: parseInt(editorContainer.querySelector('.edit-weekly-reset-day').value),
                MonthlyResetDay: parseInt(editorContainer.querySelector('.edit-monthly-reset-day').value),
                YearlyResetMonth: parseInt(editorContainer.querySelector('.edit-yearly-reset-month').value),
                YearlyResetDay: parseInt(editorContainer.querySelector('.edit-yearly-reset-day').value),

                EnableThresholdNotifications: editorContainer.querySelector('.edit-enable-threshold-notifications').checked,
                NotificationThresholds: editorContainer.querySelector('.edit-notification-thresholds').value,

                EnableTimeWindow: editorContainer.querySelector('.edit-enable-time-window').checked,
                WatchWindowStartHour: parseFloat(editorContainer.querySelector('.edit-window-start').value),
                WatchWindowEndHour: parseFloat(editorContainer.querySelector('.edit-window-end').value),
                AllowedDays: Array.from(editorContainer.querySelectorAll('.edit-allowed-day:checked')).map(cb => parseInt(cb.getAttribute('data-day')))
            };

            if (userId === newUserId) {
                const userSelect = editorContainer.querySelector('.edit-user-select');
                if (!userSelect || !userSelect.value) {
                    toast({ type: 'error', text: 'Please select a user.' });
                    return;
                }
                const selectedOption = userSelect.options[userSelect.selectedIndex];
                userData.UserId = selectedOption.value;
                userData.Username = selectedOption.getAttribute('data-username');
                userData.IsEnabled = true;
                this.config.LimitedUsers.push(userData);
                toast(`User ${userData.Username} added. Save all changes to apply.`);
            } else {
                const userToUpdate = this.config.LimitedUsers.find(u => u.UserId === userId);
                if (userToUpdate) {
                    Object.assign(userToUpdate, userData);
                    toast(`User ${userToUpdate.Username} updated. Save all changes to apply.`);
                }
            }

            this.editingUserId = null;
            this.renderLimitedUsers(this.view, this.config);
        }

        renderLimitedUsers(view, config) {
            const container = view.querySelector('#limitedUsersContainer');
            if (!config.LimitedUsers) {
                config.LimitedUsers = [];
            }

            getLimitedUsersStatus().then(userStatuses => {
                const statusMap = new Map(userStatuses.map(s => [s.UserId, s]));

                let listHtml = '';
                const usersToRender = [...config.LimitedUsers];

                if (this.editingUserId === newUserId) {
                    usersToRender.unshift({ UserId: newUserId, EnableDailyLimit: true, DailyLimitMinutes: 120, NotificationThresholds: '80,95', ResetTimeOfDayHours: 3, WeeklyResetDay: 0, MonthlyResetDay: 1, YearlyResetMonth: 1, YearlyResetDay: 1 });
                }

                if (usersToRender.length === 0) {
                    listHtml = '<div class="paper-card" style="padding: 1em;"><p>No users have been added to the watch time limiter.</p></div>';
                } else {
                    listHtml = '<div class="paper-card" style="padding: 1em;">' + usersToRender.map(user => {
                        if (user.UserId === this.editingUserId) {
                            return this.getLimitedUserEditorHtml(user, user.UserId === newUserId);
                        } else {
                            return this.getLimitedUserDisplayHtml(user, statusMap.get(user.UserId));
                        }
                    }).join('<hr style="margin: 1em 0; border-color: #444;" />') + '</div>';
                }

                container.innerHTML = listHtml;

                if (this.editingUserId) {
                    const editor = container.querySelector('.user-editor');
                    if (editor) {
                        const user = config.LimitedUsers.find(u => u.UserId === this.editingUserId) || usersToRender[0];

                        editor.querySelector('.edit-reset-time').value = user.ResetTimeOfDayHours || 3;
                        editor.querySelector('.edit-weekly-reset-day').value = user.WeeklyResetDay || 0;
                        editor.querySelector('.edit-monthly-reset-day').value = user.MonthlyResetDay || 1;
                        editor.querySelector('.edit-yearly-reset-month').value = user.YearlyResetMonth || 1;
                        editor.querySelector('.edit-yearly-reset-day').value = user.YearlyResetDay || 1;
                        editor.querySelector('.edit-window-start').value = user.WatchWindowStartHour || 0;
                        editor.querySelector('.edit-window-end').value = user.WatchWindowEndHour || 23.5;

                        const allowedDays = user.AllowedDays || [0, 1, 2, 3, 4, 5, 6];
                        allowedDays.forEach(day => {
                            const checkbox = editor.querySelector(`.edit-allowed-day[data-day="${day}"]`);
                            if (checkbox) {
                                checkbox.checked = true;
                            }
                        });

                        const timeWindowCheckbox = editor.querySelector('.edit-enable-time-window');
                        const timeWindowContainer = editor.querySelector('.time-window-container');
                        const updateTimeWindowVisibility = () => {
                            timeWindowContainer.classList.toggle('hide', !timeWindowCheckbox.checked);
                        };
                        timeWindowCheckbox.addEventListener('change', updateTimeWindowVisibility);
                        updateTimeWindowVisibility();
                    }
                }
            });
        }

        renderExclusionLists(view, config) {
            const usersContainer = view.querySelector('#excludedUsersContainer');
            const clientsContainer = view.querySelector('#excludedClientsContainer');
            const librariesContainer = view.querySelector('#excludedLibrariesContainer');

            usersContainer.innerHTML = this.allUsers.map(user => {
                const isChecked = (config.ExcludedUserIds || []).includes(user.Id);
                return `<label><input is="emby-checkbox" type="checkbox" class="excludedUser" data-userid="${user.Id}" ${isChecked ? 'checked' : ''} /><span>${user.Name}</span></label>`;
            }).join('');

            clientsContainer.innerHTML = this.allClients.map(client => {
                const isChecked = (config.ExcludedClients || []).some(c => c.toLowerCase() === client.toLowerCase());
                return `<label><input is="emby-checkbox" type="checkbox" class="excludedClient" data-client="${client}" ${isChecked ? 'checked' : ''} /><span>${client}</span></label>`;
            }).join('');

            const ignoredLibraryTypes = ['collections', 'playlists', 'boxsets'];
            const displayLibraries = this.allLibraries.filter(lib => !lib.CollectionType || !ignoredLibraryTypes.includes(lib.CollectionType.toLowerCase()));

            librariesContainer.innerHTML = displayLibraries.map(lib => {
                const isChecked = (config.ExcludedLibraryIds || []).includes(lib.Id);
                return `<label><input is="emby-checkbox" type="checkbox" class="excludedLibrary" data-library-id="${lib.Id}" ${isChecked ? 'checked' : ''} /><span>${lib.Name}</span></label>`;
            }).join('');
        }

        loadData(view) {
            loading.show();
            Promise.all([
                getPluginConfiguration(),
                ApiClient.getUsers(),
                getClientList(),
                ApiClient.getVirtualFolders()
            ]).then(([config, users, clients, virtualFolders]) => {
                this.config = config;
                this.allUsers = users;
                this.allClients = clients;
                this.allLibraries = virtualFolders.Items;

                view.querySelectorAll('[data-config-key]').forEach(el => {
                    const key = el.getAttribute('data-config-key');
                    const value = config[key];
                    if (el.type === 'checkbox') {
                        el.checked = value;
                    } else if (!Array.isArray(value)) {
                        el.value = value || '';
                    }
                });

                const portInput = view.querySelector('#numExternalWebServerPort');
                const port = portInput.value || 9988;
                view.querySelector('#netshCommand').textContent = `netsh http add urlacl url=http://*:${port}/ user="Everyone"`;

                if (config.EnableExternalWebServer) {
                    renderWebServerStatus(view);
                }

                this.editingUserId = null;
                this.renderLimitedUsers(view, this.config);
                this.renderExclusionLists(view, this.config);
                loading.hide();
            }).catch(err => {
                loading.hide();
                console.error('Error loading Watching Eye configuration page:', err);
                toast({
                    type: 'error',
                    text: 'There was an error loading page data. Please try refreshing the page.'
                });
            });
        }

        saveData(view) {
            loading.show();

            view.querySelectorAll('[data-config-key]').forEach(el => {
                const key = el.getAttribute('data-config-key');

                if (!Array.isArray(this.config[key])) {
                    if (el.type === 'checkbox') {
                        this.config[key] = el.checked;
                    } else if (el.type === 'number') {
                        this.config[key] = parseInt(el.value) || 0;
                    }
                    else {
                        this.config[key] = el.value;
                    }
                }
            });

            this.config.ExcludedUserIds = Array.from(view.querySelectorAll('.excludedUser:checked')).map(cb => cb.getAttribute('data-userid'));
            this.config.ExcludedClients = Array.from(view.querySelectorAll('.excludedClient:checked')).map(cb => cb.getAttribute('data-client'));
            this.config.ExcludedLibraryIds = Array.from(view.querySelectorAll('.excludedLibrary:checked')).map(cb => cb.getAttribute('data-library-id'));


            updatePluginConfiguration(this.config).then(result => {
                loading.hide();
                Dashboard.processPluginConfigurationUpdateResult(result);
                this.loadData(view);
            }).catch(() => {
                loading.hide();
                toast({ type: 'error', text: 'Error saving configuration.' });
            });
        }

        onResume(options) {
            super.onResume(options);
            this.loadData(this.view);
            this.watchStatusInterval = setInterval(() => {
                if (this.config.EnableWatchTimeLimiter && !this.editingUserId && document.querySelector('#limiterPage:not(.hide)')) {
                    this.renderLimitedUsers(this.view, this.config);
                }
            }, 10000);
        }

        onPause() {
            super.onPause();
            if (this.watchStatusInterval) {
                clearInterval(this.watchStatusInterval);
                this.watchStatusInterval = null;
            }
        }

        destroy() {
            if (this.watchStatusInterval) {
                clearInterval(this.watchStatusInterval);
                this.watchStatusInterval = null;
            }
            super.destroy();
        }
    };
});