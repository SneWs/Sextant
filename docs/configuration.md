# Configuration

Open Settings from the command palette, or with Command+, on macOS. On Windows and Linux it is the Settings menu. The window has five pages: Appearance, Diff, File types, Git, and Merging. OK writes them. Cancel discards the edits in that window.

Sextant stores them in `settings.json`. It does not write these choices into the repository's Git config.

| Platform | Folder |
| --- | --- |
| Windows | `%APPDATA%\Sextant` |
| macOS | `~/Library/Application Support/Sextant` |
| Linux | `$XDG_CONFIG_HOME/sextant`, or `~/.config/sextant` |

`workspace.json` in that same folder remembers open tabs, column widths, and which branches are hidden. Open tabs come back on the next launch. The `reopenTabs` field in `settings.json` controls that, and it defaults to `true`. The Settings window does not show a checkbox for it.

## Appearance

**Color theme** picks the palette. Each theme has a light palette and a dark palette.

| Theme | Light | Dark |
| --- | --- | --- |
| Catppuccin | Latte | Mocha |
| Gruvbox | light | dark |
| Monokai | light | dark |
| Tokyo Night | day | night |
| Dracula | light | dark |
| GitHub | light default | dark default |
| Black | light (derived) | dark |

- **Follow system** uses the operating system's light or dark choice of that theme.
- **Light** forces the light palette. For Tokyo Night, that is the day palette.
- **Dark** forces the dark palette.
- GitHub is the GitHub Theme light default and dark default. The accent is that theme's blue, not the green primary button.
- Black is the Black theme. It publishes a dark palette only. The light palette uses the same mint and pink, darkened so they stay readable on white.

The saved values are `palette` (`catppuccin` by default) and `theme` (`system`, `light`, or `dark`).

**Font** is the typeface for diff text, the merge editor, and the command log. Default tries Cascadia Mono, then Consolas, then DejaVu Sans Mono. A chosen face is tried first, and that stack is the fallback. The saved field is `diffFont`. Empty means the default. **Size** is that face's point size, from 8 to 48. It defaults to 12. The saved field is `diffFontSize`. The sample to the right updates as you change the face or the size. OK applies it to open diffs.

A `.xaml` or `.axaml` file in the themes folder is listed with those themes. The folder is `themes` inside the configuration folder above. The path under Color theme opens that folder in the file manager. The file name, without the extension, is the saved id. `Solarized.axaml` is `solarized`. A file named `Catppuccin.axaml` replaces the built-in Catppuccin palette. Sextant loads the file with Avalonia's runtime XAML loader. The root must be a `ResourceDictionary` with a `Light` palette and a `Dark` palette. These brushes are optional: `OnAccentBrush`, `CommitAddedPillBrush`, `CommitAddedTextBrush`, `CommitRemovedPillBrush`, and `CommitRemovedTextBrush`.

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ColorPaletteResources x:Key="Light"
                           Accent="#076678" ErrorText="#9D0006" RegionColor="#FBF1C7"
                           BaseHigh="#FF3C3836" BaseMediumHigh="#CC3C3836" BaseMedium="#993C3836"
                           BaseMediumLow="#663C3836" BaseLow="#333C3836" ChromeAltLow="#FF3C3836"
                           ChromeLow="#F9F5D7" ChromeMedium="#EBDBB2" ChromeMediumLow="#FBF1C7"
                           ChromeHigh="#D5C4A1" ChromeGray="#928374"
                           ChromeDisabledHigh="#D5C4A1" ChromeDisabledLow="#7C6F64"
                           ChromeWhite="#FFFFFFFF"
                           AltHigh="#FFFFFFFF" AltLow="#33FFFFFF" AltMedium="#99FFFFFF"
                           AltMediumHigh="#CCFFFFFF" AltMediumLow="#66FFFFFF"
                           ChromeBlackHigh="#FF000000" ChromeBlackLow="#33000000"
                           ChromeBlackMediumLow="#66000000" ChromeBlackMedium="#CC000000"
                           ListLow="#193C3836" ListMedium="#333C3836"/>
    <ColorPaletteResources x:Key="Dark"
                           Accent="#83A598" ErrorText="#FB4934" RegionColor="#282828"
                           BaseHigh="#FFEBDBB2" BaseMediumHigh="#CCEBDBB2" BaseMedium="#99EBDBB2"
                           BaseMediumLow="#66EBDBB2" BaseLow="#33EBDBB2" ChromeAltLow="#FFEBDBB2"
                           ChromeLow="#1D2021" ChromeMedium="#3C3836" ChromeMediumLow="#282828"
                           ChromeHigh="#504945" ChromeGray="#928374"
                           ChromeDisabledHigh="#665C54" ChromeDisabledLow="#7C6F64"
                           ChromeWhite="#FFFFFFFF"
                           AltHigh="#FF000000" AltLow="#33000000" AltMedium="#99000000"
                           AltMediumHigh="#CC000000" AltMediumLow="#66000000"
                           ChromeBlackHigh="#FF000000" ChromeBlackLow="#33000000"
                           ChromeBlackMediumLow="#66000000" ChromeBlackMedium="#CC000000"
                           ListLow="#19EBDBB2" ListMedium="#33EBDBB2"/>
    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="OnAccentBrush" Color="#FBF1C7"/>
            <SolidColorBrush x:Key="CommitAddedPillBrush" Color="#E6EDC5"/>
            <SolidColorBrush x:Key="CommitAddedTextBrush" Color="#79740E"/>
            <SolidColorBrush x:Key="CommitRemovedPillBrush" Color="#F5D5C8"/>
            <SolidColorBrush x:Key="CommitRemovedTextBrush" Color="#9D0006"/>
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="OnAccentBrush" Color="#1D2021"/>
            <SolidColorBrush x:Key="CommitAddedPillBrush" Color="#3A4428"/>
            <SolidColorBrush x:Key="CommitAddedTextBrush" Color="#B8BB26"/>
            <SolidColorBrush x:Key="CommitRemovedPillBrush" Color="#4A2C28"/>
            <SolidColorBrush x:Key="CommitRemovedTextBrush" Color="#FB4934"/>
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

## Diff

- **Inline** shows removed and added lines in one column.
- **Side by side** shows the old text on the left and the new text on the right.
- **Ignore whitespace** passes Git's `-w` when Sextant asks for a diff. The files on disk are unchanged. Whitespace-only edits drop out of the view.

The command palette can flip the last two without opening Settings: Toggle side-by-side diff, and Toggle ignore whitespace.

**Toggle all files** switches past-commit diffs, commit-range diffs, and blame between every file in the selection and the one file selected in the file list. All files is the default. The working-copy diff always shows both staged and unstaged files, regardless of file selection or this setting. File headers fold in the all-files and working-copy diffs. Expand all is Ctrl+Shift+E, or Command+Shift+E on macOS. Collapse all is Ctrl+Shift+C, or Command+Shift+C.

## Git

**Executable** is the `git` binary Sextant runs. Leave it empty to use `git` on `PATH`. Browse picks a file. Use system git clears the path.

Sextant still uses that Git's config, hooks, attributes, and credential helper. An SSH key, a GPG signer, and a credential manager keep working because Sextant does not replace them.

## Merging

**External merge command** is optional.

Leave it empty to resolve a text conflict in Sextant. The editor shows ours, the result, and theirs. When Git already has `merge.tool` set, that tool is used instead of the built-in editor.

A command typed here is passed to `git mergetool` for that run only. Sextant does not write it into Git config. These tokens are replaced by Git:

| Token | Meaning |
| --- | --- |
| `$LOCAL` | The current side |
| `$REMOTE` | The other side |
| `$BASE` | The common ancestor |
| `$MERGED` | The file to save |

Example, with Meld installed and on `PATH`:

```text
meld "$LOCAL" "$MERGED" "$REMOTE"
```

Use built-in editor clears the command.

## File types

A file type runs a program on the contents of a file before Sextant diffs it, and can run another program when you save a merge of that file. The point is to compare a readable form of a file that Git stores in a noisy form. Minified JSON is the usual case: Git sees one long line, so a one-field edit looks like the whole line changed. A formatter turns each side into pretty JSON, and the diff shows the lines that actually differ.

The page lists three columns.

| Column | Meaning |
| --- | --- |
| Extension | The file suffix, such as `.json`. `json` is stored as `.json`. Matching ignores case. |
| Transform | Command that turns the stored bytes into the text you want to read. |
| Restore | Command that turns the merge editor's text back into the bytes written to disk. |

Add file type appends a row. A row needs an extension and at least one command. The same extension cannot be listed twice. An extension is one suffix: `.json` is valid, and `.json.bak` is not, because of the second dot. A path, a space, or a wildcard is not an extension.

### How a command runs

The command is one line. It is not a shell. Pipes, `&&`, redirection, and shell variables are not available. Sextant splits the line into a program and arguments. Quotes group a path that contains spaces. Inside double quotes, `\\` and `\"` are escapes.

The program's standard input is the file's bytes. Standard output is the text Sextant keeps. The working directory is the repository root. `PATH` includes the directory of the Git executable you configured, then the normal `PATH`.

`$FILE` is optional. When any argument contains that token, Sextant writes the bytes to a temporary file named `input` plus the extension, and replaces `$FILE` with that path. Use it for a tool that reads a path instead of standard input. A tool that never reads standard input can exit first. That closed pipe is not a failure. If standard output is empty and `$FILE` was used, Sextant reads the temporary file back, so a tool that rewrites the file in place still works.

Each side of a diff is converted on its own. Sextant then runs `git diff --no-index` on those two results and shows that patch. A new file has an empty old side, so only the new side is converted. A deleted file converts only the old side.

Limits:

- A file larger than 8 MB is left as Git showed it.
- An all-files diff formats at most 64 matching files. The rest stay on Git's diff until you open that file alone.
- A formatted diff cannot be hunk-staged or line-staged. The patch is the formatted text, not the bytes Git has stored. Stage the whole file instead.
- If the program is missing, exits non-zero, or writes more than 8 MB, Sextant shows Git's original diff and a note that says why.

The program's arguments and any error are recorded in the command log (Repository, Command log).

### JSON with jq

Install `jq` so the name `jq` runs from a terminal.

```bash
# macOS
brew install jq

# check any platform
jq --version
```

On Windows, install `jq` and confirm a new terminal can run `jq`. If the program lives in a folder with spaces, quote it in the command, for example `"C:\Program Files\jq\jq.exe" .`.

In Settings, File types, add one row:

| Extension | Transform | Restore |
| --- | --- | --- |
| `.json` | `jq .` | `jq -c .` |

`jq .` reads JSON from standard input and writes it pretty-printed, with one value on each line. `jq -c .` writes the same JSON as a single compact line.

Take a file stored as one line:

```json
{"name":"sextant","version":1,"flags":{"fast":true}}
```

You change `version` to `2` and save it, still on one line. Git's own diff replaces that entire line. With the transform above, Sextant pretty-prints the old bytes and the new bytes, then diffs those. The view is the usual line diff, and the only changed line is the version:

```text
-  "version": 1,
+  "version": 2,
```

Two files that differ only by spacing become identical after `jq .`, so the formatted diff is empty. That is what you want when the stored layout is noise. A real field change still shows.

`jq` exits with an error on JSON it cannot parse. Sextant then shows the original diff and reports the formatter failure. Fix the file, or remove the rule for that extension, when a repository intentionally stores non-JSON in a `.json` file.

Restore runs when you save a conflict from the merge editor, not when you edit a normal diff. The editor shows the pretty text from the transform. Save and stage runs `jq -c .` on that text and writes the compact stdout back to the working tree, then stages the file. The repository keeps the one-line layout it had before the conflict.

If you want the saved file to stay pretty, set Restore to `jq .` as well, or leave Restore empty. An empty restore writes the editor text as it stands. The merge notice says which of those will happen.

Resolve every conflict marker before saving when a transform is set. If markers remain, Sextant leaves the original file unchanged and tells you to resolve them first, so `jq` is not asked to parse a half-merged buffer.

The same row in `settings.json` looks like this:

```json
{
  "sideBySide": false,
  "ignoreWhitespace": false,
  "theme": "system",
  "palette": "catppuccin",
  "reopenTabs": true,
  "diffFormats": [
    {
      "extension": ".json",
      "transform": "jq .",
      "restore": "jq -c ."
    }
  ]
}
```

A second extension is another object in `diffFormats`. Each command is still one program, not a pipeline. When you need two steps, put them in a script and point the command at that script. Pass `$FILE` if the script wants a path.
