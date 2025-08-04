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

    function getResetScheduleText(user) {
        switch (user.WatchTimeResetType) {
            case 'Daily':
                return `Resets Daily at ${formatTime(user.WatchTimeResetTimeOfDayHours)}`;
            case 'Weekly':
                const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
                return `Resets Weekly on ${days[user.WatchTimeResetDayOfWeek]} at ${formatTime(user.WatchTimeResetTimeOfDayHours)}`;
            case 'Minutes': return `Resets every ${user.WatchTimeResetIntervalMinutes} minutes`;
            case 'Allowance': return 'Allowance (Manual Reset)';
            default: return 'Reset schedule not set';
        }
    }

    function getTimeWindowText(user) {
        if (!user.EnableTimeWindow) {
            return '';
        }
        const start = formatTime(user.WatchWindowStartHour || 0);
        const end = formatTime(user.WatchWindowEndHour || 0);
        return `Plays between ${start} and ${end}`;
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
            let remainingTimeText;
            if (status) {
                const remainingMinutes = Math.floor(status.SecondsRemaining / 60);
                remainingTimeText = `Time Remaining: ${remainingMinutes} of ${user.WatchTimeLimitMinutes} mins`;
            } else {
                remainingTimeText = `Time Remaining: ${user.WatchTimeLimitMinutes} of ${user.WatchTimeLimitMinutes} mins`;
            }

            const isEnabled = user.IsEnabled !== false;
            const disabledClass = isEnabled ? '' : 'disabled-item';
            const statusText = isEnabled ? '' : ' (Disabled)';
            const toggleTitle = isEnabled ? 'Disable Limit' : 'Enable Limit';
            const toggleIcon = isEnabled ? 'power_settings_new' : 'block';

            const scheduleText = getResetScheduleText(user);
            const timeWindowText = getTimeWindowText(user);
            const combinedScheduleText = [scheduleText, timeWindowText].filter(Boolean).join('<br>');

            return `
                    <div class="listItem ${disabledClass}" style="display:flex; align-items: center; padding: 0.5em 0;">
                        <div class="listItemBody" style="flex-grow: 1;">
                            <h3 class="listItemTitle">${user.Username}${statusText}</h3>
                            <div class="listItemText">${combinedScheduleText}</div>
                            <div class="listItemText secondary">${remainingTimeText}</div>
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

            let timeOptions = '';
            for (let i = 0; i < 24; i++) {
                const hourDisplay = i.toString().padStart(2, '0');
                timeOptions += `<option value="${i}">${hourDisplay}:00</option>`;
                timeOptions += `<option value="${i + 0.5}">${hourDisplay}:30</option>`;
            }

            const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
            const dayOptions = days.map((day, i) => `<option value="${i}">${day}</option>`).join('');

            return `
                    <div class="user-editor" data-userid="${user.UserId}">
                        <h3>${isNew ? 'Add New Limited User' : 'Editing: ' + user.Username}</h3>
                        <div class="user-editor-flex-container">
                            <div>
                                <div class="inputContainer">${userSelectHtml}</div>
                                <div class="inputContainer">
                                    <input is="emby-input" class="edit-watch-time" type="number" label="Watch Time Limit (Minutes):" required value="${user.WatchTimeLimitMinutes || 120}" />
                                </div>
                            </div>
                            <div>
                                <h3 style="margin-top:0;">Allowed Watch Window</h3>
                                <div class="checkboxContainer">
                                    <label>
                                        <input is="emby-checkbox" type="checkbox" class="edit-enable-time-window" ${user.EnableTimeWindow ? 'checked' : ''} />
                                        <span>Restrict playback to a specific time window</span>
                                    </label>
                                </div>
                                <div class="time-window-container" style="display: flex; gap: 1em;">
                                    <div class="inputContainer" style="flex-grow: 1;">
                                        <select is="emby-select" class="edit-window-start" label="From:">${timeOptions}</select>
                                    </div>
                                    <div class="inputContainer" style="flex-grow: 1;">
                                        <select is="emby-select" class="edit-window-end" label="To:">${timeOptions}</select>
                                    </div>
                                </div>
                            </div>
                            <div>
                                <h3 style="margin-top:0;">Reset Schedule</h3>
                                <div class="inputContainer">
                                     <select is="emby-select" class="edit-reset-type" label="Reset Schedule:">
                                        <option value="Minutes">After a set number of minutes</option>
                                        <option value="Daily">Daily at a specific time</option>
                                        <option value="Weekly">Weekly on a specific day</option>
                                        <option value="Allowance">Allowance (Manual Reset Only)</option>
                                    </select>
                                </div>
                                <div class="inputContainer">
                                    <input is="emby-input" class="edit-reset-minutes" type="number" label="Reset Interval (Minutes):" value="${user.WatchTimeResetIntervalMinutes || 1440}" />
                                </div>
                                <div class="inputContainer">
                                    <select is="emby-select" class="edit-reset-hour" label="Reset Time:">${timeOptions}</select>
                                </div>
                                <div class="inputContainer">
                                     <select is="emby-select" class="edit-reset-day" label="Reset Day:">${dayOptions}</select>
                                </div>
                            </div>
                        </div>

                        <div class="user-editor-buttons">
                           <button is="emby-button" type="button" class="raised button-submit btn-save-user-inline" data-userid="${user.UserId}"><span>Save User</span></button>
                           <button is="emby-button" type="button" class="raised button-cancel btn-cancel-edit-user" data-userid="${user.UserId}"><span>Cancel</span></button>
                        </div>
                    </div>
                `;
        }

        saveUserInline(saveButton, userId) {
            const editorContainer = saveButton.closest('.user-editor');
            if (!editorContainer) return;

            const minutes = parseInt(editorContainer.querySelector('.edit-watch-time').value);
            if (!minutes || minutes <= 0) {
                toast({ type: 'error', text: 'Please enter a valid time limit.' });
                return;
            }

            const userData = {
                WatchTimeLimitMinutes: minutes,
                IsEnabled: true,
                WatchTimeResetType: editorContainer.querySelector('.edit-reset-type').value,
                WatchTimeResetIntervalMinutes: parseInt(editorContainer.querySelector('.edit-reset-minutes').value) || 1440,
                WatchTimeResetTimeOfDayHours: parseFloat(editorContainer.querySelector('.edit-reset-hour').value) || 3,
                WatchTimeResetDayOfWeek: parseInt(editorContainer.querySelector('.edit-reset-day').value) || 0,
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
                this.config.LimitedUsers.push(userData);
                toast(`User ${userData.Username} added. Save all changes to apply.`);
            } else {
                const userToUpdate = this.config.LimitedUsers.find(u => u.UserId === userId);
                if (userToUpdate) {
                    userData.IsEnabled = userToUpdate.IsEnabled; // Preserve existing enabled state
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
                    usersToRender.unshift({ UserId: newUserId });
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
                        const user = this.config.LimitedUsers.find(u => u.UserId === this.editingUserId) || {};

                        const resetTypeSelect = editor.querySelector('.edit-reset-type');
                        resetTypeSelect.value = user.WatchTimeResetType || 'Daily';

                        editor.querySelector('.edit-reset-hour').value = user.WatchTimeResetTimeOfDayHours || 3;
                        editor.querySelector('.edit-reset-day').value = user.WatchTimeResetDayOfWeek || 0;
                        editor.querySelector('.edit-window-start').value = user.WatchWindowStartHour || 0;
                        editor.querySelector('.edit-window-end').value = user.WatchWindowEndHour || 23.5;

                        const updateResetFields = () => {
                            const resetType = resetTypeSelect.value;
                            editor.querySelector('.edit-reset-minutes').closest('.inputContainer').classList.toggle('hide', resetType !== 'Minutes');
                            editor.querySelector('.edit-reset-hour').closest('.inputContainer').classList.toggle('hide', resetType !== 'Daily' && resetType !== 'Weekly');
                            editor.querySelector('.edit-reset-day').closest('.inputContainer').classList.toggle('hide', resetType !== 'Weekly');
                        };
                        resetTypeSelect.addEventListener('change', updateResetFields);
                        updateResetFields();

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
                toast('Settings saved.');
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