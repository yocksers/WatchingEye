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

    function getLogEvents() {
        return ApiClient.getJSON(ApiClient.getUrl("WatchingEye/GetLogEvents"));
    }

    function clearLogs() {
        return ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("WatchingEye/ClearLogs") });
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

    return class extends BaseView {
        constructor(view, params) {
            super(view, params);

            this.config = {};
            this.watchStatusInterval = null;
            this.editingUserId = null;
            this.allUsers = [];

            ApiClient.getUsers().then(users => {
                this.allUsers = users;
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
                    view.querySelectorAll('#notificationsPage, #limiterPage, #loggingPage').forEach(page => {
                        page.classList.toggle('hide', page.id !== targetId);
                    });

                    if (targetId === 'loggingPage') {
                        renderLogs(view);
                    }
                }
            });

            view.querySelector('#btnAddLimitedUser').addEventListener('click', () => {
                if (this.editingUserId) return;
                this.editingUserId = newUserId;
                this.renderLimitedUsers(this.view, this.config);
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


            this.loadData(this.view);
        }

        getLimitedUserDisplayHtml(user, status) {
            const isEnabled = user.IsEnabled !== false;
            const disabledClass = isEnabled ? '' : 'disabled-item';
            const statusText = isEnabled ? '' : ' (Disabled)';
            const toggleTitle = isEnabled ? 'Disable Limit' : 'Enable Limit';
            const toggleIcon = isEnabled ? 'power_settings_new' : 'block';

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
                        <div class="listItemText secondary">${remainingTimeText}</div>
                        <div class="listItemText">${timeWindowText}</div>
                    </div>
                    <div style="display: flex; align-items: center; gap: 0.5em; margin-left: 1em;">
                        <input is="emby-input" type="number" class="extendTimeMinutes" placeholder="Mins" value="30" style="width: 80px;" data-userid="${user.UserId}" />
                        <button is="emby-button" type="button" class="raised mini btnExtendTime" data-userid="${user.UserId}" title="Extend Time">
                            <span>Extend</span>
                        </button>
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
                        <div class="time-window-container" style="display: flex; gap: 1em;">
                            <div class="inputContainer" style="flex-grow: 1;"><select is="emby-select" class="edit-window-start" label="From:">${timeOptions}</select></div>
                            <div class="inputContainer" style="flex-grow: 1;"><select is="emby-select" class="edit-window-end" label="To:">${timeOptions}</select></div>
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
                WatchWindowEndHour: parseFloat(editorContainer.querySelector('.edit-window-end').value)
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

        loadData(view) {
            loading.show();
            getPluginConfiguration().then(config => {
                this.config = config;
                view.querySelectorAll('[data-config-key]').forEach(el => {
                    const key = el.getAttribute('data-config-key');
                    const value = config[key];
                    if (el.type === 'checkbox') {
                        el.checked = value;
                    } else {
                        el.value = value || '';
                    }
                });

                this.editingUserId = null;
                this.renderLimitedUsers(view, this.config);
                loading.hide();
            });
        }

        saveData(view) {
            loading.show();

            view.querySelectorAll('[data-config-key]').forEach(el => {
                const key = el.getAttribute('data-config-key');

                if (Object.prototype.hasOwnProperty.call(this.config, key) && !Array.isArray(this.config[key])) {
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