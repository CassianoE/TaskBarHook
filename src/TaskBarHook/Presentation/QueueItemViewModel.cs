using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskBarHook.Queue;

namespace TaskBarHook.Presentation;

public sealed partial class QueueItemViewModel : ObservableObject
{
    public QueueItemViewModel(QueueTrack track)
    {
        Id = track.Id;
        Title = track.Title;
        ImageUrl = track.ImageUrl;
        ApplyArtwork(track.ArtworkBytes);
    }

    public string Id { get; }

    public string Title { get; }

    public string? ImageUrl { get; }

    [ObservableProperty] private ImageSource? _artwork;

    [ObservableProperty] private bool _hasArtwork;

    public void ApplyArtwork(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            Artwork = null;
            HasArtwork = false;
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Artwork = image;
            HasArtwork = true;
        }
        catch
        {
            Artwork = null;
            HasArtwork = false;
        }
    }
}
