# UefiAccess keyboard map

This page lists every key the UefiAccess application (`UefiAccess.App.exe`) responds to, every
access key, the F6 regions, and the rules that move keyboard focus. It is also for whoever maintains
the window: the keyboard map, the `Strings` resource class
(`src/UefiAccess.Presentation/Resources/Strings.cs`) and the window's code-behind must agree, and a
difference between this page and the build is a bug to report.

Three rules hold everywhere in the window:

1. **Nothing moves focus on a timer.** Focus moves only in direct response to a key you pressed or a
   button you activated, or when an action you started (Refresh, Open baseline folder, the checks or
   the writing of a change, Read Dell settings) finishes. There are no auto-dismissing messages, no
   polling and no delayed focus changes. The application has one timed step of its own, and it is not
   a focus move: after you move in the variable list, the detail pane is rebuilt once the list has
   been quiet for 300 ms (section 9). That rebuild says nothing and moves no focus.
2. **Every state change is announced once, in the Status box.** The Status box is the only polite
   live region in the window; the log panel is marked off (it never interrupts) and nothing is
   assertive.
3. **The window can change exactly two firmware variables**, BootOrder (the order of its entries) and
   BootNext, and only after the Confirm change dialog; every such change writes a snapshot folder
   first. It can suspend BitLocker for one restart, only when you tick that box in the same dialog.
   Nothing else on the machine is changed. Changes are enabled only on the configured enabled unit
   (recognized by its Windows machine ID); on every other machine `Confirm change` refuses and nothing
   is written. The Dell settings region only reads; it changes no BIOS setting.

Screen-reader shortcuts (NVDA and Narrator keys) are not part of this map; they belong to the screen
reader.

## 1. Window layout and regions

One window titled `UefiAccess` until the first load; from then on the title names the source that was
asked for last: `UefiAccess – Live firmware` after `Refresh`, or
`UefiAccess – Baseline (offline): <folder> (<timestamp>)` while a baseline folder is shown. A refused
or failed load names its source too. Seven regions, top to bottom, each a stock `GroupBox` with an
accessible name:

| Order | Region (spoken name) | Contains |
|---|---|---|
| 1 | Toolbar | the five buttons (`Refresh`, `Open baseline folder`, `Clear filter`, `Copy value`, `Export visible list`) |
| 2 | Filter | the filter box, the Group by combo box and the Sort by combo box |
| 3 | List | the variable list (`Variables`: rows in groups) |
| 4 | Detail | the detail pane for the selected variable (`Variable details`), and its `Change` group after the `Writability` line (section 7) |
| 5 | Log | the log panel (`Log`) and its `Copy log` button |
| 6 | Dell settings | the `Read Dell settings` button, the state line `Dell settings source` (a read-only box that says when and where the values shown were loaded, or why they could not be) and the list `Dell BIOS settings`, one row per Dell BIOS setting (section 8) |
| 7 | Status | the machine identity strip (eight read-only text lines, not tab stops, reached by object navigation: the source description, then `Manufacturer`, `Model`, `BIOS version`, `Service tag`, `CPU`, `Operating system` and `Expected model confirmed`) and the read-only `Status` box |

The regions are exposed to UI Automation as named groups, not as landmarks: WPF on .NET 8 has no
`AutomationProperties.LandmarkType`, so a screen reader's landmark keys find nothing in this window.
Move between regions with `F6` and `Shift+F6` (section 4) or with object navigation.

The list and the detail pane sit side by side under a splitter named `Resize list and details`; the
log panel sits under a second splitter named `Resize log panel`. Each splitter says its position
after every arrow press along its direction, `Left` or `Right` on `Resize list and details` and `Up`
or `Down` on `Resize log panel`; the other two arrows do nothing and say nothing (section 8).

The window asks for 1200 × 800 device-independent pixels and can be made no smaller than 900 × 600;
each of those is reduced to the screen's work area when the window opens, and the window opens centered
in it, so no part starts under the taskbar at a high display scale. The Status region keeps its full
height at the bottom; the regions above it share the rest inside an unnamed pane that scrolls only
when they do not fit at the current text size. The `Dell settings` region is as high as its content,
and its list scrolls inside, so its rows never push the Status region down.

The control with keyboard focus is marked from the moment the window opens: a button has a two-pixel
border in the text color; a text box a three-pixel border; the focused list row a two-pixel outline
in the Highlight color; a focused splitter turns the Highlight color with a HighlightText frame.
Every color is a Windows system color, so a contrast theme supplies all of them.

## 2. Tab order

`Tab` and `Shift+Tab` cycle through the whole window in this order and wrap at both ends
(`KeyboardNavigation.TabNavigation="Cycle"` on the window). Every focusable control has an explicit
`TabIndex` (1 to 27), and there is no focus trap in any region. Stops 14 to 20 are the `Change` group
of the detail pane, and only the controls of it that belong to the variable shown are tab stops; the
others are hidden (section 7). Stops 24 to 26 are the `Dell settings` region.

| Tab stop | Control (automation name) | Region |
|---|---|---|
| 1 | `Refresh` button | Toolbar |
| 2 | `Open baseline folder` button | Toolbar |
| 3 | `Clear filter` button | Toolbar |
| 4 | `Copy value` button | Toolbar |
| 5 | `Export visible list` button | Toolbar |
| 6 | `Filter variables` text box | Filter |
| 7 | `Group by` combo box (Namespace, Kind, None) | Filter |
| 8 | `Sort by` combo box (Name, Namespace, Size, Kind) | Filter |
| 9 | `Variables` list: one tab stop; `Tab` enters it at a group header or row and leaves it from anywhere inside; arrow keys move inside it. Each group is one expander named `<label>, <n> items` holding one table of rows named `<label> rows`; the group's generated container is not an automation element, so a screen reader meets the header first | List |
| 10 | `Resize list and details` splitter; its value names the list's and the detail pane's shares of the width, and Status says them after each `Left` or `Right` press (section 8) | between List and Detail |
| 11 | detail heading, named by the selected variable (`No variable selected` when nothing is selected): a level 1 heading that takes focus so the pane always has an entry point | Detail |
| 12 | detail fields: one read-only text box per field (Identity, Attributes, Size, Decoded when present, Raw with the hex dump box), each named by its label; they share this index | Detail |
| 13 | `Writability` line (a read-only text box) | Detail |
| 14 | `Boot order entries` list (shown for `BootOrder` read from the live machine): one tab stop, one row per entry of the proposed order | Detail, `Change` group |
| 15 | `Move up` button (with `Boot order entries`) | Detail, `Change` group |
| 16 | `Move down` button (with `Boot order entries`) | Detail, `Change` group |
| 17 | `Review change` button (with `Boot order entries`) | Detail, `Change` group |
| 18 | `Boot once from this entry` button (for an active boot entry of category Boot read from the live machine) | Detail, `Change` group |
| 19 | `Clear one-time boot` button (for `BootNext` read from the live machine) | Detail, `Change` group |
| 20 | `Restore previous value` button (for `BootOrder` or `BootNext` once this application has changed it on this machine, so a snapshot exists) | Detail, `Change` group |
| 21 | `Resize log panel` splitter; its value names the log panel's share of the height and that of the list and detail pane, and Status says them after each `Up` or `Down` press (section 8) | between Detail and Log |
| 22 | `Log` list box | Log |
| 23 | `Copy log` button | Log |
| 24 | `Read Dell settings` button (`Alt+S`; never disabled, so always a tab stop) | Dell settings |
| 25 | `Dell settings source`, the state line: a read-only box of one line that wraps, its text selected whole on arrival (section 8) | Dell settings |
| 26 | `Dell BIOS settings` list: one tab stop, one row per Dell BIOS setting (ten), each named by its sentence and read with its position (`1 of 10`); `Tab` enters it at the selected row | Dell settings |
| 27 | `Status` box | Status |

Inside the detail pane every field is a read-only `TextBox`, so `Tab` walks the heading first, then
field by field, then the `Writability` line, then the controls of the `Change` group the variable has
(for `BootOrder`: the list, `Move up`, `Move down`, `Review change` and, after a change, `Restore
previous value`; for an active boot entry: `Boot once from this entry`; for `BootNext`: `Clear
one-time boot` and `Restore previous value`; for every other variable, and for every variable of a
baseline folder, none). Both splitters are tab stops (needed for keyboard resizing); the identity
strip, the list column headers and the unnamed pane that holds the upper regions are not.

## 3. Access keys (Alt + letter)

Every access key is unique. They work from anywhere in the window and do not move focus themselves:
the button is activated in place, and the focus rule of that action (section 9) decides what happens
next. `Alt` alone shows the underlines. A letter alone never activates a button (the application sets
WPF's `IsAltKeyRequiredInAccessKeyDefaultScope`): in the list a plain letter is type-ahead
(section 6), in the filter box it is text.

| Key | Button (label as shown) | What it does |
|---|---|---|
| `Alt+R` | `Refresh` | reads the firmware variables from the live machine |
| `Alt+O` | `Open baseline folder` | opens the folder dialog and loads a baseline folder offline; also works while `Refresh` is still reading, and the baseline then replaces that read |
| `Alt+C` | `Clear filter` | empties the filter box so every variable is listed again |
| `Alt+Y` | `Copy value` | copies the value of the detail field that has focus, or the selected variable's decoded summary; while focus is in the `Dell settings` region, copies the selected Dell row's sentence or the state line's text |
| `Alt+X` | `Export visible list` | opens the save dialog and writes every row that passes the filter (plain text or JSON); the firmware is never touched |
| `Alt+L` | `Copy log` | copies every line of the log panel, each with its full date and time |
| `Alt+U` | `Move up` | moves the selected entry of `Boot order entries` one position up in the proposed order; nothing is written |
| `Alt+D` | `Move down` | the same, one position down |
| `Alt+V` | `Review change` | checks the proposed order, then opens the `Confirm change` dialog |
| `Alt+B` | `Boot once from this entry` | checks a one-time boot from the boot entry shown, then opens the dialog |
| `Alt+M` | `Clear one-time boot` | checks the removal of the one-time boot choice (`BootNext`), then opens the dialog |
| `Alt+P` | `Restore previous value` | checks putting back the value in the newest snapshot of the variable shown, then opens the dialog |
| `Alt+S` | `Read Dell settings` | loads the Dell BIOS settings of this computer and changes nothing; never disabled, so the key always answers |

The six keys of the `Change` group work while their button is shown (section 7); the key of a hidden
button does nothing. In the `Confirm change` dialog the one access key is `Alt+F` (`Confirm change`);
`Escape` is `Cancel`. A screen reader learns each button's shortcut as its accelerator key (`Alt+R`
and so on) and reports no access key, and each button's help text names the key as well.

`Alt+F4` closes the window, except while a change is written: then the window stays open and Status
says why. `Alt+Space` opens the window menu.

## 4. Region navigation: F6 and Shift+F6

| Key | Effect |
|---|---|
| `F6` | next region: Toolbar → Filter → List → Detail → Log → Dell settings → Status → Toolbar (wraps) |
| `Shift+F6` | previous region: Toolbar → Status → Dell settings → Log → Detail → List → Filter → Toolbar (wraps) |

On arrival, Status announces `<Region> region` and focus lands on the region's entry control. The
Status region is the exception: arriving there announces nothing, so the Status box keeps its last
message. The `Dell settings` region adds a few words on arrival only where they change what its rows
mean: `Dell settings region, from baseline folder <name>` while a baseline folder is shown, and
`Dell settings region, could not be loaded` when the last read gave no answer.

| Region | Focus lands on |
|---|---|
| Toolbar | the first enabled toolbar button: `Refresh`; while `Refresh` is reading, `Open baseline folder`; while a baseline folder loads, `Clear filter`; while a change is written, `Copy value` |
| Filter | the `Filter variables` box (text not selected; use `Ctrl+F` to select it) |
| List | the selected row, or the first row when nothing is selected (or the list itself when it is empty) |
| Detail | the first field of the detail pane, its value selected whole (section 7), or the caret at its start for a value of 512 characters or more (or the heading when nothing is selected) |
| Log | the log list box, on its selected line, or the last line |
| Dell settings | the selected row of `Dell BIOS settings` (the rows are there from the start, also before any read), or `Read Dell settings` if that row cannot take focus |
| Status | the `Status` box |

`F6` and `Shift+F6` skip a region none of whose controls can take focus and go on in the same
direction; only the region they land in is announced. The two splitters belong to no region: from
each, `F6` and `Shift+F6` continue in their direction to the regions on either side. The `Change`
group is part of the Detail region, not a region of its own; reach it with `Tab` from the
`Writability` line. `F6` from inside an open combo box drop-down first closes the drop-down; press it
again to move.

## 5. Global keys

| Key | Where | Effect |
|---|---|---|
| `Ctrl+F` | anywhere | focuses the `Filter variables` box and selects its text. While a change is written the box is disabled and Status says `The filter is unavailable while a change is written; wait for its result.` |
| `Escape` | in the filter box | clears the filter and moves focus to the list at the previously selected row |
| `Escape` | while the checks of a change run | cancels the checks; nothing is written, and Status says `Cancelled; nothing changed.` |
| `Escape` | in the detail pane (any field, the hex dump, the `Writability` line, the `Change` group) | moves focus back to the list at the same row; a proposed boot order is kept |
| `Ctrl+Z` | in the `Boot order entries` list | puts the firmware's order back in the list (section 7) |
| `Escape` | in an open combo box drop-down | closes it without changing the choice |
| `Escape` | while dragging a splitter with the mouse | cancels the drag |
| `Escape` | elsewhere, when no checks run | nothing |
| `Tab`, `Shift+Tab` | anywhere | next or previous tab stop (section 2) |
| `F6`, `Shift+F6` | anywhere | next or previous region (section 4) |
| `Alt+<letter>` | anywhere | access keys (section 3) |
| `Enter`, `Space` | on a focused button | activates the button |

## 6. Variable list keys

The list is a single tab stop; these keys work while a row or a group header has focus. The list is
built as one group after another: each group is a stock expander whose header is a focusable toggle
button, holding a table (a `ListView` with six columns) of the group's rows. Every row is announced
as one sentence (`AutomationProperties.Name`), for example
`BootOrder, EFI global variable, NV BS RT, 4 bytes, BootOrder: Boot0000 (Windows Boot Manager), Boot0001 (UEFI: SanDisk)`,
capped at 200 characters (the full values are in the detail pane). Every group header is announced
with its count, for example `EFI global variable, 28 items`, and the header is announced once
(entering a group from its header reads the short grid name `<label> rows` and then the row). The
list's help text,
`One row per firmware variable. Arrows move, Home and End jump to the first and last row, Page Up, Page Down and typing a name work inside the group, Enter opens the details, Ctrl+C copies the key.`,
is heard once on arrival in the region.

On the enabled unit, two rows say more while this application can change them (read from the live
machine, the variable well formed): `changeable: order of entries` follows the name in `BootOrder`'s
sentence, and `changeable: one-time boot` in `BootNext`'s; the same two rows carry the UI Automation
item status `Changeable: order of entries` and `Changeable: one-time boot`. On a machine that is not
the enabled unit, the same two rows carry the item status `Not changeable on this machine` and the
words `not changeable on this machine` follow the name. Every other row carries no item status.

| Key | Effect |
|---|---|
| `Down`, `Up` | next or previous row, selecting it; the detail pane follows and focus stays in the list. `Down` on the last row of a group moves to the next group's header, `Up` on the first row to this group's header; on a header, `Down` enters the group (or moves to the next header when it is collapsed) and `Up` moves to the previous group's last row |
| `Home`, `End` | the first row of the first group, or the last row of the last group, across all groups |
| `Page Down`, `Page Up` | one page of rows down or up inside the current group |
| letters and digits (type-ahead) | selects the next row inside the current group whose name starts with the letters typed, for example `Bo` for `Boot0000`. Rows of other groups are not searched; `Group by: None` puts every row in one group. A letter is never an access key here |
| `Enter`, `Space` | on a row: moves focus into the detail pane at its first field; on a group header: collapses or expands the group (focus and selection stay) |
| `Ctrl+C` | copies the selected row's key (`<guid>:<name>`); if the clipboard cannot be written, Status says `Copy failed: <reason>` |
| `Left` | on a row: moves to its group header; on an expanded group header: collapses the group |
| `Right` | on a collapsed group header: expands it; on an expanded group header: moves to its first row; on a row: nothing |
| `Ctrl+Left`, `Ctrl+Right` | scroll the columns one page left or right when they are wider than the list; the row keeps focus |

Selection is single. Changing the selection updates the detail pane and nothing else: focus stays in
the list and nothing is announced. With `Group by` set to `None` there is one group, `All variables,
<N> items`, and the rows are one flat sequence inside it.

## 7. Detail pane keys

The detail pane shows, for the selected variable, a heading with the variable name and the sections
Identity, Attributes, Size, Decoded (only when a decoder accepted the variable) and Raw. The heading
is a level 1 heading and each section title a level 2 heading. Every value is a read-only `TextBox`
named by its label alone, with the help text `Escape returns to the list at the same row.`; the box
speaks its value, and the help text does not say the box is read-only because the screen reader says
so from the box's own state. There are two kinds of box:

- **A single-line value** wraps inside its box. The whole value is selected when keyboard focus
  arrives on its box from another control, so a screen reader reads all of it. A value of 512
  characters or more is not selected; the caret goes to its start, the help text is
  `Long value, <n> characters: Down arrow reads it line by line. Escape returns to the list at the same row.`,
  and `Down` reads line by line.
- **A multi-line value** keeps its own lines, unwrapped, in a fixed-width font with a horizontal
  scroll bar, read line by line from the first; nothing is selected on arrival. That is the hex dump
  (`Raw data, hex dump`) and any decoded value with a line break.

The last line, named `Writability`, is a single-line box selected whole on arrival. It says, for the
variable shown, what this application can change and what it cannot (with no variable selected it
reads `No variable selected`):

- `BootOrder`, read on the enabled unit and well formed:
  `Changeable: the order of its entries can be changed from this application (Move up, Move down, Review change). Nothing else about it can be changed.`
- `BootNext`, likewise:
  `Changeable: this one-time boot choice can be removed (Clear one-time boot) or put back from a snapshot (Restore previous value).`
- an active boot entry of category Boot:
  `Cannot be changed. This entry can be chosen for one boot (Boot once from this entry).`
- a variable with an authenticated-write attribute:
  `Cannot be changed: authenticated write (<flags>); only a signed update can change it. This application never writes it.`
  (for `SecureBoot`, `SetupMode`, `PK`, `KEK`, `db` and `dbx` the line continues with the way to
  change Secure Boot in the firmware's setup, which this application only reports).
- every other variable:
  `Cannot be changed: this variable is not on the whitelist of this application. Only BootOrder (reorder) and BootNext can be changed.`
- `BootOrder` or `BootNext` whose firmware value is malformed: `Cannot be changed until fixed: <reason>`.
- every variable of a baseline folder that the application could otherwise change:
  `Cannot be changed: shown from a baseline folder; nothing can be changed offline.`
- on a machine that is not the enabled unit, for `BootOrder`, `BootNext` and an active boot entry:
  `Cannot be changed on this machine: changes are enabled only on the enabled unit, one specific OptiPlex 7020 with BIOS A18. The Change controls and the dialog work up to Confirm change, which refuses; nothing is written.`

| Key | Effect |
|---|---|
| `Tab`, `Shift+Tab` | next or previous field |
| `Escape` | back to the list at the same row |
| `Left`, `Right`, `Home`, `End` | move the caret inside the field (read-only); while the whole value is still selected, `Left` goes to the start and `Right` to the end |
| `Up`, `Down` | in a single-line field: the previous or next line of the wrapped value; in the hex dump: previous or next line |
| `Shift+arrows`, `Ctrl+A` | select text inside the field |
| `Ctrl+C` | copies the selection inside the field; right after arrival that is the whole value (nothing for a value of 512 characters or more: use `Alt+Y`) |
| `Alt+Y` (`Copy value`) | copies the whole value of the field that has focus; with focus elsewhere, the selected variable's decoded summary or the whole hex dump |
| `Ctrl+Home`, `Ctrl+End` | start or end of the value |
| `Page Up`, `Page Down` | scroll the detail pane when it is longer than the window |

### The `Change` group

After the `Writability` line, a group named `Change`, with the help text
`Controls that change this variable through the write pipeline. Nothing is written until you confirm in a dialog.`
It is shown only for a variable read from the live machine that this application can change, and shows
only that variable's controls; for every other variable, and every variable of a baseline folder, it
is hidden and has no tab stop. On any machine the controls, the checks and the dialog work; on a
machine that is not the enabled unit, `Confirm change` refuses and nothing is written.

- For `BootOrder`: the list `Boot order entries` (a copy of the firmware's order that you rearrange
  before anything is written), then `Move up`, `Move down` and `Review change`.
- For an active boot entry of category Boot: `Boot once from this entry`.
- For `BootNext`: `Clear one-time boot`.
- For `BootOrder` and `BootNext` once this application has changed them on this machine: `Restore
  previous value`.

| Key | Where | Effect |
|---|---|---|
| `Up`, `Down`, `Home`, `End` | the list | select a row; nothing moves |
| `Ctrl+Up`, `Ctrl+Down` | the list | move focus without selecting; nothing moves |
| `Alt+U`, `Alt+D` | anywhere while the list is shown (or `Enter`/`Space` on `Move up`/`Move down`) | move the selected entry one position; the row keeps its focus |
| `Ctrl+Z` | the list | puts the firmware's order back; the selection stays on the same entry |
| `Enter`, `Space` | a row of the list | nothing: a change needs a button |
| `Alt+V`, `Alt+B`, `Alt+M`, `Alt+P` | anywhere while the button is shown | the checks, then the dialog. While the checks run, `Open baseline folder` and `Export visible list` are disabled too; `Refresh` stays available, and a load that finishes then cancels the checks |
| `Escape` | the list or a `Change` button | back to the variable list at the same row; while the checks run it cancels them instead |

### The `Confirm change` dialog

After the checks, a dialog titled `Confirm change` opens in front of the window, with focus on the box
`What will change`, its sentence selected whole. It shows the change and writes nothing itself; the
change is written only after `Confirm change`. `Tab` goes through, in order: `What will change`,
`Before`, `After`, `BitLocker state`, `Risk`, `Recovery key`, `Warnings`, `Snapshot folder`, the
check box `Suspend BitLocker for one restart` (shown only when BitLocker protects or is encrypting the
drive, or its state could not be read, and the change is a reorder or a one-time boot; ticked at
first), `Confirm change` (`Alt+F`) and `Cancel` (`Escape`).

Every box is a read-only text box named by its label alone, with the help text
`Escape cancels; nothing is written.`. Each sentence you hear on `Tab` is a single-line box selected
whole; the three multi-line boxes (`Before`, `After`, `Warnings`) are read line by line from the
first, which is why each says on its first line what follows. There is no default button: a change is
confirmed only by `Space` or `Enter` on `Confirm change`, or by `Alt+F`. `Escape` anywhere, `Cancel`
and the close button all cancel, and nothing is written. Ticking or clearing the check box changes the
`What will change` sentence and the button's name and says nothing; the dialog has no live region and
runs nothing on a timer.

## 8. Splitter, combo box, log and status keys

Splitters (`Resize list and details`; `Resize log panel`): reach them with `Tab` or `Shift+Tab`, then:

| Key | Effect |
|---|---|
| `Left`, `Right` | on `Resize list and details`: give the list less or more width, one percent per press; Status says the new shares |
| `Up`, `Down` | on `Resize log panel`: give the log panel more or less height, one percent per press; Status says the new shares |
| `Escape` | cancels a mouse drag in progress; a keyboard step is applied at once and the opposite arrow undoes it |

A splitter's position is the whole percent that one of the two panes beside it has of the space they
share; the other pane has the rest. `Left` lowers the list's share and `Right` raises it; `Up` raises
the log panel's share and `Down` lowers it; an arrow across the splitter's direction does nothing. The
panes never shrink below their minimums, and a press at a limit moves nothing. After every arrow press
along the direction, and once at the end of a mouse drag that changed the shares, Status says the
position as a sentence, for example `List 62 percent of the width, details 38 percent.` or
`Log panel 31 percent of the height, list and details 69 percent.`; a press at a limit says the same
after `Limit reached: `. A splitter's position is the one Status sentence not also written to the log
panel. The splitters are `AnnouncingGridSplitter` (`src/UefiAccess.App/Platform/`): the stock
`GridSplitter` plus a peer that adds a read-only Value pattern and moves one percent per press.

Combo boxes (`Group by`, `Sort by`):

| Key | Effect |
|---|---|
| `Up`, `Down` | previous or next choice; the list regroups or re-sorts immediately. Status says nothing (the combo box speaks the new choice, and the rows shown are the same) |
| `Alt+Down`, `F4` | open the drop-down; `Enter` chooses, `Escape` closes without a change |
| `Home`, `End` | first or last choice |
| first letter | jumps to the choice starting with that letter |

Log panel (`Log`, a read-only list box of the last 200 log lines, each announced as
`<level> <time>: <message>`; `<time>` is `HH:mm:ss`, or `yyyy-MM-dd HH:mm:ss` for a line from another
day):

| Key | Effect |
|---|---|
| `Up`, `Down`, `Home`, `End`, `Page Up`, `Page Down` | move through the lines |
| `Alt+L` (`Copy log`) | copies every line as `<yyyy-MM-dd HH:mm:ss +hh:mm> <level> <message>` to the clipboard |

The log panel is `LiveSetting="Off"`: new lines never interrupt you. While focus is on the oldest
line, new lines never remove it (the panel can then hold up to 400 lines and returns to 200 once focus
moves on). The full log is in the daily log file,
`%ProgramData%\UefiAccess\logs\uefiaccess-<yyyyMMdd>.log`.

Status box (`Status`, a read-only text box): `Left`, `Right`, `Home`, `End`, `Ctrl+A`, `Ctrl+C` read
and copy the current text. The Status box is the one text box whose spoken name carries its text
(`Status: <message>`), because screen readers announce a live region by its name. So landing on it
reads the message as the name and once more as the box's value, and an announcement made while focus
is on the box may be heard twice.

### The Dell settings region

The region `Dell settings` shows the Dell BIOS settings of this computer, in the words of the BIOS
setup screen (F2), and changes none of them. Nothing is read when the window opens or on a timer; the
settings are read only when you press `Read Dell settings` (`Alt+S`, from anywhere in the main
window), with the help text
`Loads the agreed Dell BIOS settings from this computer through Dell Command Monitor, or Dell Command Configure when that is missing. Changes nothing. Access key Alt+S.`
The button is never disabled, so `Alt+S` always answers. A read takes a second or two and stops at a
time limit of 120 seconds; you can go on working meanwhile.

The list `Dell BIOS settings` has one row per setting, ten in all: `Boot list option`, `Secure Boot`,
`SATA operation`, `TPM security`, `TPM activation`, `Virtualization`, `VT for Direct I/O`, `Integrated
NIC`, `Wake on LAN`, `Num Lock at boot`. The rows are there from the start, the first selected, and a
read changes their words in place: no row is added, removed or made again. Each row is named by one
sentence, the name first, for example `Num Lock at boot: Enabled. Possible values: Disabled, Enabled.`,
and a screen reader adds its position (`1 of 10`). No row carries an item status or a help text, and
no row says it is read-only: that nothing can be changed here is said once, on the button's help and
on the state line.

| Key | Where | Effect |
|---|---|---|
| `Alt+S` | anywhere in the main window | reads the Dell BIOS settings of this computer; focus stays where it is |
| `Up`, `Down`, `Home`, `End`, `Page Up`, `Page Down` | `Dell BIOS settings` | move the selection; Status says nothing |
| `Ctrl+Up`, `Ctrl+Down` | `Dell BIOS settings` | move focus, and the row that gets focus is selected too, so `Alt+Y` copies the row you are on |
| a letter | `Dell BIOS settings` | the next row whose name starts with it (`W` for `Wake on LAN`) |
| `Ctrl+C` | a row | copies the row's sentence to the clipboard; if the clipboard cannot be written, Status says `Copy failed: <reason>` |
| `Enter`, `Space` | a row | nothing: there is no detail pane for a Dell row |
| `Left`, `Right`, `Home`, `End`, `Up`, `Down`, `Ctrl+A`, `Ctrl+C` | the state line `Dell settings source` | read and copy its text; right after arrival its whole text is selected |
| `Alt+Y` (`Copy value`) | the button, the state line or the list | copies the selected Dell row's sentence, or the state line's text when it has focus |
| `Escape` | anywhere in the region | nothing; while the checks of a change run it cancels them |

The state line `Dell settings source` (help text
`When and where the Dell values were loaded, or why they could not be.`) is one line that wraps,
selected whole when keyboard focus arrives on it. It reads, before the first read,
`Not loaded yet. Read Dell settings (Alt+S) loads them from this computer. This application shows Dell BIOS settings and never changes them.`;
after a read that answered, `Loaded at <hh:mm:ss> from <source> on this computer, BIOS <version>. This application shows Dell BIOS settings and never changes them; they are changed in the BIOS setup (F2).`
(with the fallback note when Dell Command Configure answered, and the A18 warning on another BIOS); and
while a baseline folder is shown, the values that folder recorded. The values read from this computer
are kept for the life of the window; `Refresh` after a baseline folder shows them again without
reading, and only the next `Alt+S` replaces them.

## 9. Focus rules

Focus moves only in direct response to a key or when an action you started finishes; every other
action leaves focus where it is. The main rules:

- **The window opens** with focus on `Refresh` and Status reading
  `Ready. Press Refresh (Alt+R) to read the firmware variables, or Open baseline folder (Alt+O) to view a baseline offline.`
  Nothing loads by itself.
- **A load completes.** On success focus goes to the first row of the list (after a change written and
  verified, to the written variable's row), and Status says the Loaded sentence. On a refusal or
  failure focus goes to the `Status` box, which holds the one-sentence reason. So after `Alt+R` you end
  up either on the data or on the sentence that explains why there is none.
- **`Enter` or `Space` on a row** moves focus into the detail pane at its first field; **`Escape` in
  the detail pane** returns to the list at the same row. Selecting a row with the arrows leaves focus
  in the list (the row's sentence is what you hear).
- **`F6` / `Shift+F6`** move to the entry control of the next or previous region (section 4), skipping
  a region none of whose controls can take focus; only the region landed in is announced.
- **An action started from its own button** (Refresh or Open baseline folder pressed while focus was
  on that button, or the checks or writing of a change) moves focus to the `Status` box, because the
  button is disabled while the action runs and a disabled control cannot keep focus. The completed
  action then moves focus as the load rule says.
- **A change.** The checks run while you keep working; their end opens the `Confirm change` dialog, or
  moves focus to the `Status` box when they refuse (`Change blocked: <reason>`). While a change is
  written, nothing can start another load or change, the window does not close, and UefiAccess refuses
  a log off, shut down or restart (`A change is being written; wait for its result.`). The result is
  announced once, with the Loaded sentence of the list read again.
- **A Dell read** announces its answer, and moves focus to the selected Dell row (or the Status box on
  a problem with no earlier values), only when you have not moved on and nothing else was started
  since `Alt+S`; otherwise the rows and the state line take the result quietly. `Alt+S` answers while
  a read runs, while the variables load, during a change, or while a baseline folder is shown with a
  one-sentence explanation each time.

**The deferred detail-pane rebuild.** The detail pane is not rebuilt inside the key press that
selected a row. The rebuild is held while a key or mouse button is down in the list and, when it comes
up, waits until the list has been quiet for 300 ms (`DetailRefreshTiming.QuietPeriod`), then runs at
the window's next idle moment. A key pressed during the wait withdraws the waiting rebuild and starts
the 300 ms again, so a burst of quick presses costs one rebuild, after the last key. Quick separate
presses are about 150 to 250 ms apart, so a 300 ms quiet period keeps the rebuild from falling between
two presses of a burst; the rebuild then runs while the screen reader speaks the row you stopped on.
Every way of moving keyboard focus into the pane (`Enter` or `Space` on a row, `F6` to `Detail`, `Tab`
from a splitter, a click, `Copy value`, `Export visible list`) refreshes it first without waiting for
the 300 ms. The rebuild updates the pane in place: sections and fields the shown row and the new row
both have are kept, with only their text changed, and only the differing ones are added or removed.
The rebuild says nothing and moves no focus, and it is the one timed step of the application's own
code (finding A2-7): the no-timer rule has this one exception, pinned by a test to the one file that
holds it, because merging separate presses needs a wait after the last key.

**`Showing <N> of <M> variables`** is said only when the filter text or either count differs from the
list as the window applied it last; a change of `Group by` or `Sort by` alone changes none of them and
says nothing. `Clear filter` and `Escape` in the filter box say the count once per press.

**The list is rebuilt** whenever its content changes (`Clear filter`, a `Group by`/`Sort by` change,
`Escape` in the filter box, every load). When keyboard focus is on a row or header at that moment, the
window puts it back as soon as the new list is on screen: on the same row (the selected variable
survives the rebuild), or on the header of the same group, falling back to the selected row, the first
row, the first header, or the empty list. This is one deferred step at the end of the same key press,
not a timer; without it stock WPF would drop the focus on the window.

## 10. Quick reference card

```
Alt+R Refresh          Alt+O Open baseline folder   Alt+C Clear filter
Alt+Y Copy value       Alt+X Export visible list    Alt+L Copy log
Alt+S Read Dell settings   loads this computer's Dell BIOS settings; changes nothing

F6 / Shift+F6          next / previous region: Toolbar, Filter, List, Detail, Log, Dell settings, Status
Ctrl+F                 filter box, text selected
Escape                 filter box: clear and back to the list; detail pane: back to the list

List                   arrows, Home/End (whole list), Page Up/Down and type a name (in the group),
                       Enter/Space = details (on a header: collapse/expand), Ctrl+C = copy key,
                       Left/Right = collapse/expand group, Ctrl+Left/Right = scroll the columns
Detail pane            Tab = next field (its value selected whole; the caret at the start of one
                       of 512 characters or more), Up/Down = line by line,
                       Escape = back to the list, Alt+Y = copy this value
Splitters              Left/Right (list and details), Up/Down (log panel): one percent per press,
                       Status says both panes' shares; Escape = cancel a mouse drag

Change group           Alt+U Move up, Alt+D Move down, Alt+V Review change (BootOrder);
                       Alt+B Boot once from this entry (an active boot entry);
                       Alt+M Clear one-time boot (BootNext); Alt+P Restore previous value
Boot order entries     Up/Down select, Ctrl+Z = the firmware's order again, Enter/Space = nothing
While the checks run   Escape = cancel them, wherever focus is
Confirm change dialog  Tab through the boxes and the check box, Alt+F = Confirm change,
                       Escape = Cancel; nothing is written before Confirm change

Dell settings region   Tab: Read Dell settings, the state line, the list Dell BIOS settings
Dell BIOS settings     arrows, Home/End, Page Up/Down, a letter = next name,
                       Ctrl+C = copy the row, Enter/Space = nothing
In the Dell region     Alt+Y = copy the selected row (the state line when it has focus); Escape = nothing
```
