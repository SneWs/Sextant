# Working with Sextant

A repository is a tab. The active tab is the one Sextant loads. History, the file watcher, and diffs run there. An inactive tab stays unloaded until you select it. Close a tab with the × on the tab, or with Ctrl+W (Command+W on macOS). Ctrl+Tab moves to the next tab on every platform. Command+Tab is left to macOS.

The tab shows a green dot when the working tree is clean, an amber dot when it is dirty, and a red dot during a conflict.

The empty window, and the File menu, open, clone, or init a repository. A closed repository is opened again from the folder. There is no list of recent repositories beyond the tabs that restore on launch.

The command palette (Ctrl+P, or Command+P on macOS) lists the actions for the open repository, in alphabetical order. A click runs the row you clicked. Type to filter. Enter runs the selected row.

## The window

Under the tabs, the branch bar names the checked-out branch and, when Git knows, how many commits you are ahead of or behind the upstream. Pull and Push sit on the right. While a command runs, its label replaces that status, and Cancel appears when the command can be cancelled.

A conflict replaces that quiet state with a banner and Continue and Abort. A sparse checkout adds a banner: excluded paths stay out of the working tree. Sextant does not check those paths out to read them.

Below that, the window is three columns. Drag the splitters to resize them. The widths are remembered.

1. **Locations** lists branches, remotes, tags, stashes, submodules, and worktrees.
2. **History / Files** switches between the commit graph and the repository file tree.
3. **Files and the diff** describe the selected commit, or the working copy.

View, Toggle locations hides the first column. The command palette has the same action.

## Locations

The filter icon under the Locations heading opens a box. Text there filters branches, tags, stashes, and the other rows. Escape closes that box and clears the text.

Sections group themselves by name. `release/1.2` is a folder of tags or branches, not a flat list.

A double-click checks out a local branch, checks out a remote branch into a local one, or opens a worktree or a checked-out submodule. An uninitialized submodule is not opened, and Sextant does not run `git submodule update`.

A tag is different. One click selects the commit that tag points at, including an annotated tag. The same action is Show in graph on other rows that have it.

The row menu is the rest of the branch work: merge, rebase, delete, rename, set upstream, push or delete a tag, hide a branch, and the stash actions. Hide branch keeps the row in the list at reduced opacity and drops that branch from the graph. Hidden names are stored for that repository. Show all branches brings them back. A stash can be hidden the same way.

Add remote and Add worktree are in the Repository menu and the command palette. The new worktree's folder must not exist yet. Sextant does not open it for you, and it does not remove worktrees.

## History

The top row of the graph is the working copy. Commits follow, newest first. The subject is on the left. Branch names sit on the detail line under it. Tags are amber labels on the right of the row.

Sextant loads a page of history and more as you reach the end. The soft cap is about 50,000 commits in one tab, after which Load more is explicit. Closing the tab drops that list.

Select a commit to see its files and diff. Select two commits to see the diff between them. The right-hand menu on a commit can check out a branch that points there, copy the SHA, create a branch or tag, save a patch, reset soft, mixed, or hard, cherry-pick, revert, or start an interactive rebase.

Search is Ctrl+F (Command+F). The box spans the window under the branch bar. The magnifying glass runs the search. Escape clears it and hides the box.

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

Each file has a Git or LFS badge. Only locked files show lock information, including the owner's name. Opening this tab or pressing its Refresh button asks the LFS server for current locks, including locks on ordinary Git files; ordinary history browsing does not. F5 also refreshes locks while the Files tab is open. Loading LFS locks is shown in the centered branch bar. A failed lookup shows a notice above the tree, and lock actions stay disabled until the lookup succeeds.

Right-click any unlocked file for Lock file, or a locked file for Unlock file. A file does not need to be stored in LFS to use an LFS lock. Force Unlock file sits below Unlock file and asks for confirmation before removing a lock, including another user's lock. These actions use your installed Git LFS and its configured server and credentials. Server errors are shown in the window, rather than treating unknown locks as unlocked. Lock protection depends on the Git LFS lock checks; it does not prevent editing a local file. A file's History action returns to the History tab.

## Files and commits

The working-copy list is grouped into Conflicts, then Staged, then Unstaged. Empty groups are omitted. Each group title is a bar. The rows under it are the files, with a status letter and Stage, Unstage, Discard, or History.

Stage all, Unstage all, and Discard all are in the command palette. Discard all asks before it restores tracked files and removes untracked files.

The commit box is above the file list. Write the message and press Commit, or Ctrl+Enter (Command+Enter). The commit menu also offers Commit without hooks, and Amend. Amend replaces the tip message and keeps unstaged work unstaged. Nothing staged leaves Commit disabled, and the box says so.

On a past commit, that area shows the SHA, author, date, and the size of the change instead of a message you can edit.

## Diffs, blame, and previews

The Diff and Blame tabs sit above the patch. Blame annotates the selected file. Diff is the patch for the selected file, or for every file when all-files mode is on.

Changed lines can be staged or unstaged one hunk or one line at a time, from the working copy, when the patch is Git's own text. A formatted file-type diff does not offer that, because the lines on screen are not the lines in the repository. See [Configuration](configuration.md) for the formatter.

A large diff waits for Load anyway. A line longer than 256 characters is split so one line does not stall the window.

PNG, JPG, JPEG, GIF, BMP, WEBP, ICO, SVG, TIF, and TIFF open as a picture of each side. An FBX file shows a clay view of each model, with mesh, vertex, and triangle counts, material names, and animation takes. Hold Ctrl, or Command on macOS, and drag to rotate. The wheel zooms while that key is held. A double-click with the key returns the first angle. A plain wheel scrolls the diff.

## Fetch, pull, and push

Pull takes the upstream of the current branch. The menu on that button also offers Fetch, Fetch all, and Fetch all and clean up. Fetch all and clean up prunes remote-tracking branches that the remote has deleted.

Push sends the current branch. The menu offers Push ignoring local checks, which skips hooks, and Push with force-with-lease. Force-with-lease refuses to overwrite remote commits that you have not seen. It is not a plain force.

Network commands run one at a time per repository and report progress on the branch bar. Your credential helper answers any prompt. Sextant does not store a GitHub token of its own.

## Conflicts

Continue and Abort cover merge, rebase, cherry-pick, and revert.

For a text conflict, the diff area becomes the three-way editor: ours, the result, and theirs. Previous and Next walk the conflicts in the file. Take ours and Take theirs fill the result. Save and stage writes the result and stages the file when no conflict markers remain.

A file type with a restore command converts that text before the write. The editor tells you when it will. [Configuration](configuration.md) works through JSON and `jq`.

If an external merge command is set, or Git has `merge.tool`, the file's merge action opens that tool instead.

## Git LFS

Pointer files stay pointers in a normal diff. Sextant does not download them just to show status. An image or FBX pointer under 8 MB is the exception: both versions load for the preview. When the signed-in `gh` account cannot see the repository, another account already signed in to `gh` is tried for that download.

The file list marks a path with an LFS badge when Git's attributes say the filter is `lfs`. The tooltip says when the path is not tracked.

On a working-copy file the menu can Track with LFS, Stop tracking with LFS, or Download that one pointer. Track stages `.gitattributes` and the file. Stop tracking stages `.gitattributes` only.

Repository, Fetch LFS objects downloads objects for the current commit and leaves the working tree. Repository, Pull LFS files replaces pointer files and leaves files you have changed.

Checkout itself does not ask Git LFS to download. After the branch switch, `git lfs checkout` fills in objects that are already in the local store.

## Keyboard

The command key is Command on macOS and Ctrl on Windows and Linux, except where noted.

| Action | Keys |
| --- | --- |
| Open repository | Command/Ctrl+O |
| Command palette | Command/Ctrl+P |
| Search history | Command/Ctrl+F |
| Commit | Command/Ctrl+Enter |
| Create branch | Command/Ctrl+B |
| Stash | Command/Ctrl+Shift+S |
| Close tab | Command/Ctrl+W |
| Next tab | Ctrl+Tab |
| Refresh | F5 |
| Settings | Command+, on macOS |

Escape in the history search box hides it and clears the query. Escape in the locations filter closes that box.
