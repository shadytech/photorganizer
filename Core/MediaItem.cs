using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace MediaSorter.Core;

public enum ItemStatus { Pending, Moved, Copied, Duplicate, Skipped, Failed }

/// <summary>One file in the preview grid.</summary>
public sealed class MediaItem : INotifyPropertyChanged
{
    public required string SourcePath { get; init; }
    public required DetectedDate Detected { get; init; }
    public required string PlannedPath { get; init; }
    public long SizeBytes { get; init; }

    public string FileName => Path.GetFileName(SourcePath);
    public string DateText => Detected.Date?.ToString("yyyy-MM-dd HH:mm") ?? "-";
    public string SourceText => Detected.Source switch
    {
        DateSource.PhotoMetadata => "Photo metadata",
        DateSource.VideoMetadata => "Video metadata",
        DateSource.FileName => "Filename",
        DateSource.FileModified => "File date (low confidence)",
        DateSource.OnlineOnly => "Not checked (online-only)",
        DateSource.ModifiedChosen => "File modified date",
        _ => "Unknown",
    };
    public bool IsLowConfidence => Detected.Source is DateSource.FileModified or DateSource.None;
    public bool IsOnlineOnly => Detected.Source == DateSource.OnlineOnly;

    private string _finalPath = "";
    public string FinalPath { get => _finalPath; set => Set(ref _finalPath, value); }

    private ItemStatus _status = ItemStatus.Pending;
    public ItemStatus Status { get => _status; set { Set(ref _status, value); OnChanged(nameof(StatusText)); } }

    private string _message = "";
    public string Message { get => _message; set { Set(ref _message, value); OnChanged(nameof(StatusText)); } }

    public string StatusText => string.IsNullOrEmpty(Message) ? Status.ToString() : $"{Status}: {Message}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(n);
    }
}
