    define(['baseView', 'loading', 'dialogHelper', 'toast', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-select'], function (BaseView, loading, dialogHelper, toast) {
        'use strict';

        const pluginId = "e8c3b1b3-4f56-4f38-a28a-2e6c5a043007";

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

        function getResetScheduleText(user) {
            switch (user.WatchTimeResetType) {
                case 'Daily': return `Resets Daily at ${user.WatchTimeResetTimeOfDayHours}:00`;
                case 'Weekly':
                    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
                    return `Resets Weekly on ${days[user.WatchTimeResetDayOfWeek]}`;
                case 'Minutes': return `Resets every ${user.WatchTimeResetIntervalMinutes} minutes`;
                case 'Allowance': return 'Allowance (Manual Reset)';
                default: return 'Reset schedule not set';
            }
        }

        function getTimeWindowText(user) {
            if (!user.EnableTimeWindow) {
                return '';
            }
            const start = (user.WatchWindowStartHour || 0).toString().padStart(2, '0') + ':00';
            const end = (user.WatchWindowEndHour || 0).toString().padStart(2, '0') + ':00';
            return `Plays between ${start} and ${end}`;
        }

        function renderLimitedUsers(view, config) {
            const container = view.querySelector('#limitedUsersContainer');
            if (!config.LimitedUsers) {
                config.LimitedUsers = [];
            }

            getLimitedUsersStatus().then(userStatuses => {
                const statusMap = new Map(userStatuses.map(s => [s.UserId, s]));

                let html = '<div class="paper-card" style="padding: 1em;">';
                if (config.LimitedUsers.length === 0) {
                    html += '<p>No users have been added to the watch time limiter.</p>';
                } else {
                    html += config.LimitedUsers.map(user => {
                        const status = statusMap.get(user.UserId);
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
                        <div class="listItem ${disabledClass}" style="display:flex; align-items: center; padding: 0.5em;">
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
                        </div>
                    `;
                    }).join('');
                }
                html += '</div>';
                container.innerHTML = html;
            });
        }

        function showUserLimitDialog(view, config, userId, callback) {
            const user = userId ? config.LimitedUsers.find(u => u.UserId === userId) : null;
            const isEdit = user != null;

            const dlg = dialogHelper.createDialog({ removeOnClose: true, size: 'small' });
            const template = view.querySelector('#userLimitDialogTemplate').content.cloneNode(true);
            dlg.appendChild(template);
            dialogHelper.open(dlg);

            dlg.querySelector('.dialogTitle').textContent = isEdit ? 'Edit User Limit' : 'Add Limited User';

            const selUser = dlg.querySelector('#selectUser');
            const txtUsername = dlg.querySelector('#txtUsername');

            if (isEdit) {
                dlg.querySelector('.selectUserContainer').classList.add('hide');
                dlg.querySelector('.txtUsernameContainer').classList.remove('hide');
                txtUsername.value = user.Username;
            } else {
                ApiClient.getUsers().then(users => {
                    const currentLimitedUserIds = new Set(config.LimitedUsers.map(u => u.UserId));
                    const availableUsers = users.filter(u => !currentLimitedUserIds.has(u.Id));
                    if (availableUsers.length > 0) {
                        selUser.innerHTML = availableUsers.map(u => `<option value="${u.Id}" data-username="${u.Name}">${u.Name}</option>`).join('');
                    } else {
                        selUser.innerHTML = '<option value="">No more users to add</option>';
                        form.querySelector('button[type="submit"]').disabled = true;
                    }
                });
            }

            const numUserWatchTime = dlg.querySelector('#numUserWatchTime');
            const selectResetType = dlg.querySelector('#selectWatchTimeResetType');
            const numResetMinutes = dlg.querySelector('#numWatchTimeResetIntervalMinutes');
            const selectResetHour = dlg.querySelector('#selectWatchTimeResetTimeOfDayHours');
            const selectResetDay = dlg.querySelector('#selectWatchTimeResetDayOfWeek');

            const chkEnableTimeWindow = dlg.querySelector('#chkEnableTimeWindow');
            const timeWindowContainer = dlg.querySelector('#timeWindowContainer');
            const selectStartHour = dlg.querySelector('#selectWatchWindowStartHour');
            const selectEndHour = dlg.querySelector('#selectWatchWindowEndHour');

            let hourOptions = '';
            for (let i = 0; i < 24; i++) {
                const time = i.toString().padStart(2, '0') + ':00';
                hourOptions += `<option value="${i}">${time}</option>`;
            }
            selectStartHour.innerHTML = hourOptions;
            selectEndHour.innerHTML = hourOptions;

            numUserWatchTime.value = isEdit ? user.WatchTimeLimitMinutes : 120;
            selectResetType.value = isEdit ? user.WatchTimeResetType : 'Daily';
            numResetMinutes.value = isEdit ? user.WatchTimeResetIntervalMinutes : 1440;
            selectResetHour.value = isEdit ? user.WatchTimeResetTimeOfDayHours : 3;
            selectResetDay.value = isEdit ? user.WatchTimeResetDayOfWeek : 0;

            chkEnableTimeWindow.checked = isEdit ? user.EnableTimeWindow : false;
            selectStartHour.value = isEdit ? (user.WatchWindowStartHour || 0) : 0;
            selectEndHour.value = isEdit ? (user.WatchWindowEndHour || 23) : 23;


            function updateDialogScheduleUI() {
                const resetType = selectResetType.value;
                dlg.querySelector('#resetMinutesContainer').classList.toggle('hide', resetType !== 'Minutes');
                dlg.querySelector('#resetDailyContainer').classList.toggle('hide', resetType !== 'Daily' && resetType !== 'Weekly');
                dlg.querySelector('#resetWeeklyContainer').classList.toggle('hide', resetType !== 'Weekly');
            }

            function updateDialogTimeWindowUI() {
                timeWindowContainer.classList.toggle('hide', !chkEnableTimeWindow.checked);
            }

            selectResetType.addEventListener('change', updateDialogScheduleUI);
            chkEnableTimeWindow.addEventListener('change', updateDialogTimeWindowUI);
            updateDialogScheduleUI();
            updateDialogTimeWindowUI();

            const form = dlg.querySelector('form');
            dlg.querySelector('.btnCancel').addEventListener('click', () => dialogHelper.close(dlg));

            form.addEventListener('submit', (e) => {
                e.preventDefault();

                const minutes = parseInt(numUserWatchTime.value);
                if (!minutes || minutes <= 0) {
                    toast({ type: 'error', text: 'Please enter a valid time limit.' });
                    return;
                }

                const userData = {
                    WatchTimeLimitMinutes: minutes,
                    WatchTimeResetType: selectResetType.value,
                    WatchTimeResetIntervalMinutes: parseInt(numResetMinutes.value) || 1440,
                    WatchTimeResetTimeOfDayHours: parseInt(selectResetHour.value) || 3,
                    WatchTimeResetDayOfWeek: parseInt(selectResetDay.value) || 0,
                    EnableTimeWindow: chkEnableTimeWindow.checked,
                    WatchWindowStartHour: parseInt(selectStartHour.value),
                    WatchWindowEndHour: parseInt(selectEndHour.value)
                };

                if (isEdit) {
                    const request = { ...userData, UserId: userId };
                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("WatchingEye/EditUserLimit"),
                        data: JSON.stringify(request),
                        contentType: 'application/json'
                    }).then(() => {
                        toast('User limit updated successfully.');
                        dialogHelper.close(dlg);
                        if (callback) callback();
                    }).catch(() => toast({ type: 'error', text: 'Error updating user limit.' }));

                } else {
                    const selectedOption = selUser.options[selUser.selectedIndex];
                    if (!selectedOption || !selectedOption.value) {
                        toast({ type: 'error', text: 'Please select a user.' });
                        return;
                    }
                    const newUser = {
                        ...userData,
                        UserId: selectedOption.value,
                        Username: selectedOption.getAttribute('data-username'),
                        IsEnabled: true
                    };
                    config.LimitedUsers.push(newUser);
                    toast(`User ${newUser.Username} added. Please click Save to apply changes.`);
                    dialogHelper.close(dlg);
                    if (callback) callback();
                }
            });
        }

        return class extends BaseView {
            constructor(view, params) {
                super(view, params);

                this.config = {};
                this.watchStatusInterval = null;

                view.querySelector('.watchingEyeForm').addEventListener('submit', (e) => {
                    e.preventDefault();
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
                    showUserLimitDialog(view, this.config, null, () => renderLimitedUsers(view, this.config));
                });

                view.querySelector('#btnResetAll').addEventListener('click', () => {
                    ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("WatchingEye/ResetAllUsersTime") }).then(() => {
                        toast('Reset time for all users.');
                        renderLimitedUsers(view, this.config);
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

                    if (buttonTarget.classList.contains('btnRemoveUser')) {
                        const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                        if (user) {
                            this.config.LimitedUsers = this.config.LimitedUsers.filter(u => u.UserId !== userId);
                            toast(`User ${user.Username} removed. Please click Save to apply changes.`);
                            renderLimitedUsers(view, this.config);
                        }
                        return;
                    }

                    if (buttonTarget.classList.contains('btnExtendTime')) {
                        const input = view.querySelector(`.extendTimeMinutes[data-userid="${userId}"]`);
                        const minutes = parseInt(input.value);

                        if (!userId || !minutes || minutes <= 0) {
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
                            renderLimitedUsers(view, this.config);
                        }).catch(() => toast({ type: 'error', text: 'Error extending time.' }));
                        return;
                    }

                    if (buttonTarget.classList.contains('btnEditUser')) {
                        showUserLimitDialog(view, this.config, userId, () => this.loadData(view));
                        return;
                    }

                    if (buttonTarget.classList.contains('btnToggleUserLimit')) {
                        ApiClient.ajax({
                            type: "POST",
                            url: ApiClient.getUrl("WatchingEye/ToggleUserLimit"),
                            data: JSON.stringify({ UserId: userId }),
                            contentType: 'application/json'
                        }).then(() => this.loadData(view))
                            .catch(() => toast({ type: 'error', text: 'Error toggling user limit.' }));
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
                            renderLimitedUsers(view, this.config);
                        }).catch(() => toast({ type: 'error', text: 'Error resetting time.' }));
                    }
                });

                this.loadData(this.view);
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
                    renderLimitedUsers(view, this.config);
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
                    if (this.config.EnableWatchTimeLimiter && document.querySelector('#limiterPage:not(.hide)')) {
                        renderLimitedUsers(this.view, this.config);
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
            }
        };
    });