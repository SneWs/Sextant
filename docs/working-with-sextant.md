# Working with Sextant

A repository is a tab. The active tab is the one Sextant loads. History, the file watcher, and diffs run there. An inactive tab stays unloaded until you select it. Close a tab with the × on the tab, or with Ctrl+W (Command+W on macOS). Ctrl+Tab moves to the next tab on every platform. Command+Tab is left to macOS.

The tab shows a green dot when the working tree is clean, an amber dot when it is dirty, and a red dot during a conflict.

The empty window, and the File menu, open, clone, or init a repository. File → Exit, after a separator, closes Sextant the same way as closing the window. On Windows, they also open a repository from WSL; see [Getting started](getting-started.md#wsl-windows-only). A closed repository is opened again from the folder. There is no list of recent repositories beyond the tabs that restore on launch.

The command palette (Ctrl+P, or Command+P on macOS) lists the actions for the open repository, in alphabetical order. A click runs the row you clicked. Type to filter. Enter runs the selected row.

## The window

On GNOME, the window uses the desktop's own frame, including that theme's corners and caption buttons. KDE, Cinnamon, Windows, and macOS keep the frame from the desktop too.

Under the tabs, the branch bar names the checked-out branch and, when Git knows, how many commits you are ahead of or behind the upstream. The name sits in a rounded pill with a Stash button beside it on the left and a search button beside it on the right, and the group stays centered. The Stash split menu holds the stash variants, and the search button toggles the history search box, the same as the Search menu item. The down and up arrow buttons for Pull and Push sit on the right of the bar. While a command runs, its label replaces that status, and Cancel appears when the command can be cancelled.

A conflict replaces that quiet state with a banner and Continue and Abort. A sparse checkout adds a banner: excluded paths stay out of the working tree. Sextant does not check those paths out to read them.

Below that, the window is three columns. Drag the splitters to resize them. The widths are remembered.

1. **Locations** lists branches, remotes, tags, stashes, submodules, and worktrees.
2. **History** and **Repository files** share one dock as tabs. History is the selected tab.
3. **Files and the diff** describe the selected commit, or the working copy. Diff, Blame, and LFS locks share that diff dock. Diff is the selected tab. Reset layout puts LFS locks back with Diff and Blame. A layout saved earlier keeps its own arrangement until then.

View → Window → Locations shows the locations pane and brings it forward. It does not hide it. A pane that was closed comes back. A pane that is open but has no space left in the split, which is what closing its floating window used to leave behind, is given a share again. The command palette has the same action. The other entries in that menu do the same for History, Commit, Changes, Diff, Blame, Repository files, LFS locks, and Command log.

View → Save layout… writes the current arrangement to a file. View → Restore layout… reads one of those files and uses it, including the next time Sextant opens. Reset layout returns to the default arrangement. The command palette lists Save layout and Restore layout.

## Locations

Expand all and Collapse all sit to the left of the filter. Expand all opens every section and folder. Collapse all closes them, including a folder inside a section that was already closed. Sextant remembers that choice for the repository. Text in the filter matches branches, tags, stashes, and the other rows. A filter that already has text still shows the rows that match.

Sections group themselves by name. `release/1.2` is a folder of tags or branches, not a flat list.

A double-click checks out a local branch, checks out a remote branch into a local one, or opens a worktree or a checked-out submodule. An uninitialized submodule is not opened, and Sextant does not run `git submodule update`.

A click on a branch, a tag, or a stash shows the History pane when that pane is closed or another tab is in front, and scrolls the graph to the commit that row points at. The checked-out branch stays checked out. A tag or a hidden branch that is not in the loaded history is loaded up to that commit instead of reporting an error, and this pane names that revision until you close the filter. Show in graph does the same. An annotated tag uses the commit the tag points at.

A stash click also shows the files that stash holds, with the diff of those files. Untracked files are included when the stash was made with Stash (include untracked). A double-click shows that same stash and leaves the checked-out branch as it is. Pop, Apply, and Drop stay on the row menu.

The row menu is the rest of the branch work: merge, rebase, delete, rename, set upstream, push or delete a tag, hide a branch, and the stash actions. Hide branch keeps the row in the list at reduced opacity and drops that branch from the graph. Hidden names are stored for that repository. Show all branches brings them back. A stash can be hidden the same way.

A file history, an applied search, or hidden branches show a line under the filter. The line names the file, the search, or what is hidden. File history and a search include a close button on that line. The button returns the commit graph. A line that only names hidden branches has no close button. Escape still returns the graph when the search box is not focused.

Add remote and Add worktree are in the Repository menu and the command palette. The new worktree's folder must not exist yet. Sextant does not open it for you, and it does not remove worktrees.

## History

The top row of the graph is the working copy. Commits follow, newest first. The subject is on the left. Branch names sit on the detail line under it. Tags are amber labels on the right of the row.

Sextant loads a page of history and more as you reach the end. The soft cap is about 50,000 commits in one tab, after which Load more is explicit. Closing the tab drops that list. While the graph is loading, whether it is the first page, Load more, a search, or a jump to a tag outside the loaded history, a small pill with a spinner floats over the middle of the list and the branch bar names the work, so the list is never just sitting there.

Select a commit to see its files and diff. Select two commits to see the diff between them. The right-hand menu on a commit can check out a branch that points there, copy the SHA, create a branch or tag, save a patch, reset soft, mixed, or hard, cherry-pick, revert, or start an interactive rebase.

Search is Ctrl+F (Command+F). The box spans the window under the branch bar. The magnifying glass runs the search. Escape clears it and hides the box. The line that names the search, the file, or the hidden branches sits in the Locations pane.

| Query | Result |
| --- | --- |
| words | Subject or author contains the words |
| `author:name` | That author |
| `branch:name` | History from that branch, still drawn as a graph |
| a SHA of 7 to 64 hex digits | That commit |
| `*.fbx`, `MyFile.cs`, `dir/file.cs` | Commits that touch a matching path |
| `file:pattern` | The same path search, with the prefix written out |

A name with no slash matches that file in any directory. A version such as `1.2.3` stays a text search. A path, author, subject, or SHA search is a flat list, without the lane lines. A `branch:` search keeps the graph.

A file's History button is the same search limited to that path.

## Repository file tree

The Files tab beside History lists the current repository's tracked files and non-ignored untracked files, not just changed files. Directories expand with the arrow or a double-click. Only visible rows are drawn, and a refresh keeps expanded directories. Sparse-checkout paths are listed from Git's index without checking them out.

Each file has a Git or LFS badge. Only locked files show lock information, including the owner's name. Opening the Files tab asks the LFS server for current locks when this repository uses Git LFS: a local `lfs.url`, a local LFS clean, smudge, or process filter, or a `filter=lfs` attribute in `.gitattributes` or `info/attributes`. The request includes locks on ordinary Git files in that repository. A repository that does not use Git LFS skips the request, and so does ordinary history browsing. Returning to the repository, reactivating the window, and F5 refresh the file list and do not ask the lock server. There is no separate Refresh button in the file tree. Loading LFS locks is shown in the centered branch bar. A failed lookup uses the global error banner, like other Git commands, and lock actions stay hidden until the lookup succeeds.

When locks have been loaded, right-click an unlocked file for Lock file, or a locked file for Unlock file. A file does not need to be stored in LFS to use an LFS lock. Force Unlock file sits below Unlock file and asks for confirmation before removing a lock, including another user's lock. These actions use your installed Git LFS and its configured server and credentials. Server errors are shown in the window, rather than treating unknown locks as unlocked. Lock protection depends on the Git LFS lock checks; it does not prevent editing a local file. A file's History action returns to the History tab.

The same menu offers Open in editor and Open in Finder, File Explorer, or File Manager, using the same desktop actions as the diff's file header. Remove file asks before deleting the file from disk and removing it from Git tracking. A tracked file's deletion is staged; an untracked file is only deleted from disk. Uncommitted changes are lost, but committed history and LFS locks remain unchanged. Directories and submodules cannot be removed with this file action.

## Files and commits

Changes is its own docked tool. It starts under Commit. The working-copy list is grouped into Conflicts, then Staged, then Unstaged. Empty groups are omitted. Each group title is a bar. The rows under it are the files, with a status letter and Stage, Unstage, Discard, or History. Click a file to select it. Command-click (Ctrl-click on Windows and Linux) adds or removes a file, and Shift-click selects the range between the file you already selected and the one you click. A group title is not part of that selection. A past commit lists its files in that same tool. View → Window → Changes shows it and brings it forward. It does not hide it.

Stage all, Unstage all, and Discard all sit on the left of the Changes command bar, and again on the left of the diff command bar, while the working copy is open and that list has files. They hide when that list is empty. Stage selected, Unstage selected, and Stash selected sit to the right of those on the Changes bar. Stage selected stages the selected unstaged and untracked files and leaves conflicted files alone. Unstage selected returns the selected staged files to the unstaged list. Each is unavailable when the selection has nothing it can change. The diff bar keeps Stage all, Unstage all, and Discard all, and it has no selected-file buttons, so Expand all and Collapse all stay on the right. Stage all is Ctrl+Shift+A (Command+Shift+A on macOS) and Unstage all is Ctrl+Shift+R (Command+Shift+R). Stage all is unavailable when there is nothing to stage, and Unstage all when nothing is staged. Both are also in the Repository menu and the command palette. Discard all is in the command palette. Discard all asks before it restores tracked files and removes untracked files.

Stash saves changes to tracked files with Ctrl+S (Command+S on macOS). It is unavailable when there are no tracked changes to save, before the first commit, or while files have unresolved conflicts. Untracked files are left alone. Stash selected, on the Changes bar, asks for a message and then saves only the selected files. An untracked file in the selection is included. Files you did not select stay as they are. A file that is both staged and unstaged is saved as a whole. Stash selected is unavailable before the first commit or while any file has an unresolved conflict. Ctrl+Shift+S (Command+Shift+S) pops the latest stash after confirmation. Pop is unavailable when there is no stash or files have unresolved conflicts. Both actions are in the Repository menu and appear in the command palette when available. The Repository menu also nests three variants under Stash: Stash (include untracked) also takes untracked files, Stash (keep index) stashes everything but leaves the staged changes in place, and Stash (staged) stashes only the staged changes and keeps the rest. They need what they stash: the untracked variant is enabled by untracked files alone, and the staged variant by staged changes. Click a stash in Locations to see the files it holds and the diff of those files, including untracked files when that stash took them.

The commit box is the Commit tool. Write the message and press Commit, or Ctrl+Enter (Command+Enter). When nothing is staged, Commit stages every change and then commits. That is only for an ordinary commit. A rebase, merge, cherry-pick, or revert leaves the index alone, and Commit stays disabled until a file is staged. Files that are already staged are committed as they are, and the other changes stay unstaged. The commit menu also offers Commit without hooks, which stages in the same cases, and Amend. Amend replaces the tip message and keeps unstaged work unstaged, and it still needs a staged change. The box says when nothing is staged, and it says when Commit will stage every change. Below the message box, the current committer is shown as name and email; click it to change the name and email, either for this repository only or globally. Committing and amending need both a name and an email, so Sextant refuses either without one and points you at that row.

On a past commit, that area shows the SHA, author, date, and the size of the change instead of a message you can edit.

## Diffs, blame, and previews

Diff and Blame are separate docked tools. They start as two tabs in the pane under Changes. Blame annotates the selected file. The working-copy diff always includes both staged and unstaged files. Selecting a file in Changes opens and scrolls to its header without hiding the other files. A partially staged file has separate Staged and Unstaged sections. For past commits and commit ranges, Diff can show the selected file or every file.

Each working-copy file header has Stage file or Unstage file for that section.

Changed lines can be staged or unstaged one hunk or one line at a time, from the working copy, when the patch is Git's own text. A formatted file-type diff does not offer that, because the lines on screen are not the lines in the repository. See [Configuration](configuration.md) for the formatter.

A large diff waits for Load anyway. A line longer than 256 characters is split so one line does not stall the window.

PNG, JPG, JPEG, GIF, BMP, WEBP, ICO, SVG, TGA, TIF, and TIFF open as a picture of each side. `.targa` is the same preview as `.tga`. An FBX file shows a clay view of each model, with mesh, vertex, and triangle counts, material names, and animation takes. Hold Ctrl, or Command on macOS, and drag to rotate. The wheel zooms while that key is held. A double-click with the key returns the first angle. A plain wheel scrolls the diff.

## Fetch, pull, and push

Pull takes the upstream of the current branch. Settings → Git → When pulling chooses rebase or merge. Rebase is the default. Either choice passes `--autostash`, so tracked changes, staged or unstaged, are stashed and applied again when the pull finishes. Untracked files stay in the worktree. The menu on that button also offers Pull, Pull with rebase, Fetch, Fetch all, and Fetch all and clean up. Pull with rebase always rebases, including when the setting is merge. Fetch all and clean up prunes remote-tracking branches that the remote has deleted. Pull is Ctrl+Shift+U (Command+Shift+U on macOS) and Push is Ctrl+Shift+P (Command+Shift+P). Both are also in the Repository menu.

Push sends the current branch. The menu offers Push ignoring local checks, which skips hooks, and Push with force-with-lease. Force-with-lease refuses to overwrite remote commits that you have not seen. It is not a plain force.

Network commands run one at a time per repository and report progress on the branch bar. Your credential helper answers any prompt. Sextant does not store a GitHub token of its own.

Settings → Git → Fetch every runs Fetch for the active repository. 0 minutes leaves it off. Any other value is how many minutes pass between fetches. The first one waits that long after you set it. Switching tabs fetches the repository you selected, on the next interval.

## Conflicts

Continue and Abort cover merge, rebase, cherry-pick, and revert.

For a text conflict, the diff area becomes the three-way editor: ours, the result, and theirs. Previous and Next walk the conflicts in the file. Take ours and Take theirs fill the result. Save and stage writes the result and stages the file when no conflict markers remain.

A file type with a restore command converts that text before the write. The editor tells you when it will. [Configuration](configuration.md) works through JSON and `jq`.

If an external merge command is set, or Git has `merge.tool`, the file's merge action opens that tool instead.

## Git LFS

Pointer files stay pointers in a normal diff. Sextant does not download them just to show status. An image pointer under 20 MB, or an FBX pointer under 8 MB, is the exception: both versions load for the preview. When the signed-in `gh` account cannot see the repository, another account already signed in to `gh` is tried for that download.

The file list marks a path with an LFS badge when Git's attributes say the filter is `lfs`. The tooltip says when the path is not tracked.

On a working-copy file the menu can Track with LFS, Stop tracking with LFS, or Download that one pointer. Track stages `.gitattributes` and the file. Stop tracking stages `.gitattributes` only.

Repository, Fetch LFS objects downloads objects for the current commit and leaves the working tree. Repository, Pull LFS files replaces pointer files and leaves files you have changed.

Checkout itself does not ask Git LFS to download. After the branch switch, `git lfs checkout` fills in objects that are already in the local store.

View → Window → LFS locks lists the locks the server currently holds. The pane starts beside Diff and Blame, and Reset layout puts it back there. **My Locks** is the locks you hold. **Locks** is everyone else's. A group with nothing in it is left out. Each of your locks has a checkbox and Unlock. The My Locks bar has a checkbox that checks or clears those rows, and Unlock all while at least one is checked. Unlock all asks, then unlocks the checked rows. Unlock on one of your own locks does not ask. Another person's row shows Force unlock when that server accepted the lock verify call. Force unlock asks before removing the lock. A repository that does not use Git LFS says so and skips the request. When the server has no locks, the pane says No LFS locks. Opening the pane loads the list. Refreshing the repository does not. The loading and unlocking status appears in the branch bar. A failed lookup clears the list and shows the error banner.

## Keyboard

The command key is Command on macOS and Ctrl on Windows and Linux, except where noted.

| Action | Keys |
| --- | --- |
| Open repository | Command/Ctrl+O |
| Command palette | Command/Ctrl+P |
| Search history | Command/Ctrl+F |
| Commit | Command/Ctrl+Enter |
| Commit message | Shift+Esc |
| History and commit | Escape, Escape |
| Pull | Command/Ctrl+Shift+U |
| Push | Command/Ctrl+Shift+P |
| Create branch | Command/Ctrl+Shift+C |
| Stash | Command/Ctrl+S |
| Pop latest stash | Command/Ctrl+Shift+S |
| Stage all | Command/Ctrl+Shift+A |
| Unstage all | Command/Ctrl+Shift+R |
| Expand all diff sections | Command/Ctrl+Shift+E |
| Collapse all diff sections | Command/Ctrl+Shift+- |
| Close tab | Command/Ctrl+W |
| Next tab | Ctrl+Tab |
| Switch to tab by number | Command/Ctrl+0 to Command/Ctrl+9 |
| Refresh | F5 |
| Settings | Command+, on macOS |

Escape in the history search box hides it and clears the query. Shift+Esc shows the Commit pane, selects the working copy at the top of the history list, and focuses the commit message. Pressing Escape twice within half a second selects that same top row and scrolls the history list there. History's tab is selected when it shares a group. The Diff, Blame, and LFS locks group selects its first tab. An open Commit pane is selected and the commit message takes focus. Pane sizes stay as they are, and a closed pane stays closed. A locations filter, a history search, and hidden branches stay as they are.
In the command palette, Up and Down move through the matches, Enter runs the selected command, and Escape closes the palette.
