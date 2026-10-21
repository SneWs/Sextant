# Privacy policy

Last updated: 14 October 2026

Sextant is a desktop Git client made by Marcus Grenängen. This policy covers Sextant on Windows, macOS, and Linux, including a build you make from source, a build from the GitHub releases, and the Microsoft Store package published as Sextant-Git.

The public address of this policy is `https://github.com/SneWs/Sextant/blob/master/docs/privacy.md`.

## What Sextant does not do

Sextant has no account, no sign-in, and no server of its own. It does not include advertising, analytics, or crash reporting. It does not sell or share personal information. It does not send the contents of your repositories, your settings, or your credentials to Marcus Grenängen.

## Information stored on your computer

Sextant writes its own settings on the computer where you run it:

| Platform | Folder |
| --- | --- |
| Windows | `%APPDATA%\Sextant` |
| macOS | `~/Library/Application Support/Sextant` |
| Linux | `$XDG_CONFIG_HOME/sextant`, or `~/.config/sextant` |

That folder holds `settings.json` (appearance, diff, Git, and merge choices), `workspace.json` (open tabs, window layout, and hidden branches), and any theme files you add. These files stay on that computer. Uninstalling Sextant does not delete the folder. You can delete it yourself.

Sextant reads the Git repositories you open so it can show status, history, and diffs. That reading happens on your machine. Repository files are not copied anywhere else by Sextant.

A command log in the window shows Git commands from the current session. Before a command is written there, a token embedded in an HTTPS URL is removed. The log is not saved as its own file and is not sent off the machine.

On Windows, an SSH key passphrase can be remembered for later prompts in the same repository. That checkbox keeps the passphrase in memory until Sextant closes. It is not written to disk.

## Network

Sextant does not send telemetry. When it starts, unless you have chosen not to be prompted for updates, and when you choose Check for update, it requests the latest release from `https://api.github.com/repos/SneWs/Sextant/releases/latest`. That request names Sextant as the user agent. It does not send repository contents, settings, or credentials. A copy installed from the Microsoft Store does not make that request. The Store delivers its updates.

Clone, fetch, pull, push, and Git LFS use the `git` already installed on the machine, and the credential helper, SSH client, and GPG tools that Git is configured to use. Those programs talk to the remotes in your repositories. What they send, including credentials, is decided by your Git configuration and by that remote. Sextant does not replace your credential store and does not keep a copy of a hosting token. When Fetch every in Settings is above 0, that same fetch runs for the active repository on that interval.

A repository opened from WSL on Windows uses that distribution's Git, so the same rule applies inside the distribution.

## Microsoft Store

If you install Sextant from the Microsoft Store, Microsoft handles the purchase, the download, and the installation under Microsoft's own privacy statement. That information is not sent to Marcus Grenängen by the app.

## Children

Sextant is a tool for working with Git repositories. It does not knowingly collect personal information from children.

## Changes

If this policy changes, the updated text replaces this page and the date at the top changes.

## Contact

Marcus Grenängen  
marcus [at] grenangen.se
