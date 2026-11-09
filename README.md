# Media Sorter

A Windows app that reads the date each photo or video was taken and moves it into `Year\MM - Month` folders.

**📖 New here? Read the [User Guide](GUIDE.md)** for where the app is, how to use it, and how to undo a run.

```
Destination\
  2023\
    05 - May\
    12 - December\
  2024\
    03 - March\
  Unknown Date\
  MediaSorter-log-20261108-143000.csv
```

## How it picks the date

1. Photo EXIF **Date Taken** (JPG, HEIC, PNG, RAW)
2. Video creation date (MOV, MP4, M4V, 3GP, AVI). Times stored in UTC are converted to local time
3. A date in the filename: `IMG_20230514_142233`, `VID-20230514-WA0001`, `Screenshot_2023-05-14-…`, `FB_IMG_1684072953123`
4. The file's modified date (optional; these rows show in amber in the preview)
5. Otherwise the file goes to `Unknown Date`

To ignore all of that and sort purely by the date Explorer shows as "Date modified", set **Sort by** to **File modified date**. It's much faster since files aren't opened.

## Using it

1. Pick a **source** and a **destination** folder.
2. **Scan & Preview** reads the dates but leaves every file where it is.
3. **Organize** moves the files (or copies them, if you tick "Copy instead of move").
   - If two files have the same name, the second is renamed: `photo (2).jpg`.
   - If a byte-identical file is already there, the new one is left in the source and marked **Duplicate**.
   - Next to the progress bar you'll see an estimate of the time left. For moves and copies it's based on file sizes, so a few big videos don't throw it off.
4. Every run writes a CSV log to the destination. **Undo a run…** reads a log and moves the files back.

## OneDrive online-only files

Files marked "online-only" in OneDrive aren't actually on the PC. By default they're listed as **Skipped** and never opened or moved. To include them, either right-click the folder → **Always keep on this device**, or tick **Download OneDrive online-only files** (OneDrive must be running).

## Build

Requires the .NET 10 SDK.

```
dotnet test tests/MediaSorter.Tests
dotnet publish MediaSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
