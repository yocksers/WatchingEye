# Watching Eye for Emby

Watching Eye is a comprehensive plugin for Emby Media Server that helps you monitor and manage viewership. It can notify users about media playback status, block unwanted transcodes, and allow administrators to set highly detailed watch time limits for specific users, complete with remote management via a standalone web page and a public API.

<img width-="250" height="209" alt="WatchingEye" src="https://github.com/user-attachments/assets/e3f62249-8b7d-42eb-b5fb-f3f9eb426170" />

---

## Features

-   **Advanced Transcode Notifications**:
    -   Informs users with a customizable pop-up message when their media is transcoding.
    -   Uses `{reason}` in the message to show a user-friendly cause of transcoding (e.g., "the video format is not supported").
    -   Provides separate, customizable messages for transcodes caused by client limitations vs. bandwidth limitations.
-   **Transcode Blocking**:
    -   **By Format**: Stop playback automatically if a transcode is initiated from a blocked container format (e.g., MKV, AVI).
    -   **By Resolution**: Block transcodes for media that exceeds a maximum resolution you define (e.g., block 4K transcodes).
-   **Playback Notifications**:
    -   **Direct Play**: Optionally, notify users when their media is direct playing, assuring them of the best possible quality.
    -   **Playback Start**: Display a custom message at the beginning of any playback session.
-   **Granular Watch Time Limiter**:
    -   Enforce multi-layered time limits (Daily, Weekly, Monthly, Yearly) for designated users.
    -   **Per-User Schedules**: Each user can have a unique reset schedule, including the time of day for daily resets, the day of the week for weekly resets, and the day of the month/year for monthly/yearly resets.
    -   **Time Windows**: Restrict playback for specific users to certain hours of the day (e.g., only between 8:00 AM and 10:00 PM).
    -   **Threshold Alerts**: Send server-side notifications (e.g., to Pushover) when a user reaches a certain percentage (e.g., 80%, 95%) of their limit.
-   **Remote Management & API**:
    -   **Standalone Web Page**: An optional, password-protected external web page to monitor and control user watch times from any device on your network without needing to log into the Emby dashboard.
    -   **Public API**: A secure API to allow external scripts, dashboards, or applications to monitor and control user watch time.
-   **Easy User Management**:
    -   View detailed watch time status for each limited user.
    -   **Extend Time**: Add "time credits" to any user on the fly.
    -   **Time-Out**: Temporarily block a user from playback for a specified number of minutes.
    -   **Reset Time**: Instantly reset the watch time for a single user or all users.
-   **Event Logging**:
    -   An in-plugin log viewer shows the last 200 events, such as when users hit their time limit or trigger a transcode notification.
-   **Exclusions**:
    -   Exclude specific users, clients, or entire libraries from all notification and blocking rules.

---

## Installation

1.  Download the `WatchingEye.dll` file from the latest release on the project's GitHub page.
2.  Copy the `.dll` file into the `plugins` directory of your Emby Server's data folder.
3.  Restart Emby Server to load the plugin.

---

## Configuration

The plugin's settings can be accessed from the Emby Dashboard by navigating to **Plugins** (under the Advanced section) and clicking on **Watching Eye**.

The configuration page is split into four tabs:

### 1. Notifications

Configure all pop-up messages sent to clients.

-   **Transcode Notification & Blocking**:
    -   Enable and customize messages for transcodes caused by client issues or bandwidth limits.
    -   Enable blocking by container format (e.g., `mkv,ts,avi`).
    -   Enable blocking by resolution and set the maximum allowed vertical resolution (e.g., `1080`).
-   **Direct Play & Playback Start Notifications**:
    -   Enable and customize messages for Direct Play and when a session first begins.
-   **General Notification Settings**:
    -   **Confirmation Buttons**: Choose individually for each notification type whether it should require manual dismissal or disappear automatically.
    -   **Exclusions**: Select users, clients, and libraries that should be ignored by all notification and blocking rules.

### 2. Watch Time Limiter

This is the main control center for managing user watch time.

-   **Main Settings**:
    -   `Enable Watch Time Limiter`: The master switch for this feature.
    -   `Enable Server Notifications`: Sends alerts via Emby's notification system (e.g., Pushover, Slack) when a user hits a warning threshold or their final limit.
    -   Customize the messages shown to users when they are blocked due to a time limit, a time window restriction, or a manual time-out.
-   **Limited Users**:
    -   Click **Add User** to configure a new user's limits.
    -   The user editor allows you to configure everything for a user in a clean, tabbed interface:
        -   **Limits**: Set daily, weekly, monthly, and yearly limits.
        -   **Reset Schedule**: Define the exact time and day when each limit period resets.
        -   **Time Window**: Enable and configure allowed playback hours.
        -   **Notifications**: Enable and define percentage thresholds for server-side warnings.
-   **External Standalone Web Page**:
    -   Enable the web server, set a port, and define a password. **A server restart is required to enable/disable or change these settings.**
    -   **Important**: On Windows, you must run a `netsh http add urlacl` command as an Administrator for this to work. The required command is displayed on the configuration page for your convenience. For Docker, you must map the chosen port.
-   **External API Control**:
    -   Enable the API and generate a unique API key for use in external applications.

### 3. Logging

View the last 200 events captured by the plugin, such as "Transcode" or "Limit Reached". You can clear the log from this page.

### 4. API & Help

This tab provides a complete guide and documentation for the **Public API**, including authentication instructions and details for each available endpoint. This is your go-to reference for building custom integrations.
