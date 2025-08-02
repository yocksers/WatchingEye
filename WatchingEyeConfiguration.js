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
                    let remainingTimeText = 'Status unavailable.';
                    if (status) {
                        const remainingMinutes = Math.floor(status.SecondsRemaining / 60);
                        remainingTimeText = `Time Remaining: ${remainingMinutes} minutes`;
                    }

                    const isEnabled = user.IsEnabled !== false; // Default to true
                    const disabledClass = isEnabled ? '' : 'disabled-item';
                    const statusText = isEnabled ? '' : ' (Disabled)';
                    const toggleTitle = isEnabled ? 'Disable Limit' : 'Enable Limit';
                    const toggleIcon = isEnabled ? 'power_settings_new' : 'block';


                    return `
                        <div class="listItem ${disabledClass}" style="display:flex; align-items: center; padding: 0.5em;">
                            <div class="listItemBody" style="flex-grow: 1;">
                                <h3 class="listItemTitle">${user.Username}</h3>
                                <div class="listItemText">Limit: ${user.WatchTimeLimitMinutes} minutes${statusText}</div>
                                <div class="listItemText secondary">${remainingTimeText}</div>
                            </div>
                            <div style="display: flex; align-items: center; gap: 0.5em; margin-left: 1em;">
                                <input is="emby-input" type="number" class="extendTimeMinutes" placeholder="Mins" value="30" style="width: 80px;" data-userid="${user.UserId}" />
                                <button is="emby-button" type="button" class="raised mini btnExtendTime" data-userid="${user.UserId}" title="Extend Time">
                                    <span>Extend</span>
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

    function showAddUserDialog(view, config) {
        const dlg = dialogHelper.createDialog({
            removeOnClose: true,
            size: 'small'
        });

        const template = view.querySelector('#limitedUserDialogTemplate').content.cloneNode(true);
        dlg.appendChild(template);
        dialogHelper.open(dlg);

        const selUser = dlg.querySelector('#selectUser');
        const numUserWatchTime = dlg.querySelector('#numUserWatchTime');
        const form = dlg.querySelector('form');

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

        dlg.querySelector('.btnCancel').addEventListener('click', () => dialogHelper.close(dlg));

        form.addEventListener('submit', (e) => {
            e.preventDefault();

            const selectedOption = selUser.options[selUser.selectedIndex];
            if (!selectedOption || !selectedOption.value) {
                toast({ type: 'error', text: 'Please select a user.' });
                return;
            }

            const newUser = {
                UserId: selectedOption.value,
                Username: selectedOption.getAttribute('data-username'),
                WatchTimeLimitMinutes: parseInt(numUserWatchTime.value) || 120,
                IsEnabled: true
            };

            if (!config.LimitedUsers) {
                config.LimitedUsers = [];
            }
            config.LimitedUsers.push(newUser);

            renderLimitedUsers(view, config);
            toast(`User ${newUser.Username} added.`);
            dialogHelper.close(dlg);
        });
    }

    function updateResetScheduleUI(view) {
        const resetType = view.querySelector('#selectWatchTimeResetType').value;
        view.querySelector('#resetMinutesContainer').classList.toggle('hide', resetType !== 'Minutes');
        view.querySelector('#resetDailyContainer').classList.toggle('hide', resetType === 'Minutes');
        view.querySelector('#resetWeeklyContainer').classList.toggle('hide', resetType !== 'Weekly');
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
                    view.querySelectorAll('#notificationsPage, #limiterPage').forEach(page => {
                        page.classList.toggle('hide', page.id !== targetId);
                    });
                }
            });

            view.querySelector('#btnAddLimitedUser').addEventListener('click', () => {
                showAddUserDialog(view, this.config);
            });

            view.querySelector('#selectWatchTimeResetType').addEventListener('change', () => {
                updateResetScheduleUI(view);
            });

            view.querySelector('#limitedUsersContainer').addEventListener('click', (e) => {
                const removeBtn = e.target.closest('.btnRemoveUser');
                if (removeBtn) {
                    const userId = removeBtn.getAttribute('data-userid');
                    const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                    if (user) {
                        this.config.LimitedUsers = this.config.LimitedUsers.filter(u => u.UserId !== userId);
                        renderLimitedUsers(view, this.config);
                        toast(`User ${user.Username} removed.`);
                    }
                    return;
                }

                const extendBtn = e.target.closest('.btnExtendTime');
                if (extendBtn) {
                    const userId = extendBtn.getAttribute('data-userid');
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
                        const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                        toast(`Time extended for ${user.Username} by ${minutes} minutes.`);
                        renderLimitedUsers(view, this.config);
                    }).catch(() => {
                        toast({ type: 'error', text: 'Error extending time.' });
                    });
                    return;
                }

                const toggleBtn = e.target.closest('.btnToggleUserLimit');
                if (toggleBtn) {
                    const userId = toggleBtn.getAttribute('data-userid');
                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("WatchingEye/ToggleUserLimit"),
                        data: JSON.stringify({ UserId: userId }),
                        contentType: 'application/json'
                    }).then(() => {
                        this.loadData(view);
                    }).catch(() => {
                        toast({ type: 'error', text: 'Error toggling user limit.' });
                    });
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
                    } else if (el.tagName === 'SELECT') {
                        el.value = value;
                    }
                    else {
                        el.value = value || '';
                    }
                });
                renderLimitedUsers(view, config);
                updateResetScheduleUI(view);
                loading.hide();
            });
        }

        saveData(view) {
            loading.show();
            view.querySelectorAll('[data-config-key]').forEach(el => {
                const key = el.getAttribute('data-config-key');
                if (el.type === 'checkbox') {
                    this.config[key] = el.checked;
                } else if (el.type === 'number' || (el.tagName === 'SELECT' && !isNaN(parseInt(el.value)))) {
                    this.config[key] = parseInt(el.value) || 0;
                }
                else {
                    this.config[key] = el.value;
                }
            });

            updatePluginConfiguration(this.config).then(result => {
                loading.hide();
                Dashboard.processPluginConfigurationUpdateResult(result);
                toast('Settings saved.');
            }).catch(() => {
                loading.hide();
                toast({ type: 'error', text: 'Error saving configuration.' });
            });
        }

        onResume(options) {
            super.onResume(options);
            this.loadData(this.view);
            this.watchStatusInterval = setInterval(() => {
                if (this.config.EnableWatchTimeLimiter) {
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
