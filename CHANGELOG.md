# Changelog

All notable changes to jebaited_player. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); version numbers: the first number changes only for big milestones (for example new platforms), the second grows with every upgrade cycle with new features, and the last one counts fixes and is never reset (since 1.6.3; earlier versions followed [Semantic Versioning](https://semver.org/)).

Other languages: [Deutsch](CHANGELOG_DE.md) · [Hrvatski](CHANGELOG_HR.md). The same history is included in the player (click the version number on the home screen). This file is generated from `src/JebaitedPlayer.App/History/update-history.json`.

## [2.1.8] - 2026-11-21

Fix: the player starts again when the main window was maximized and the mini player was the last view; the mouse wheel changes the volume faster and more smoothly.

### Improved

- The mouse wheel over the volume bar changes the volume by 5 % per notch (was 2 %), in proportion to how far the wheel turns, so fine-grained wheels and touchpads are smooth. The mini player slider follows at once.

### Fixed

- The player no longer starts without any window when the main window was maximized and the last view was the mini player.

Release: [v2.1.8](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.8) · Changes since [v2.1.7...v2.1.8](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.7...v2.1.8)

## [2.1.7] - 2026-11-21

Mini player and home screen: five mini player looks, volume by mouse wheel, a pulsing LIVE badge for radio, music that keeps playing on the home screen and a fully customizable clock, date and weather.

### Added

- Five mini player looks (right-click → Look): classic Winamp, card, narrow strip, side panel and UFO screen. They all have the same functions and your choice is remembered.
- The mouse wheel over the volume bar changes the volume in steps of 2 % in the full and the mini player. The wheel over the video or other parts of the player no longer changes the volume by accident.
- A red, gently pulsing LIVE badge while a radio station plays: in every mini player look, in the full player and on the home screen.
- The song title scrolls in an endless loop (no gap and no jump) and by default shows only the artist and the song. Right-click the title to add album, year, genre, sound quality or the file name.
- Playback bar on the home screen: current song, source (local, radio, Spotify, YouTube Music, Discord), Play/Pause, Previous/Next and volume.
- Home screen look (Settings → Appearance): font, size and position of the time, the date and the weather, each on its own, with a preview and “Reset look to default”.
- More weather details (Open-Meteo, free, no key): feels-like temperature, minimum and maximum, chance and amount of rain, wind, humidity, UV index, sunrise and sunset, the next hours and the next days. Only what the weather service provides is shown.
- A separate Minimize button (and menu item): the player goes to the taskbar and the music keeps playing; it is different from Mini player and Close.
- Discord bot is in the source menu next to Radio, Spotify and YouTube Music.
- Next episode before the end (Settings → General): choose how many seconds before the end of an episode the card for the next one appears; the countdown then ends exactly at the end. 0 keeps the old behavior (only when the episode has ended).

### Improved

- The mini player and all other windows (the Equalizer panel, Radio, Discord…) always stay completely inside the visible screen area: the taskbar, display scaling and several monitors are taken into account, and a window moved outside is pulled back.
- The narrow strip now has a volume slider.

### Fixed

- Pressing Home no longer stops music or radio. Only video still closes, as before.
- Only one window opens at start: the mini player if it was the last view (or when a music file is opened), otherwise the full player. Before, the big player appeared first and the mini player a moment later.

### Known issues

- The location is chosen by city name only; automatic location is not included (nothing is sent before you pick a city).
- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v2.1.7](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.7) · Changes since [v2.0.6...v2.1.7](https://github.com/jebaitedcro7/jebaited_player/compare/v2.0.6...v2.1.7)

## [2.0.6] - 2026-11-21

2.0: your own Discord bot plays the player's music and radio in a voice channel; every keyboard shortcut can be changed; choose the accent color.

### Added

- Discord bot (Ctrl+Shift+D, the headphones button in the mini player, or right-click → Discord bot…): your own bot plays the player's music and radio in a voice channel on your server, so friends hear what you listen to. Create a free bot in the Discord Developer Portal (the steps are in the window), paste its token and invite it; “Join my channel” finds the channel you are in. The token is stored encrypted on this computer only. Only local music and radio are sent: films, YouTube and Spotify are never sent (the bot sends silence). Voice is end-to-end encrypted (Discord DAVE).
- Your own keyboard shortcuts (Settings → Shortcuts): every action can have two keys. Click a key and press the new combination; if another action already uses it, the player tells you, and pressing it again moves it. “Default” restores one action or the whole tab. F1 and the menus always show your actual keys, and your keys are included in settings export and backups.
- Accent color (Settings → Appearance → Accent color): eight ready-made colors or your own (hue, saturation and lightness, or #RRGGBB) with a live preview. Buttons, sliders, selected items and the mini player display and level meter follow it; in each theme the color is adjusted when needed so that text stays readable.

### Improved

- The longer seek (Ctrl+←/→) and the fine and coarse subtitle shifts (Ctrl+G/H, Shift+G/H) are now actions of their own, so their keys can be changed too.
- The full license texts of the Discord bot components (NetCord, Concentus, libdave, OpenSSL, mlspp, nlohmann/json) are installed with the program in the licenses folder, together with THIRD_PARTY_NOTICES.

### Fixed

- The short privacy text in Settings was out of date (it named only GitHub, subtitles and radio); it now lists every service the player can connect to.
- The F1 list of shortcuts was missing Ctrl+M, Ctrl+R, Ctrl+Shift+S and Ctrl+Shift+Y; it is now built from the same list the player uses, so it is always complete.

### Known issues

- The Discord bot needs Windows 11 (or Windows 10 build 20348 or newer) to send sound.
- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v2.0.6](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.0.6) · Changes since [v1.8.4...v2.0.6](https://github.com/jebaitedcro7/jebaited_player/compare/v1.8.4...v2.0.6)

## [1.8.4] - 2026-11-21

Cycle 6: YouTube Music, time, date and weather on the home screen, and error reports.

### Added

- YouTube Music (Ctrl+Shift+Y, or the source label in the mini player): paste a link from YouTube or YouTube Music (a song or a playlist) and it plays in the official YouTube player in its own window, without an account or a key. Searching needs your own free YouTube API key (the steps are in the window; “API key…” changes or removes it). While YouTube is the source, the mini player shows YOUTUBE and its buttons control it. The YouTube player must stay visible (YouTube rules), so there is no background playback.
- Home screen information: the time and the date at the top left, and if you want the weather for a city you choose (Settings → Appearance; from Open-Meteo, free and without an account, at most every 30 minutes, °C or °F). Without internet the last known weather is shown with the time it was updated.
- Error reports: errors and crashes are saved on your computer as short redacted reports (no folders, video names, names, keys or IP address). Settings → About → “Send via GitHub…” opens a prepared issue that you can read before you send it.

### Improved

- One sound at a time: when YouTube starts, the local file pauses (a radio station stops) and Spotify is no longer the source; opening a file or a station pauses YouTube.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.8.4](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.8.4) · Changes since [v1.7.4...v1.8.4](https://github.com/jebaitedcro7/jebaited_player/compare/v1.7.4...v1.8.4)

## [1.7.4] - 2026-11-21

Cycle 5: Spotify remote control, “Up next” on the home screen and your own home screen background.

### Added

- Spotify (Ctrl+Shift+S, or the source label in the mini player): sign in with your own free Spotify developer app (a guide is in the window), see the song with its cover, play, pause, skip, seek, set the volume, play your playlists and saved songs, search and choose the device. The music plays in your Spotify app; while Spotify is the source, the mini player buttons control it. Controlling playback needs Spotify Premium.
- “Up next” on the home screen: the next episode of a series you are watching appears by itself (also across seasons), you can add any video (right-click the video or a “Continue watching” item), change the order with ▲ ▼ and remove items with ×. An item disappears once it is watched.
- Settings → Appearance → Home screen background: the default space background or your own picture (Fill, Fit or Center), with a preview. The picture is not copied; if it is moved or deleted, the space background comes back.

### Improved

- A big background picture is loaded in the background and only at screen size, so it does not slow down the start.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.7.4](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.7.4) · Changes since [v1.6.3...v1.7.4](https://github.com/jebaitedcro7/jebaited_player/compare/v1.6.3...v1.7.4)

## [1.6.3] - 2026-11-20

Cycle 4: live radio after Stop, protected settings, a question for unsaved changes and a separate output for music.

### Added

- Settings → Audio → “Output for music and radio”: music, radio and the mini player can play on a different output than movies (for example a virtual cable for Discord). By default it is the same output as for movies.
- Closing the settings with unsaved changes now asks: Save, Discard changes or Cancel (the window stays open).

### Improved

- Settings survive updates and damage: a damaged settings file is kept aside (settings.corrupt-….json) and your settings come back from the latest automatic backup, with a message. A settings file that cannot be read is never overwritten, and settings written by a newer version are kept.
- Live radio uses much less memory: only a small buffer is kept instead of up to 200 MB.

### Fixed

- Radio: after Stop (or Pause) and Play, a station always played the same song, from the moment it was first tuned in. Stop now really stops the station and Play connects again, live; the stopped station stays shown in the mini player. Space, the media key and Play after a dropped connection also reconnect live, and live radio can no longer be rewound by accident.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.6.3](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.6.3) · Changes since [v1.5.2...v1.6.3](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.2...v1.6.3)

## [1.5.2] - 2026-11-19

Radio fix.

### Fixed

- Radio played nothing when a station was the first thing you played after starting the player (for example when the player opened as the mini player). Stations now always play.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.5.2](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.2) · Changes since [v1.5.1...v1.5.2](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.1...v1.5.2)

## [1.5.1] - 2026-11-19

Radio: a play button and clear feedback.

### Improved

- Radio: every station has a ▶ button (clicking the name works too). The station that is playing is shown in green, and the window says “Connecting…”, “Playing…” or that a station is not working right now, so you can pick another one.
- The Radio window stays open when the mini player opens.
- Version number follows the project rule: the middle number grows with each big upgrade, the last number with each follow-up and is never reset (1.5.0 → 1.5.1).

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.5.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.1) · Changes since [v1.5.0...v1.5.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.0...v1.5.1)

## [1.5.0] - 2026-11-19

Radio, and Save, Discard and Default in the settings.

### Added

- Radio (Ctrl+R, the right-click menu or the source label in the mini player): search tens of thousands of stations by name, genre and country in the free Radio Browser directory, keep favorites with the star, or play any stream URL. Stations play in the mini player with the music equalizer, marked RADIO and LIVE, with the current song when the station sends it.
- Settings: Save keeps your changes, Discard changes goes back to the last save, and a label shows whether there are unsaved changes.
- Default…: set the current tab or all settings back to their default values. Language, keys and passwords, volume and window positions stay.

### Improved

- Radio stations are not added to “Continue watching”.
- The privacy statement describes the radio directory.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.5.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.0) · Changes since [v1.4.1...v1.5.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.4.1...v1.5.0)

## [1.4.1] - 2026-11-19

Separate equalizers for films and music, mini player looks and automatic switching.

### Added

- Two equalizers: one for films and series, one for music and radio. The one for what is playing is used automatically. In Settings → Audio you choose which one you are editing, and the mini player shows the one for music.
- Mini player looks: six display fonts and four VU meter styles (bars, LED segments, thin lines with peak, off). Right-click the mini player or go to Settings → Appearance.
- Opening music starts the mini player, and opening a film or series starts the full player. This can be turned off.

### Improved

- The mini player always opens where you left it. If you close the player while it is the mini player, it starts as the mini player next time.
- Right-click menu on the mini player: font, VU meter, size, strip, equalizer, playlist, automatic switching and settings.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.4.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.4.1) · Changes since [v1.4.0...v1.4.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.4.0...v1.4.1)

## [1.4.0] - 2026-11-19

Mini music player, automatic backups and settings export and import.

### Added

- Mini player (right-click menu or Ctrl+M): a small Winamp-style window in the jebaited_cro look that stays on top. It has a green display with the time and a real level meter (L/R), the scrolling artist and title with the bitrate, a seek bar, previous, play, pause, stop, next, open and volume, plus an equalizer and a playlist that open below. It shrinks to a thin strip, and you can make it bigger or smaller by dragging the corner, with Ctrl + mouse wheel or with Ctrl + / −. The music keeps playing when you switch, and Ctrl+M takes you back.
- Automatic backups: your settings and watch history are copied once a day and before every update. The last 7 copies are kept on your computer (Settings → General → Backup and transfer).
- Export and import settings: save your settings to a file and load them again, also on another computer. Passwords, keys for subtitle sites and the window position are not exported. Your current settings are backed up before an import, and a wrong file is rejected without changing anything.

### Improved

- Opening one song plays the whole album (all songs in that folder), and the next song starts right away, without a countdown.
- Music no longer fills “Continue watching” on the home screen.
- The equalizer works the same in the settings and in the mini player.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.4.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.4.0) · Changes since [v1.3.0...v1.4.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.3.0...v1.4.0)

## [1.3.0] - 2026-11-19

Equalizer, a reliable “Watched” status and local diagnostics.

### Added

- Equalizer under Settings → Audio: 10 bands and a preamp, presets (Flat, Rock, Pop, Jazz, Classical, Electronic, Bass boost, Treble boost, Vocals and dialogue) and your own preset (Custom). Changes are heard right away without interrupting playback, and it works together with volume levelling. While watching, it is also in the Audio menu.
- Diagnostics / Logs (Settings → About): the player keeps a local log of errors, crashes and playback problems, so they can be found and fixed. You can view and filter it and export it as a ZIP (file names can be anonymized). Nothing is sent anywhere, and old logs are deleted automatically.

### Improved

- “Watched” now means really watched: at least 80% of the video was actually played. Skipping or jumping to the end no longer marks a video as watched, and watching the same part twice is counted once.
- Watched videos show a green check, “Watched” and a full green bar on the home screen and in the episode list.

### Fixed

- Reopening a watched episode or film and closing it early no longer removes the green bar and the “Watched” status. Only “Mark as watched / unwatched” in the episode list changes it.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.3.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.3.0) · Changes since [v1.2.0...v1.3.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.2.0...v1.3.0)

## [1.2.0] - 2026-11-17

Player controls, settings in tabs and the update history.

### Added

- Home button at the top left of the player: back to the home screen without closing the app. The position is saved, so you can continue watching later. It shows and hides together with the other controls, also in full screen.
- Timeline: click anywhere to jump there right away. Dragging still works, and a bubble shows the time under the mouse.
- Settings are now split into tabs: General, Language, Subtitles, Audio, Video, Appearance, Updates and About.
- New settings: what happens when you reopen a video (ask, resume or start from the beginning), seek step for the arrow keys, open videos in full screen, keep the player on top, preferred audio language, audio output device, audio delay, volume levelling, hardware decoding, picture quality, default aspect ratio, HDR passthrough, how long the controls stay visible and the encoding for old subtitles.
- Update history: click the version number on the home screen. It is included in the app, so it works without the internet; when you are online, newer versions from GitHub are shown too.

### Improved

- A click on the timeline seeks precisely at once (no second jump). The timeline is easier to hit and gets thicker under the mouse.
- “Back to the home screen” is also in the right-click menu.
- Code signing policy, privacy statement and automatic builds on GitHub Actions, as preparation for free code signing by SignPath Foundation.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.
- The progress window of the installer comes from Velopack and is only in English.

Release: [v1.2.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.2.0) · Changes since [v1.1.0...v1.2.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.1.0...v1.2.0)

## [1.1.0] - 2026-11-17

Languages, a new home screen, themes and manuals.

### Added

- Interface in English, German and Croatian; under “More languages” also Bosnian, Serbian (Latin) and Slovenian. All menus, settings, messages, errors, sync, online subtitles, the episode list and the history are translated.
- Language choice on the first start (right after the installation); without a choice, English stays. The language can be changed in Settings at once, without a restart, and is remembered.
- New home screen: the Alien logo and the name jebaited_cro, a discreet space background, a gear for Settings at the bottom left and a dark/light theme switch.
- Manuals in three languages (Quick Start, Basic Guide, Advanced Guide) with screenshots: README_EN, README_DE and README_HR.

### Improved

- A new installation gets default subtitle languages that match the chosen language. Existing settings, watch history and keys are kept.

### Known issues

- The progress window of the installer comes from Velopack and is only in English.
- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.

Release: [v1.1.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.1.0) · Changes since [v1.0.1...v1.1.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.0.1...v1.1.0)

## [1.0.1] - 2026-11-17

Check of the automatic updates. The player works the same as in 1.0.0.

### Improved

- Verified automatic updates from GitHub: version 1.0.0 updates itself to 1.0.1, and settings and watch history are kept.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.

Release: [v1.0.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.0.1) · Changes since [v1.0.0...v1.0.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.0.0...v1.0.1)

## [1.0.0] - 2026-11-17

First public release.

### Added

- Plays almost every video and audio format (mpv): H.264, HEVC, AV1, VP9, 4K, HDR, 5.1 sound and hardware decoding.
- Subtitles from the internet: OpenSubtitles, SubDL and Titlovi.com; the subtitles for your release are chosen automatically and saved next to the video.
- Sync to the speech: offset, speed (25 / 23.976 fps) and a piecewise correction for a different cut; subtitles for another episode are rejected.
- Manual sync: offset, gradual delay, alignment by line (without hurry) and Z/X.
- Series: episode list, continue watching and the next episode.
- Guide for subtitles from the internet: opens the sign-up, checks and saves your details (encrypted).
- Windows: a single instance (a new file goes to the open player), several files at once, “Open with”, file type registration and setting the default player through Windows Settings.
- Installation without administrator rights, shortcuts and automatic updates (installed when the player closes); settings and history are kept.

### Known issues

- The app is not digitally signed yet, so Windows SmartScreen or Smart App Control may warn before the installation.

Release: [v1.0.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.0.0)
