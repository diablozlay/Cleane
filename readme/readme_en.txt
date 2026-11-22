============================================================
WIN/ZW Data Search / Cleanup Tool — User Guide
============================================================

A friendly little tool that does two things: finds files fast and sweeps away Windows junk. It also has a built-in cleaner for ZW3D user folders. Nothing scary here — just follow along

■ 1. First, about permissions
------------------------------------------------------------
- The program asks for administrator rights on startup (cleaning system folders and removing accounts needs them).
- The window shows your current status:
  - ● Administrator: all good.
  - ● Standard rights: system folders, accounts, etc. can't be cleaned. Click Restart as administrator to fix that.

■ 2. The three main features
------------------------------------------------------------
  ・Search → Find files by name / type / location, then open, copy the path, or delete them
  ・Clean items → Tick the Windows junk you want gone, preview first, then clean
  ・User Folder Manager → Clean up ZW3D user folders (same actions as the official User Folder Manager)

■ 3. Searching for files
------------------------------------------------------------
1. Keyword: type a file name or keyword. Wildcards work, e.g. *.pdf or report*2024. If you pick a *Type*, the keyword can stay empty.
2. Type: Documents, Spreadsheets, Presentations, Images, Videos, Music, Archives, Programs & installers, CAD / 3D, Temp & log files — or Custom extensions… (separate several with commas or spaces, e.g. pdf, dwg, z3prt). There are also ZW3D types: all / temp junk only / model files only.
3. Location: system drive, all drives (including external), a specific drive, your user folder, or Choose folder… to pick your own.
4. Exact name match: when ticked, the file name must be exactly what you typed (e.g. setup.exe), so enter the full name.
5. Permanently delete files: when ticked, "Delete" in the results window deletes straight away, skipping the Recycle Bin. Use with care.
6. Click Search (or press Enter in the box). Up to 200,000 results are shown; if you hit that, make your keyword more specific. You can click Stop search at any time.

▼ The results window
- The filter box at the top narrows results by name or path.
- Columns: Name, Location, Size, Date modified.
- Double-click or press Enter to open the file's location (with the file selected). Right-click for more options.
- Buttons:

  ・Open file location → Shows the file selected in File Explorer
  ・Open folder → Opens the containing folder
  ・Open file → Opens the file (programs/scripts ask first before running)
  ・Copy path → Copies the full path (right-click also offers "copy file name")
  ・Select all / Select none → One toggle button. After "Select all", only Delete, Permanent delete and Close stay enabled
  ・Delete → Moves to the Recycle Bin (restorable; hold Shift while clicking to delete permanently)
  ・Permanent delete → Skips the Recycle Bin, cannot be undone (shortcut: Shift+Delete)
  ・Stop search / Close → Exactly what they say

- You'll always get a confirmation before deleting. Files inside Windows or program folders trigger an extra system file warning — think twice when you see it.
- Files in use or without permission are skipped, and the result message tells you how many. Close the program using them, or run as administrator, and try again.

■ 4. Cleaning Windows junk
------------------------------------------------------------
Items are grouped by risk and color-coded:

● Recommended (SAFE) — ticked by default
- Temp files *.tmp / *._mp (scans the whole system drive)
- System and user temp folders
- Internet temp files (IE / legacy Edge cache)
- Recent documents list
- Prefetch (next boot will be a bit slower once, then back to normal)

● Advanced (Caution) — check before ticking
- Empty the Recycle Bin (all drives, can't be undone)
- Log files *.log (some programs' logs will vanish)
- *.gid / *.chk / *.old
- Windows backup files *.bak
- Cookies (legacy IE/Edge; you'll be signed out of sites)
- Windows Disk Cleanup (cleanmgr): click Set up Disk Cleanup… first and tick what you want

● Danger — delete actions from the original batch file, irreversible
- Delete the whole \d folder on the system drive
- Delete the aspnet user account (only exists with old IIS/.NET)
- Ticking any Danger item first shows a two-step warning; you must click OK on both. If you don't understand it, don't tick it!

▼ Bottom buttons
- Select all (excluding danger): ticks SAFE and Advanced items, never Danger ones.
- Select none / Restore defaults: clear all ticks / back to the default (SAFE only).
- Preview: only calculates, deletes nothing, and tells you roughly how much space you'd free. Strongly recommended first!
- View list: after a preview, lists the files that would be cleaned (first 200,000) so you can open their locations and check.
- Start cleaning: cleans for real after confirmation. When done, it reports files removed, space freed and time taken.
- Stop: changed your mind mid-clean? Press this.
- The LOG area at the bottom shows what's happening live.

■ 5. ZW3D User Folder Manager
------------------------------------------------------------
It mirrors the official User Folder Manager, with the same button names:
1. Open User Folder Manager. The program detects the ZW3D versions on this PC (e.g. ZW3D 2025, ZW3D WuKong 2027). Pick one from the drop-down; the path fills in automatically, and Open takes you to the folder.
2. Available actions:
   - Clear auto output files
   - Clear custom configurations
   - Clear temp files
   - Clear user folder
   - Save current custom configuration (backup) / Import backup configuration (restore)
   - Delete subfolders...: tick the subfolders to remove and press Delete selected (this returns settings to factory defaults)
3. Buttons whose target folder doesn't exist are greyed out (nothing to clean).
4. Important: cleaning makes ZW3D quit. Save your work and close ZW3D first; you'll get one more confirmation before anything is deleted. Want to keep your settings? Use *Save current custom configuration* first.

■ 6. Changing language
------------------------------------------------------------
15 languages are supported (Chinese Traditional/Simplified, English, 日本語, 한국어, Deutsch, Français, Español, Italiano, Português, Polski, Čeština, Magyar, Русский, Türkçe). Pick one from the language menu and the program restarts itself with the new language, keeping your window size.

■ 7. Quick tips
------------------------------------------------------------
- Preview first, clean second — the safest habit.
- "Permanent delete" really means gone; even the Recycle Bin can't help.
- Don't tick what you don't understand, and leave the Danger items alone unless you're sure.
- Always close ZW3D before cleaning its folders.
- Window size/position and language are stored in %AppData%\SystemJunkCleaner.

Happy cleaning, and enjoy a snappier PC!
