using System.IO;
using System.Windows;
using System.Windows.Controls;
using MediaSorter.Core;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MediaSorter;

public partial class MainWindow : Window
{
    private List<MediaItem> _plan = [];
    private OrganizeOptions? _planOptions;
    private CancellationTokenSource? _cts;

    // ETA state: refreshed on every progress report and once a second in between,
    // so the estimate keeps moving while one large file is being copied.
    private Eta? _eta;
    private double _etaDone, _etaTotal;
    private readonly DispatcherTimer _etaTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        _etaTimer.Tick += (_, _) => UpdateEta();
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e) => PickFolder(SourceBox, "Choose the folder with your photos and videos");
    private void BrowseDest_Click(object sender, RoutedEventArgs e) => PickFolder(DestBox, "Choose where the Year\\Month folders should go");

    private void PickFolder(TextBox target, string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (Directory.Exists(target.Text)) dlg.InitialDirectory = target.Text;
        if (dlg.ShowDialog(this) == true) target.Text = dlg.FolderName;
    }

    private void Paths_Changed(object sender, TextChangedEventArgs e)
    {
        if (ScanButton is null) return; // fires during InitializeComponent
        ScanButton.IsEnabled = _cts is null && SourceBox.Text.Trim().Length > 0 && DestBox.Text.Trim().Length > 0;
        InvalidatePlan();
    }

    /// <summary>Options that change what a scan finds or which date it picks make the preview out of date.</summary>
    private void ScanOption_Changed(object sender, RoutedEventArgs e)
    {
        if (FileDateCheck is null) return; // fires during InitializeComponent
        // The fallback checkbox only matters when sorting by date taken.
        FileDateCheck.IsEnabled = ModeCombo.SelectedIndex == 0;
        InvalidatePlan();
    }

    private void InvalidatePlan()
    {
        if (RunButton is null || _cts is not null) return;
        if (RunButton.IsEnabled) StatusText.Text = "Options changed - scan again to update the preview.";
        RunButton.IsEnabled = false;
    }

    private OrganizeOptions CurrentOptions() => new(
        SourceBox.Text.Trim(),
        DestBox.Text.Trim(),
        IncludeSubfolders: SubfoldersCheck.IsChecked == true,
        CopyInsteadOfMove: CopyCheck.IsChecked == true,
        UseFileDateFallback: FileDateCheck.IsChecked == true,
        DeleteEmptySourceFolders: CleanupCheck.IsChecked == true,
        DownloadOnlineOnlyFiles: DownloadCheck.IsChecked == true,
        Mode: ModeCombo.SelectedIndex == 1 ? DateMode.Modified : DateMode.DateTaken);

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var opt = CurrentOptions();
        if (!Directory.Exists(opt.SourceFolder)) { Warn("The source folder doesn't exist."); return; }
        if (string.Equals(Path.GetFullPath(opt.SourceFolder).TrimEnd('\\'), Path.GetFullPath(opt.DestinationFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            // Allowed, but worth knowing: files get sorted in place.
            StatusText.Text = "Destination is the same as the source - files will be sorted in place.";
        }

        BeginWork("Scanning…");
        try
        {
            var label = opt.Mode == DateMode.Modified ? "Reading file dates" : "Reading metadata";
            var progress = new Progress<(int done, int total)>(p => ShowProgress(p.done, p.total, p.done, p.total, label));
            var plan = await Task.Run(() => Organizer.BuildPlan(opt, progress, _cts!.Token));
            _plan = plan;
            _planOptions = opt;
            Grid.ItemsSource = _plan;

            var took = Eta.FormatElapsed(_eta!.Elapsed);
            var byMeta = plan.Count(i => i.Detected.Source is DateSource.PhotoMetadata or DateSource.VideoMetadata);
            var byName = plan.Count(i => i.Detected.Source == DateSource.FileName);
            var low = plan.Count(i => i.Detected.Source == DateSource.FileModified);
            var unknown = plan.Count(i => i.Detected.Source == DateSource.None);
            var online = plan.Count(i => i.IsOnlineOnly);
            var chosen = plan.Count(i => i.Detected.Source == DateSource.ModifiedChosen);
            var breakdown = opt.Mode == DateMode.Modified
                ? $"{chosen:N0} by modified date, {unknown:N0} unknown"
                : $"{byMeta:N0} from metadata, {byName:N0} from filename, {low:N0} from file date, {unknown:N0} unknown";
            StatusText.Text = plan.Count == 0
                ? "No photos or videos found."
                : $"{plan.Count:N0} files ({took}): {breakdown}"
                  + (online > 0 ? $", {online:N0} skipped (OneDrive online-only)" : "") + ". Review, then click Organize.";
            if (online > 0)
                MessageBox.Show(this,
                    $"{online:N0} files are OneDrive \"online-only\" - they aren't stored on this PC, so their dates can't be read and they won't be moved.\n\n" +
                    "To include them, either right-click the folder in Explorer → \"Always keep on this device\" and wait for it to download, " +
                    "or tick \"Download OneDrive online-only files\" (OneDrive must be running) and scan again.",
                    "Some files aren't downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { StatusText.Text = "Scan cancelled."; }
        catch (Exception ex) { Warn(ex.Message); StatusText.Text = "Scan failed."; }
        finally { EndWork(); RunButton.IsEnabled = _plan.Any(i => i.Status == ItemStatus.Pending); }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_planOptions is null || _plan.Count == 0) return;
        // Use the option checkboxes as they are now for move/copy/cleanup, but the folders from the scan.
        var now = CurrentOptions();
        var opt = _planOptions with
        {
            CopyInsteadOfMove = now.CopyInsteadOfMove,
            DeleteEmptySourceFolders = now.DeleteEmptySourceFolders,
        };

        var pending = _plan.Count(i => i.Status == ItemStatus.Pending);
        var verb = opt.CopyInsteadOfMove ? "Copy" : "Move";
        if (MessageBox.Show(this,
                $"{verb} {pending:N0} files into Year\\Month folders under:\n{opt.DestinationFolder}\n\nA log is saved there so you can undo this.",
                "Organize files", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        BeginWork(opt.CopyInsteadOfMove ? "Copying…" : "Moving…");
        try
        {
            var plan = _plan;
            // Weight progress by size (plus a fixed per-file cost) so big videos don't wreck the estimate.
            const long perFileOverhead = 256 * 1024;
            var cumulative = new double[plan.Count + 1];
            var pendingBefore = new int[plan.Count + 1];
            for (int i = 0; i < plan.Count; i++)
            {
                var isPending = plan[i].Status == ItemStatus.Pending;
                cumulative[i + 1] = cumulative[i] + (isPending ? plan[i].SizeBytes + perFileOverhead : 0);
                pendingBefore[i + 1] = pendingBefore[i] + (isPending ? 1 : 0);
            }
            var label = opt.CopyInsteadOfMove ? "Copying" : "Moving";
            var progress = new Progress<(int done, int total)>(p =>
                ShowProgress(cumulative[p.done], cumulative[^1], pendingBefore[p.done], pending, label));
            var s = await Task.Run(() => Organizer.Execute(plan, opt, progress, _cts!.Token));
            var done = opt.CopyInsteadOfMove ? $"{s.Copied:N0} copied" : $"{s.Moved:N0} moved";
            StatusText.Text = $"Done in {Eta.FormatElapsed(_eta!.Elapsed)}: {done}, {s.Duplicates:N0} duplicates skipped, {s.Failed:N0} failed. Log: {s.LogPath}";
            if (_cts!.IsCancellationRequested) StatusText.Text = "Stopped part-way. " + StatusText.Text;
        }
        catch (Exception ex) { Warn(ex.Message); StatusText.Text = "Organize failed."; }
        finally { EndWork(); RunButton.IsEnabled = _plan.Any(i => i.Status == ItemStatus.Pending); }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick a MediaSorter log to undo",
            Filter = "MediaSorter logs|MediaSorter-log-*.csv|CSV files|*.csv",
        };
        if (Directory.Exists(DestBox.Text)) dlg.InitialDirectory = DestBox.Text;
        if (dlg.ShowDialog(this) != true) return;

        if (MessageBox.Show(this, "Move every file from this run back to where it came from?", "Undo",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var (restored, skipped) = Organizer.Undo(dlg.FileName);
            StatusText.Text = $"Undo: {restored:N0} files restored, {skipped:N0} skipped (missing or original path taken).";
            _plan = []; Grid.ItemsSource = null; RunButton.IsEnabled = false;
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void BeginWork(string text)
    {
        _cts = new CancellationTokenSource();
        StatusText.Text = text;
        Progress.Value = 0;
        _eta = new Eta();
        _etaDone = _etaTotal = 0;
        EtaText.Text = "";
        _etaTimer.Start();
        ScanButton.IsEnabled = RunButton.IsEnabled = UndoButton.IsEnabled = false;
        SourceBox.IsEnabled = DestBox.IsEnabled = false;
        CancelButton.IsEnabled = true;
    }

    private void EndWork()
    {
        _cts?.Dispose();
        _cts = null;
        _etaTimer.Stop();
        EtaText.Text = "";
        CancelButton.IsEnabled = false;
        UndoButton.IsEnabled = SourceBox.IsEnabled = DestBox.IsEnabled = true;
        ScanButton.IsEnabled = SourceBox.Text.Trim().Length > 0 && DestBox.Text.Trim().Length > 0;
        Grid.Items.Refresh();
    }

    /// <param name="doneUnits">Work finished, in whatever unit tracks cost (files or bytes).</param>
    /// <param name="files">Files finished, for the "x / y" counter.</param>
    private void ShowProgress(double doneUnits, double totalUnits, int files, int totalFiles, string label)
    {
        Progress.Maximum = Math.Max(1, totalUnits);
        Progress.Value = doneUnits;
        StatusText.Text = $"{label}… {files:N0} / {totalFiles:N0}";
        _etaDone = doneUnits;
        _etaTotal = totalUnits;
        UpdateEta();
    }

    private void UpdateEta()
    {
        if (_eta is null || _cts is null) return;
        EtaText.Text = Eta.Format(_eta.Remaining(_etaDone, _etaTotal));
    }

    private void Warn(string msg) => MessageBox.Show(this, msg, "Media Sorter", MessageBoxButton.OK, MessageBoxImage.Warning);
}
