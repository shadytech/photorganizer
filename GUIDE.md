# Media Sorter User Guide

Media Sorter goes through a folder of photos and videos, works out when each one was taken, and moves it into folders by year and month:

```
Destination\
  2022\
    10 - October\
    11 - November\
  2023\
    05 - May\
  Unknown Date\
  MediaSorter-log-20261108-210630.csv
```

Nothing moves until you've seen a preview and confirmed it, and every run can be undone.

---

## 1. Getting the app

### Where the executable is

The app is a single file, **`MediaSorter.exe`**, in the **`publish`** folder inside the project folder:

```
<project folder>\publish\MediaSorter.exe
```

Double-click it to start. You can copy that one file anywhere you like (Desktop, a USB stick, another PC), since it doesn't need any other files beside it.

The `publish` folder isn't stored in the GitHub repo. If you've just downloaded or cloned the repo, build the exe first (next section).

### Building the exe

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). In a terminal, from the project folder, run:

```
dotnet publish MediaSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

When it finishes, the exe is at `publish\MediaSorter.exe`.

### Requirements for running it

- Windows 10 or 11 (64-bit)
- The **.NET 10 Desktop Runtime**. If it's missing, Windows shows a message with a download link the first time you open the app.

---

## 2. The window at a glance

| Area | What it's for |
|---|---|
| **Source folder** | The folder with the photos and videos you want sorted. |
| **Destination folder** | Where the Year\Month folders are created. It can be a new empty folder, an existing library, a network drive, or even the same as the source. |
| **Sort by** | Which date decides the folder (see [Choosing which date to sort by](#3-choosing-which-date-to-sort-by)). |
| **Checkboxes** | Options for the scan and the move (see [Options](#4-options)). |
| **1. Scan & Preview** | Reads the dates and shows where every file will go. Doesn't move anything. |
| **2. Organize** | Moves (or copies) the files, after asking you to confirm. |
| **Cancel** | Stops a scan or a move partway through. Files already moved stay moved, and they're in the log. |
| **Undo a run…** | Puts files from an earlier run back where they came from. |
| **Preview table** | One row per file: name, date, where the date came from, destination, and status. |
| **Bottom bar** | Progress, a count such as "Moving… 120 / 870", and the estimated time left. |

---

## 3. Choosing which date to sort by

### Date taken (from photo/video data), the default

The app reads the date the camera or phone recorded inside the file. It works through these sources in order and uses the first one that gives a date:

1. **Photo metadata (EXIF "Date Taken")**: JPG, HEIC/HEIF, PNG, TIFF, WebP and RAW formats (CR3, CR2, NEF, ARW, DNG, ORF, RW2, RAF and others).
2. **Video metadata**: MOV, MP4, M4V, 3GP and AVI. Videos often store the time in UTC, and the app converts it to this PC's time zone.
3. **A date in the filename**, for names like:
   - `IMG_20230514_142233.jpg`
   - `VID-20230514-WA0001.mp4` (WhatsApp)
   - `Screenshot_2023-05-14-14-22-33.png`
   - `FB_IMG_1684072953123.jpg`
4. **The file's modified date**, only if "Use file date when no metadata is found" is ticked. These rows are shaded **amber** in the preview because that date may not be when the picture was taken.
5. If none of those works, the file goes to **`Unknown Date`**.

### File modified date

This mode uses only the **Date modified** that Windows Explorer shows, and ignores the date inside the file. It's much faster because files aren't opened: a scan of 870 files on a network drive takes under a second instead of several seconds.

Use it when you specifically want files grouped by when they were last saved or changed, rather than when they were taken.

> **Film scans and edited exports:** scanned film and many edited exports have no camera date inside them. In "Date taken" mode they fall back to the modified date, which is usually when they were scanned or exported, not when the photo was taken. They're shaded amber in the preview so you can spot them.

---

## 4. Options

| Option | Default | What it does |
|---|---|---|
| **Include subfolders** | On | Also scans every folder inside the source folder. |
| **Use file date when no metadata is found** | On | Falls back to the modified date for files with no camera date. When it's off, those files go to `Unknown Date` instead. Greyed out in "File modified date" mode. |
| **Copy instead of move** | Off | Copies files and leaves the originals where they are. Handy for a trial run. |
| **Delete empty folders left in source** | On | After a move, removes folders in the source that are now empty. The source folder itself is never deleted. |
| **Download OneDrive online-only files** | Off | See [OneDrive files](#7-onedrive-files). |

If you change **Sort by**, **Include subfolders**, **Use file date…** or **Download OneDrive…** after scanning, the **Organize** button turns off until you scan again. That way the move always matches what you saw in the preview.

---

## 5. Step by step

1. **Pick the folders.** Click **Browse…** next to *Source folder* and *Destination folder*, or paste a path such as `Z:\General Shared\Photos`.
2. **Choose your options**, including **Sort by**.
3. **Click "1. Scan & Preview".** The table fills in, and the bottom bar shows a summary such as:
   > 870 files (5s): 590 from metadata, 0 from filename, 280 from file date, 0 unknown.
4. **Check the preview.**
   - The **Date from** column tells you how reliable each date is.
   - **Amber** rows have a fallback date and are worth a glance.
   - **Grey** rows are online-only OneDrive files that will be skipped.
   - You can click a column header to sort, for example by *Goes to*.
5. **Click "2. Organize"** and confirm. The bottom bar shows progress and the time left.
6. **Read the result** in the status bar:
   > Done in 3m 12s: 532 moved, 67 duplicates skipped, 0 failed. Log: …\MediaSorter-log-20261108-210353.csv

> **Tip:** the first time, try it on a copy of a few hundred photos, or tick **Copy instead of move**, until you're happy with the results.

### What the Status column means

| Status | Meaning |
|---|---|
| **Pending** | Not processed yet. |
| **Moved** / **Copied** | Done. If the name was already taken, it says "renamed to avoid name clash". |
| **Duplicate** | A byte-for-byte identical file was already in the destination, so this one was left where it was. |
| **Skipped** | Not processed, for example an online-only OneDrive file. |
| **Failed** | Couldn't be moved (it was in use, access was denied, the disk was full, and so on). The reason is shown and the file stays where it was. |

### Files with the same name

- **Different files, same name, same month:** the second one is renamed, for example `IMG_0001 (2).jpg`. Nothing is overwritten.
- **Identical files:** only one copy ends up in the destination. The extra copy stays in the source and is marked **Duplicate**, so you can delete it yourself once you've checked.

---

## 6. Undoing a run

Every Organize run saves a log file in the destination folder, named like `MediaSorter-log-20261108-210353.csv`. You can open it in Excel to see exactly what moved where.

To reverse a run:

1. Click **Undo a run…**.
2. Pick the log file for that run.
3. Confirm.

Every file that run moved goes back to its original location, and the Year\Month folders it emptied are removed.

Some things to know about undo:

- Undo only reverses **moves**. After a copy, your originals are still in place, so you can just delete the copied folders if you don't want them.
- A file is skipped, and left where it is, if it's no longer in the destination or if something else now occupies its original location.
- Folders removed by "Delete empty folders left in source" are recreated as needed.

---

## 7. OneDrive files

OneDrive can keep files **online-only**: they show in Explorer with a cloud icon, but they aren't actually stored on the PC. By default Media Sorter:

- detects these without downloading them,
- shows them as grey **Skipped** rows ("online-only in OneDrive, not on this PC"),
- doesn't move them,
- and tells you how many there were after the scan.

To include them, do either of these:

- **Recommended:** in Explorer, right-click the folder, choose **Always keep on this device**, and wait for the download to finish before scanning.
- Tick **Download OneDrive online-only files** and scan again. OneDrive must be running, and this can take a long time for a big library.

---

## 8. Time-left estimate

During a scan or a move, the text left of the progress bar shows the estimated time remaining:

- "estimating time…" for the first couple of seconds,
- then "a few seconds left", "about 40 sec left", "about 3 min left", "about 1 hr 20 min left" and so on.

For moves and copies the estimate is based on file sizes, so a handful of large videos won't throw it off. Moving files within the same drive is nearly instant. Moving or copying to another drive or a network share depends on its speed.

---

## 9. Supported file types

**Photos:** JPG, JPEG, JPE, HEIC, HEIF, PNG, GIF, BMP, TIF, TIFF, WebP, DNG, CR2, CR3, NEF, ARW, ORF, RW2, RAF, SRW, PEF

**Videos:** MP4, MOV, M4V, 3GP, 3G2, AVI, MTS, M2TS, MKV, WMV

Anything else (documents, ZIP files and so on) is left alone. Hidden and system files are ignored, and so are macOS `._` sidecar files.

---

## 10. Troubleshooting

**Videos show an hour (or more) off from the photos taken at the same time.**
Video times stored in UTC are converted using *this PC's* time zone. If you were in a different time zone when you filmed, the time is shifted. That only changes the folder if the video was shot within an hour or so of midnight at the end of a month.

**Lots of files land in the current month.**
They probably have no camera date (downloads, screenshots from some apps, files sent through messaging apps that strip metadata), so they fell back to the modified date. Untick **Use file date when no metadata is found** to send them to `Unknown Date` instead.

**Film scans are in the wrong year.**
Scans have no camera date, so they use the date they were scanned. Sort those folders by hand, or move them after organizing.

**"Failed" rows.**
The file was probably open in another program, read-only, or on a drive that's full or disconnected. Fix the cause and click **1. Scan & Preview** again. Files that already moved won't be picked up twice.

**The month names are in another language.**
Month folder names follow your Windows display language.

**The destination is inside the source folder.**
That's fine. Anything already in the destination folder is skipped when the source is scanned.
