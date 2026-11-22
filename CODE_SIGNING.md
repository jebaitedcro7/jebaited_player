# Code signing policy

**jebaited_player** · maintained by jebaited_cro ([@jebaitedcro7](https://github.com/jebaitedcro7)) · last updated 2026-11-17

## Status

| | |
|---|---|
| GitHub releases (all versions) | **not code signed** |
| Microsoft Store version | signed by Microsoft after certification (works with Smart App Control) |
| SignPath Foundation (free signing for open source) | application **declined** (2026-11-19): the Foundation program asks for an established public presence (community adoption, external references, sustained activity). A new application is possible once the project has more visibility. |

Because the GitHub releases are not signed, Windows SmartScreen may show “Windows protected your PC” (More info → Run anyway), and Windows **Smart App Control** blocks the installer and the program itself (it checks every program and library when it runs). To use the GitHub version, Smart App Control has to be turned off (Windows Security → App & browser control → Smart App Control settings). If it is turned on again, Windows blocks jebaited_player again; on Windows 11 builds that are not fully updated it can only be turned on again by resetting Windows. **With Smart App Control on, use the Microsoft Store version**, which is signed by Microsoft.

The sections below describe the process that would apply if a signing certificate is obtained; at present no file is signed.

When signing starts, this section will say:

> Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

Only files built from this repository by the automated GitHub Actions workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) on GitHub-hosted runners are submitted for signing:

- `jebaited_player.exe` and the program's own libraries (`jebaited_player.dll`, `JebaitedPlayer.*.dll`),
- the installer `JebaitedPlayer-win-Setup.exe`.

Files from other open source projects that are included unchanged (for example `libmpv-2.dll`, SQLite, WebRTC VAD, Velopack's update program and the .NET runtime) are **not** signed with this project's certificate. The .NET runtime files are already signed by Microsoft. The source and license of every third-party component is listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md); `libmpv-2.dll` is checked against the SHA-256 in [third_party/libmpv/SOURCE.md](third_party/libmpv/SOURCE.md) in every build.

Nothing is signed from a developer's computer, and no binaries that were not built by the workflow are ever signed.

## Team and roles

| Role | Members |
|---|---|
| Committers and reviewers | jebaited_cro ([@jebaitedcro7](https://github.com/jebaitedcro7)) |
| Approvers (every signing request is approved manually) | jebaited_cro ([@jebaitedcro7](https://github.com/jebaitedcro7)) |

- Changes from people outside the team (pull requests) are reviewed by a team member before they are merged.
- Every signing request is approved manually in SignPath for each release.
- All team members use multi-factor authentication for GitHub and SignPath.

## Release process

1. A version is prepared on a branch, tested (unit tests and the diagnostic tests in `tools/`) and merged into `main`.
2. The version, its date and its notes are written in [`src/JebaitedPlayer.App/History/update-history.json`](src/JebaitedPlayer.App/History/update-history.json). The [CHANGELOG](CHANGELOG.md) and the GitHub release notes are generated from it; the same history is included in the player.
3. A tag `vMAJOR.MINOR.PATCH` ([Semantic Versioning](https://semver.org)) starts the release workflow, which builds, tests and packages the program and submits the files above for signing.
4. After the approver has approved the signing request, the signed installer and update packages are published as a [GitHub release](https://github.com/jebaitedcro7/jebaited_player/releases).

## Privacy

See the [privacy policy](PRIVACY.md). In short: no account, no ads, no telemetry; the program only connects to GitHub (update check, can be turned off) and to the subtitle sites the user sets up.

## Reporting a problem

If you think a signed file of this project is malicious or was not built from this repository, please open an [issue](https://github.com/jebaitedcro7/jebaited_player/issues) or contact SignPath Foundation.
