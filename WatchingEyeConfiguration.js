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

    function renderGlobalUserEditor(view) {
        const container = view.querySelector('#globalUserEditorContainer');

        const timeOptions = Array.from({ length: 48 }, (_, i) => `<option value="${i / 2}">${String(Math.floor(i / 2)).padStart(2, '0')}:${String((i % 2) * 30).padStart(2, '0')}</option>`).join('');
        const weekDayOptions = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((day, i) => `<option value="${i}">${day}</option>`).join('');
        const monthDayOptions = Array.from({ length: 28 }, (_, i) => `<option value="${i + 1}">${i + 1}</option>`).join('');
        const monthOptions = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'].map((m, i) => `<option value="${i + 1}">${m}</option>`).join('');
        const yearDayOptions = Array.from({ length: 31 }, (_, i) => `<option value="${i + 1}">${i + 1}</option>`).join('');

        const daysOfWeek = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
        let timeWindowsHtml = daysOfWeek.map((day, index) => `
            <div class="day-window-row" data-day="${index}" style="display: flex; align-items: center; gap: 1em; margin-bottom: 0.8em; flex-wrap: wrap;">
                <div style="width: 150px; flex-shrink: 0;">
                    <label class="checkboxContainer" style="padding:0;"><input is="emby-checkbox" type="checkbox" class="edit-day-window-enabled-global" /><span>${day}</span></label>
                </div>
                <div style="flex-grow:1; min-width: 150px;"><select is="emby-select" class="edit-day-window-start-global" label="From:">${timeOptions}</select></div>
                <div style="flex-grow:1; min-width: 150px;"><select is="emby-select" class="edit-day-window-end-global" label="To:">${timeOptions}</select></div>
            </div>
        `).join('');

        const editorHtml = `
            <div class="emby-tabs-slider" style="margin: 1.5em 0 1em;">
                <div class="emby-tabs">
                    <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button is-active" data-tab-id="tab-limits-global">Limits</button>
                    <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-schedule-global">Reset Schedule</button>
                    <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-window-global">Time Window</button>
                    <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-notifications-global">Notifications</button>
                </div>
            </div>

            <!-- Limits Tab -->
            <div id="tab-limits-global" class="user-editor-tab-content">
                 <div class="inputContainer">
                     <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" id="global_enable-daily" /><span>Enable Daily Limit</span></label>
                     <input is="emby-input" id="global_daily-minutes" type="number" label="Daily Limit (Minutes):" />
                </div>
                <div class="inputContainer">
                     <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" id="global_enable-weekly" /><span>Enable Weekly Limit</span></label>
                     <input is="emby-input" id="global_weekly-hours" type="number" label="Weekly Limit (Hours):" />
                </div>
                 <div class="inputContainer">
                     <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" id="global_enable-monthly" /><span>Enable Monthly Limit</span></label>
                     <input is="emby-input" id="global_monthly-hours" type="number" label="Monthly Limit (Hours):" />
                </div>
                 <div class="inputContainer">
                     <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" id="global_enable-yearly" /><span>Enable Yearly Limit</span></label>
                     <input is="emby-input" id="global_yearly-hours" type="number" label="Yearly Limit (Hours):" />
                </div>
            </div>

            <!-- Reset Schedule Tab -->
            <div id="tab-schedule-global" class="user-editor-tab-content hide">
                <div class="inputContainer">
                    <select is="emby-select" id="global_reset-time" label="Reset Time of Day:">${timeOptions}</select>
                    <div class="fieldDescription">This time applies to all daily, weekly, monthly, and yearly resets.</div>
                </div>
                <hr style="margin: 1.5em 0; border-color: #444;" />
                <div class="inputContainer"><select is="emby-select" id="global_weekly-reset-day" label="Weekly Reset Day:">${weekDayOptions}</select></div>
                <div class="inputContainer"><select is="emby-select" id="global_monthly-reset-day" label="Monthly Reset Day:">${monthDayOptions}</select></div>
                <div style="display: flex; gap: 1em;">
                    <div class="inputContainer" style="flex-grow:1;"><select is="emby-select" id="global_yearly-reset-month" label="Yearly Reset Month:">${monthOptions}</select></div>
                    <div class="inputContainer" style="flex-grow:1;"><select is="emby-select" id="global_yearly-reset-day" label="Yearly Reset Day:">${yearDayOptions}</select></div>
                </div>
            </div>

            <!-- Time Window Tab -->
            <div id="tab-window-global" class="user-editor-tab-content hide">
                <div class="checkboxContainer">
                    <label><input is="emby-checkbox" type="checkbox" id="global_enable-time-windows" /><span>Restrict playback to specific time windows per day</span></label>
                    <div class="fieldDescription">Define allowed playback times for each day of the week. If a day is not enabled, playback will be blocked for that entire day.</div>
                </div>
                <div id="global_time-windows-container" style="margin-top: 1.5em; border-top: 1px solid #444; padding-top: 1.5em;">
                    ${timeWindowsHtml}
                </div>
            </div>
            
            <!-- Notifications Tab -->
            <div id="tab-notifications-global" class="user-editor-tab-content hide">
                 <div class="checkboxContainer">
                    <label><input is="emby-checkbox" type="checkbox" id="global_enable-threshold-notifications" /><span>Enable notifications when nearing a limit</span></label>
                 </div>
                 <div class="inputContainer">
                    <input is="emby-input" id="global_notification-thresholds" type="text" label="Notification Thresholds (%):" />
                    <div class="fieldDescription">Comma-separated list of percentages (e.g., 50, 80, 95).</div>
                 </div>
            </div>
        `;
        container.innerHTML = editorHtml;

        container.querySelector('.emby-tabs').addEventListener('click', (e) => {
            const buttonTarget = e.target.closest('.user-editor-tab-button');
            if (!buttonTarget) return;
            e.preventDefault();
            const tabId = buttonTarget.getAttribute('data-tab-id');
            container.querySelectorAll('.user-editor-tab-button').forEach(btn => btn.classList.remove('is-active'));
            buttonTarget.classList.add('is-active');
            container.querySelectorAll('.user-editor-tab-content').forEach(content => content.classList.toggle('hide', content.id !== tabId));
        });
    }

    function populateGlobalEditor(view, config) {
        view.querySelector('#global_enable-daily').checked = config.EnableDailyLimit;
        view.querySelector('#global_daily-minutes').value = config.DailyLimitMinutes;
        view.querySelector('#global_enable-weekly').checked = config.EnableWeeklyLimit;
        view.querySelector('#global_weekly-hours').value = config.WeeklyLimitHours;
        view.querySelector('#global_enable-monthly').checked = config.EnableMonthlyLimit;
        view.querySelector('#global_monthly-hours').value = config.MonthlyLimitHours;
        view.querySelector('#global_enable-yearly').checked = config.EnableYearlyLimit;
        view.querySelector('#global_yearly-hours').value = config.YearlyLimitHours;

        view.querySelector('#global_reset-time').value = config.ResetTimeOfDayHours;
        view.querySelector('#global_weekly-reset-day').value = config.WeeklyResetDay;
        view.querySelector('#global_monthly-reset-day').value = config.MonthlyResetDay;
        view.querySelector('#global_yearly-reset-month').value = config.YearlyResetMonth;
        view.querySelector('#global_yearly-reset-day').value = config.YearlyResetDay;

        view.querySelector('#global_enable-threshold-notifications').checked = config.EnableThresholdNotifications;
        view.querySelector('#global_notification-thresholds').value = config.NotificationThresholds;

        view.querySelector('#global_enable-time-windows').checked = config.EnableTimeWindows;

        if (!config.TimeWindows) config.TimeWindows = [];

        view.querySelectorAll('#global_time-windows-container .day-window-row').forEach(row => {
            const dayIndex = parseInt(row.getAttribute('data-day'));
            const dayRule = config.TimeWindows.find(w => w.Day === dayIndex);

            if (dayRule) {
                row.querySelector('.edit-day-window-enabled-global').checked = dayRule.IsEnabled;
                row.querySelector('.edit-day-window-start-global').value = dayRule.StartHour;
                row.querySelector('.edit-day-window-end-global').value = dayRule.EndHour;
            } else {
                row.querySelector('.edit-day-window-enabled-global').checked = false;
                row.querySelector('.edit-day-window-start-global').value = 7;
                row.querySelector('.edit-day-window-end-global').value = 20;
            }
        });

        const timeWindowsContainer = view.querySelector('#global_time-windows-container');
        const mainToggle = view.querySelector('#global_enable-time-windows');
        const updateVisibility = () => {
            timeWindowsContainer.style.display = mainToggle.checked ? '' : 'none';
        };
        mainToggle.addEventListener('change', updateVisibility);
        updateVisibility();
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
            this.editingLibraryId = null;

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

                    view.querySelectorAll('#notificationsPage, #limiterPage, #loggingPage, #remoteAccessPage, #libraryRestrictionsPage').forEach(page => {
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

            view.querySelector('#libraryRestrictionsContainer').addEventListener('click', (e) => {
                const buttonTarget = e.target.closest('button');
                if (!buttonTarget) return;

                const libraryId = buttonTarget.getAttribute('data-libraryid');
                if (!libraryId) return;

                if (buttonTarget.classList.contains('btnEditLibraryRestriction')) {
                    if (this.editingLibraryId || this.editingUserId) return;
                    this.editingLibraryId = libraryId;
                    this.renderLibraryRestrictions(this.view, this.config);
                    return;
                }

                if (buttonTarget.classList.contains('btn-cancel-edit-library')) {
                    this.editingLibraryId = null;
                    this.renderLibraryRestrictions(this.view, this.config);
                    return;
                }

                if (buttonTarget.classList.contains('btn-save-library-inline')) {
                    this.saveLibraryRestrictionInline(buttonTarget, libraryId);
                    return;
                }
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

        getLibraryRestrictionDisplayHtml(library, restriction) {
            if (!restriction || !restriction.IsEnabled) {
                return `
            <div class="listItem" style="display:flex; align-items: center; padding: 0.5em 0;">
                <div class="listItemBody" style="flex-grow: 1;">
                    <h3 class="listItemTitle">${library.Name}</h3>
                    <div class="listItemText secondary">No restriction active.</div>
                </div>
                <button is="emby-button" type="button" class="raised mini btnEditLibraryRestriction" data-libraryid="${library.Id}" title="Add Restriction">
                    <span>Add Restriction</span>
                </button>
            </div>`;
            }

            const timeWindowText = `Plays between ${formatTime(restriction.StartTime || 0)} and ${formatTime(restriction.EndTime || 0)}`;
            const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
            const allowedDays = restriction.AllowedDays && restriction.AllowedDays.length < 7
                ? restriction.AllowedDays.map(d => days[d]).join(', ')
                : 'All Days';

            return `
        <div class="listItem" style="display:flex; align-items: center; padding: 0.5em 0;">
            <div class="listItemBody" style="flex-grow: 1;">
                <h3 class="listItemTitle">${library.Name}</h3>
                <div class="listItemText">${timeWindowText}</div>
                <div class="listItemText secondary">On: ${allowedDays}</div>
            </div>
            <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnEditLibraryRestriction" data-libraryid="${library.Id}" title="Edit Restriction">
                <i class="md-icon">edit</i>
            </button>
        </div>`;
        }

        getLibraryRestrictionEditorHtml(library, restriction) {
            const isNew = !restriction;
            const r = restriction || {
                IsEnabled: false,
                StartTime: 8,
                EndTime: 22,
                AllowedDays: [0, 1, 2, 3, 4, 5, 6],
                BlockMessage: "Playback from this library is not allowed at this time."
            };

            const timeOptions = Array.from({ length: 48 }, (_, i) => `<option value="${i / 2}">${String(Math.floor(i / 2)).padStart(2, '0')}:${String((i % 2) * 30).padStart(2, '0')}</option>`).join('');

            return `
            <div class="user-editor" data-libraryid="${library.Id}">
                <h3>Editing: ${library.Name}</h3>
                <div class="checkboxContainer">
                    <label><input is="emby-checkbox" type="checkbox" class="edit-lib-enabled" ${r.IsEnabled ? 'checked' : ''} /><span>Enable Time Restriction for this Library</span></label>
                </div>
                <div class="inputContainer">
                    <input is="emby-input" class="edit-lib-message" type="text" label="Block Message:" value="${r.BlockMessage}" />
                    <div class="fieldDescription">Message shown to the user when playback is blocked.</div>
                </div>
                <div style="display: flex; gap: 1em; margin-top: 1em;">
                    <div class="inputContainer" style="flex-grow: 1;"><select is="emby-select" class="edit-lib-start" label="From:">${timeOptions}</select></div>
                    <div class="inputContainer" style="flex-grow: 1;"><select is="emby-select" class="edit-lib-end" label="To:">${timeOptions}</select></div>
                </div>
                <h3 style="margin-top: 1.5em; margin-bottom: 0.5em;">Allowed Days:</h3>
                <div class="allowed-days-container" style="display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 0.5em;">
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="0" /><span>Sunday</span></label>
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="1" /><span>Monday</span></label>
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="2" /><span>Tuesday</span></label>
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="3" /><span>Wednesday</span></label>
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="4" /><span>Thursday</span></label>
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="5" /><span>Friday</span></label>
                    <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-lib-allowed-day" data-day="6" /><span>Saturday</span></label>
                </div>
                <div class="user-editor-buttons">
                   <button is="emby-button" type="button" class="raised button-submit btn-save-library-inline" data-libraryid="${library.Id}"><span>Save Settings</span></button>
                   <button is="emby-button" type="button" class="raised button-cancel btn-cancel-edit-library" data-libraryid="${library.Id}"><span>Cancel</span></button>
                </div>
            </div>`;
        }

        saveLibraryRestrictionInline(saveButton, libraryId) {
            const editorContainer = saveButton.closest('.user-editor');
            if (!editorContainer) return;

            const library = this.allLibraries.find(l => l.Id === libraryId);
            if (!library) return;

            let restriction = this.config.LibraryTimeRestrictions.find(r => r.LibraryId === libraryId);
            if (!restriction) {
                restriction = { LibraryId: libraryId, LibraryName: library.Name };
                this.config.LibraryTimeRestrictions.push(restriction);
            }

            restriction.LibraryName = library.Name;
            restriction.IsEnabled = editorContainer.querySelector('.edit-lib-enabled').checked;
            restriction.StartTime = parseFloat(editorContainer.querySelector('.edit-lib-start').value);
            restriction.EndTime = parseFloat(editorContainer.querySelector('.edit-lib-end').value);
            restriction.BlockMessage = editorContainer.querySelector('.edit-lib-message').value;
            restriction.AllowedDays = Array.from(editorContainer.querySelectorAll('.edit-lib-allowed-day:checked')).map(cb => parseInt(cb.getAttribute('data-day')));

            toast(`Settings for '${library.Name}' updated. Click the main Save button to apply.`);
            this.editingLibraryId = null;
            this.renderLibraryRestrictions(this.view, this.config);
        }

        renderLibraryRestrictions(view, config) {
            const container = view.querySelector('#libraryRestrictionsContainer');
            if (!config.LibraryTimeRestrictions) {
                config.LibraryTimeRestrictions = [];
            }

            const ignoredLibraryTypes = ['collections', 'playlists', 'boxsets'];
            const displayLibraries = this.allLibraries.filter(lib => !lib.CollectionType || !ignoredLibraryTypes.includes(lib.CollectionType.toLowerCase()));

            let listHtml = displayLibraries.map(library => {
                const restriction = config.LibraryTimeRestrictions.find(r => r.LibraryId === library.Id);
                if (this.editingLibraryId === library.Id) {
                    return this.getLibraryRestrictionEditorHtml(library, restriction);
                } else {
                    return this.getLibraryRestrictionDisplayHtml(library, restriction);
                }
            }).join('<hr style="margin: 1em 0; border-color: #444;" />');

            container.innerHTML = `<div class="paper-card" style="padding: 1em;">${listHtml}</div>`;

            if (this.editingLibraryId) {
                const editor = container.querySelector(`.user-editor[data-libraryid="${this.editingLibraryId}"]`);
                if (editor) {
                    const restriction = config.LibraryTimeRestrictions.find(r => r.LibraryId === this.editingLibraryId) || {};
                    editor.querySelector('.edit-lib-start').value = restriction.StartTime === 0 ? 0 : restriction.StartTime || 8;
                    editor.querySelector('.edit-lib-end').value = restriction.EndTime === 0 ? 0 : restriction.EndTime || 22;

                    const allowedDays = restriction.AllowedDays || [0, 1, 2, 3, 4, 5, 6];
                    allowedDays.forEach(day => {
                        const checkbox = editor.querySelector(`.edit-lib-allowed-day[data-day="${day}"]`);
                        if (checkbox) checkbox.checked = true;
                    });
                }
            }
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

            let timeWindowText = '';
            if (user.EnableTimeWindows && user.TimeWindows) {
                const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
                const activeWindows = user.TimeWindows
                    .filter(w => w.IsEnabled)
                    .map(w => `${days[w.Day]}: ${formatTime(w.StartHour)}-${formatTime(w.EndHour)}`)
                    .join('; ');
                if (activeWindows) {
                    timeWindowText = `Plays: ${activeWindows}`;
                }
            }

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

            const daysOfWeek = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
            let timeWindowsHtml = daysOfWeek.map((day, index) => `
                <div class="day-window-row" data-day="${index}" style="display: flex; align-items: center; gap: 1em; margin-bottom: 0.8em; flex-wrap: wrap;">
                    <div style="width: 150px; flex-shrink: 0;">
                        <label class="checkboxContainer" style="padding:0;"><input is="emby-checkbox" type="checkbox" class="edit-day-window-enabled" /><span>${day}</span></label>
                    </div>
                    <div style="flex-grow:1; min-width: 150px;"><select is="emby-select" class="edit-day-window-start" label="From:">${timeOptions}</select></div>
                    <div style="flex-grow:1; min-width: 150px;"><select is="emby-select" class="edit-day-window-end" label="To:">${timeOptions}</select></div>
                </div>
            `).join('');


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
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-time-windows" ${user.EnableTimeWindows ? 'checked' : ''} /><span>Restrict playback to specific time windows per day</span></label>
                        <div class="fieldDescription">Define allowed playback times for each day of the week. If a day is not enabled, playback will be blocked for that entire day.</div>
                    </div>
                    <div class="time-windows-container" style="margin-top: 1.5em; border-top: 1px solid #444; padding-top: 1.5em;">
                        ${timeWindowsHtml}
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

                EnableTimeWindows: editorContainer.querySelector('.edit-enable-time-windows').checked,
                TimeWindows: []
            };

            editorContainer.querySelectorAll('.day-window-row').forEach(row => {
                const dayRule = {
                    Day: parseInt(row.getAttribute('data-day')),
                    IsEnabled: row.querySelector('.edit-day-window-enabled').checked,
                    StartHour: parseFloat(row.querySelector('.edit-day-window-start').value),
                    EndHour: parseFloat(row.querySelector('.edit-day-window-end').value)
                };
                userData.TimeWindows.push(dayRule);
            });

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
                    usersToRender.unshift({ UserId: newUserId, EnableDailyLimit: true, DailyLimitMinutes: 120, NotificationThresholds: '80,95', ResetTimeOfDayHours: 3, WeeklyResetDay: 0, MonthlyResetDay: 1, YearlyResetMonth: 1, YearlyResetDay: 1, TimeWindows: [] });
                }

                if (usersToRender.length === 0) {
                    listHtml = '<p>No users have been added to the watch time limiter.</p>';
                } else {
                    listHtml = usersToRender.map(user => {
                        if (user.UserId === this.editingUserId) {
                            return this.getLimitedUserEditorHtml(user, user.UserId === newUserId);
                        } else {
                            return this.getLimitedUserDisplayHtml(user, statusMap.get(user.UserId));
                        }
                    }).join('<hr style="margin: 1em 0; border-color: #444;" />');
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

                        if (!user.TimeWindows) user.TimeWindows = [];

                        const timeWindowsContainer = editor.querySelector('.time-windows-container');
                        if (timeWindowsContainer) {
                            editor.querySelectorAll('.day-window-row').forEach(row => {
                                const dayIndex = parseInt(row.getAttribute('data-day'));
                                const dayRule = user.TimeWindows.find(w => w.Day === dayIndex);

                                if (dayRule) {
                                    row.querySelector('.edit-day-window-enabled').checked = dayRule.IsEnabled;
                                    row.querySelector('.edit-day-window-start').value = dayRule.StartHour;
                                    row.querySelector('.edit-day-window-end').value = dayRule.EndHour;
                                } else {
                                    row.querySelector('.edit-day-window-enabled').checked = false;
                                    row.querySelector('.edit-day-window-start').value = 7;
                                    row.querySelector('.edit-day-window-end').value = 20;
                                }
                            });
                        }

                        const mainToggle = editor.querySelector('.edit-enable-time-windows');
                        const updateVisibility = () => {
                            timeWindowsContainer.style.display = mainToggle.checked ? '' : 'none';
                        };
                        mainToggle.addEventListener('change', updateVisibility);
                        updateVisibility();
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

                if (!this.config.GlobalLimitedUser) {
                    this.config.GlobalLimitedUser = { TimeWindows: [] };
                }

                view.querySelectorAll('[data-config-key]').forEach(el => {
                    const key = el.getAttribute('data-config-key');
                    const value = config[key];
                    if (el.type === 'checkbox') {
                        el.checked = value;
                    } else if (!Array.isArray(value)) {
                        el.value = value || '';
                    }
                });

                renderGlobalUserEditor(view);
                populateGlobalEditor(view, this.config.GlobalLimitedUser);
                const chkEnableGlobal = view.querySelector('#chkEnableGlobalLimit');
                const globalEditor = view.querySelector('#globalUserEditorContainer');
                chkEnableGlobal.addEventListener('change', () => {
                    globalEditor.classList.toggle('hide', !chkEnableGlobal.checked);
                });
                globalEditor.classList.toggle('hide', !chkEnableGlobal.checked);

                const portInput = view.querySelector('#numExternalWebServerPort');
                const port = portInput.value || 9988;
                view.querySelector('#netshCommand').textContent = `netsh http add urlacl url=http://*:${port}/ user="Everyone"`;

                if (config.EnableExternalWebServer) {
                    renderWebServerStatus(view);
                }

                this.editingUserId = null;
                this.editingLibraryId = null;
                this.renderLimitedUsers(view, this.config);
                this.renderExclusionLists(view, this.config);
                this.renderLibraryRestrictions(view, this.config);
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

            const globalConfig = this.config.GlobalLimitedUser;
            globalConfig.EnableDailyLimit = view.querySelector('#global_enable-daily').checked;
            globalConfig.DailyLimitMinutes = parseInt(view.querySelector('#global_daily-minutes').value) || 0;
            globalConfig.EnableWeeklyLimit = view.querySelector('#global_enable-weekly').checked;
            globalConfig.WeeklyLimitHours = parseInt(view.querySelector('#global_weekly-hours').value) || 0;
            globalConfig.EnableMonthlyLimit = view.querySelector('#global_enable-monthly').checked;
            globalConfig.MonthlyLimitHours = parseInt(view.querySelector('#global_monthly-hours').value) || 0;
            globalConfig.EnableYearlyLimit = view.querySelector('#global_enable-yearly').checked;
            globalConfig.YearlyLimitHours = parseInt(view.querySelector('#global_yearly-hours').value) || 0;
            globalConfig.ResetTimeOfDayHours = parseFloat(view.querySelector('#global_reset-time').value);
            globalConfig.WeeklyResetDay = parseInt(view.querySelector('#global_weekly-reset-day').value);
            globalConfig.MonthlyResetDay = parseInt(view.querySelector('#global_monthly-reset-day').value);
            globalConfig.YearlyResetMonth = parseInt(view.querySelector('#global_yearly-reset-month').value);
            globalConfig.YearlyResetDay = parseInt(view.querySelector('#global_yearly-reset-day').value);
            globalConfig.EnableThresholdNotifications = view.querySelector('#global_enable-threshold-notifications').checked;
            globalConfig.NotificationThresholds = view.querySelector('#global_notification-thresholds').value;
            globalConfig.EnableTimeWindows = view.querySelector('#global_enable-time-windows').checked;
            globalConfig.TimeWindows = [];
            view.querySelectorAll('#global_time-windows-container .day-window-row').forEach(row => {
                globalConfig.TimeWindows.push({
                    Day: parseInt(row.getAttribute('data-day')),
                    IsEnabled: row.querySelector('.edit-day-window-enabled-global').checked,
                    StartHour: parseFloat(row.querySelector('.edit-day-window-start-global').value),
                    EndHour: parseFloat(row.querySelector('.edit-day-window-end-global').value)
                });
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