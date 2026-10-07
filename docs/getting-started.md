# Getting started

Sextant does not ship its own Git. Clone, commit, push, hooks, and Git LFS all go through the `git` on your machine. Install Git before you open a repository. Install Git LFS as well when a repository stores large files that way.

Published builds are on the [releases page](https://github.com/SneWs/Sextant/releases). Each tagged release has two downloads:

| Platform | File | What you run |
| --- | --- | --- |
| macOS Apple silicon | `Sextant-osx-arm64.tar` | `Sextant.app` |
| Windows x64 | `Sextant-win-x64.zip` | `Sextant.exe` |

There is no Intel macOS build and no Windows ARM build. Linux has no download on that page. Build it from source, described at the end of this page.

Installing a release from that page is free, and so is a build you make from source. The terms are in the [license](../LICENSE).

Sextant has no updater. A newer release is a new download. Settings and open tabs stay on disk when you replace the app.

## macOS

Install Git and Git LFS with Homebrew:

```bash
brew install git git-lfs
git lfs install
```

`git lfs install` writes the filters into your Git config once per user. A repository that already uses LFS still needs `git-lfs` on `PATH`, because checkout and the LFS commands call it.

Download `Sextant-osx-arm64.tar` from the latest release, then extract and move the app:

```bash
tar -xf Sextant-osx-arm64.tar
mv Sextant.app /Applications/
open /Applications/Sextant.app
```

A notarized build opens under Gatekeeper. If macOS reports the app as damaged, the download still has the quarantine flag and the ticket was not stapled. After you trust the file, clear that flag and open it again:

```bash
xattr -dr com.apple.quarantine /Applications/Sextant.app
open /Applications/Sextant.app
```

To update, quit Sextant, download the new tar, extract it, and replace `/Applications/Sextant.app`. Settings stay in `~/Library/Application Support/Sextant`.

## Windows

Install [Git for Windows](https://git-scm.com/download/win). In the installer, include Git LFS. When the install finishes, open a new terminal and check both tools:

```bat
git --version
git lfs version
```

If `git lfs` is not a command, install [Git LFS](https://git-lfs.com/) and run `git lfs install`.

Download `Sextant-win-x64.zip`, extract the whole zip, and run `Sextant.exe`. Keep the extracted files together. The executable expects the libraries that sit beside it.

To update, quit Sextant, download the new zip, and replace that folder. Settings stay in `%APPDATA%\Sextant`, which is `C:\Users\<you>\AppData\Roaming\Sextant`.

On Windows 11, Sextant also looks for WSL2. When a distribution is installed, File → Open from WSL, and the empty window, offer that distribution. The folder comes from the distribution. That tab runs the distribution's `git`, so it uses that install's config, hooks, and credential helpers. A folder opened from `\\wsl.localhost\` or `\\wsl$\` is treated the same way. Windows Git is still used for repositories that live on Windows. A WSL or Windows git command can ask for the SSH key passphrase in a window; Sextant does not save it.

## Linux

Install `git` and `git-lfs` from your distribution, then run `git lfs install` once.

The release page does not publish a Linux archive. From a clone of this repository, with the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed:

```bash
dotnet publish src/Sextant.csproj -c Release
```

The publish output includes `sextant.desktop` and the Sextant icon next to the executable. The first launch copies that desktop file into `~/.local/share/applications`, or `$XDG_DATA_HOME/applications` when that variable is set, if a copy is not already there. The copied entry points at the executable you launched.

To update a build you published yourself, quit Sextant and replace that folder. Settings stay in `$XDG_CONFIG_HOME/sextant`, or `~/.config/sextant` when `XDG_CONFIG_HOME` is unset.

## First launch

The empty window offers Open, Clone, and Init. On Windows 11 it also offers Open from WSL when a WSL2 distribution is installed.

- **Open** chooses a folder that is already a Git repository.
- **Open from WSL** chooses a WSL2 distribution, then a folder inside it. That tab uses the distribution's Git.
- **Clone** copies a remote repository into a new folder and opens it. The remote URL and credentials are handled by your Git credential helper, the same way `git clone` works in a terminal.
- **Init** creates a new repository in a folder you choose.

If Sextant cannot find `git`, a banner offers **Locate git**. You can also set the executable under Settings, Git. An empty path means the `git` on `PATH`.

File, Open repository is Ctrl+O on Windows and Linux, and Command+O on macOS.

## Build from source

You need the .NET 10 SDK and `git` on `PATH`.

```bash
dotnet run --project src/Sextant.csproj
```

```bash
dotnet test Sextant.slnx
```

The test command builds the shared tests and the tests for the operating system you are on. On macOS, `dotnet build` and `dotnet run` produce `src/bin/Debug/Sextant.app`.
