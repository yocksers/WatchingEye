# Watching Eye for Emby

Watching Eye is a plugin for Emby Media Server that helps you monitor and manage viewership. It can notify users when their media is transcoding, and it allows administrators to set and manage watch time limits for specific users.

<img width="250" height="209" alt="WatchingEye" src="https://github.com/user-attachments/assets/e3f62249-8b7d-42eb-b5fb-f3f9eb426170" />

---

## Features

-   **Transcode Notifications**: Informs users with a customizable pop-up message when their media is transcoding. This can help educate users on why the quality might not be optimal.
-   **Direct Play Notifications**: Optionally, send a notification to users when their media is direct playing, assuring them of the best possible quality.
-   **Playback Start Notifications**: Display a custom message at the beginning of playback.
-   **Highly Customizable Notifications**:
    -   Tailor notification messages to your liking.
    -   Use `{reason}` in the transcode message to show the specific cause of transcoding.
    -   Set delays, frequency, and whether notifications require manual dismissal.
-   **Watch Time Limiter**:
    -   Enable and enforce watch time limits for designated users.
    -   Limits are configurable per user.
    -   Flexible reset schedules: daily at a specific time, weekly on a certain day, or after a custom duration in minutes.
    -   Users are notified when they've reached their limit, and playback is automatically stopped.
-   **Easy User Management**:
    -   Add users to the watch time limiter through a simple dialog.
    -   View the remaining time for each limited user, which updates periodically.
    -   Extend a user's watch time on the fly for a given session.
    -   Temporarily disable or re-enable limits for a user with a single click.
-   **Exclude Users and Clients**: Prevent notifications from appearing for specific users or on certain clients.

---

## Installation

1.  Download the `WatchingEye.dll` file from the latest [GitHub Release](https://github.com/YOUR_USERNAME/YOUR_REPOSITORY/releases).
2.  Copy the `.dll` file into the `plugins` directory of your Emby Server's installation folder.
3.  Restart Emby Server to load the plugin.

---

## Configuration

The plugin's settings can be accessed from the Emby Dashboard by navigating to **Plugins** (under the Advanced section) and clicking on **Watching Eye**.

The configuration page is split into two tabs:

### 1. Notifications Tab

Here you can configure all notification-related settings.

-   **Transcode Notification**:
    -   `Enable Transcode Warning`: Toggle transcode notifications on or off.
    -   `Message Text`: Customize the message shown for transcoding. Use `{reason}` to include the transcode reason.
    -   `Max Notifications per Session`: The maximum number of times a user will be notified during a single playback session.
    -   `Initial Delay (seconds)`: The time to wait after playback starts before sending the first notification.
    -   `Delay Between Messages (seconds)`: The time to wait between subsequent notifications.
    -   `Notify on Audio-Only Transcode`: Choose whether to notify for transcodes that only involve audio.
-   **Direct Play Notification**:
    -   `Notify on Direct Play`: Enable notifications for direct play sessions.
    -   `Direct Play Message Text`: The message to display for direct plays.
-   **Playback Start Notification**:
    -   `Enable Playback Start Notification`: Toggle this notification on or off.
    -   `Playback Start Message Text`: The message to show when playback begins.
-   **General Notification Settings**:
    -   `Enable Confirmation Button`: If enabled, all notifications from this plugin will require the user to manually close them (no timeout).
    -   `Excluded Users (comma-separated)`: A list of usernames that will not receive any notifications.
    -   `Excluded Clients (comma-separated)`: A list of client names (e.g., 'Emby Web', 'Android TV') that will not receive notifications.

### 2. Watch Time Limiter Tab

This section allows you to control user watch times.

-   `Enable Watch Time Limiter`: The master switch for this feature.
-   **Reset Schedule**: Choose how often the watch time is reset for all limited users.
    -   `After a set number of minutes`: Resets the timer for all users after the specified interval has passed since the last reset.
    -   `Daily at a specific time`: A daily reset at a chosen hour.
    -   `Weekly on a specific day`: A weekly reset on a selected day and time.
-   `Limit Reached Message Text`: The message displayed to users when their time is up and playback is stopped.
-   **Limited Users**:
    -   Click the **Add User** button to select a user from a dropdown and set their watch time limit in minutes.
    -   For each user in the list, you can:
        -   See their remaining time.
        -   **Extend** their time by a specified number of minutes.
        -   **Toggle** their time limit on or off without removing them from the list.
        -   **Remove** them from the limiter.
