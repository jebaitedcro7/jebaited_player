# Changelog

All notable changes to jebaited_player. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [Semantic Versioning](https://semver.org/) (MAJOR = big changes that may break compatibility, MINOR = new features, PATCH = bug fixes and small improvements).

Other languages: [Deutsch](CHANGELOG_DE.md) · [Hrvatski](CHANGELOG_HR.md). The same history is included in the player (click the version number on the home screen). This file is generated from `src/JebaitedPlayer.App/History/update-history.json`.

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
