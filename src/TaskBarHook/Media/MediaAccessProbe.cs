using System.Text;
using Windows.Media.Control;

namespace TaskBarHook.Media;

public static class MediaAccessProbe
{
    public static async Task<string> RunAsync()
    {
        var report = new StringBuilder();
        report.AppendLine($"OS: {Environment.OSVersion}");
        report.AppendLine($"Process x64: {Environment.Is64BitProcess}");
        report.AppendLine($"User: {Environment.UserName}");
        report.AppendLine($"Admin: {IsAdministrator()}");

        try
        {
            var family = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            report.AppendLine($"Package identity: {family}");
        }
        catch (Exception ex)
        {
            report.AppendLine($"Package identity: none ({ex.GetType().Name})");
        }

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = manager.GetSessions();
            report.AppendLine($"SMTC RequestAsync: OK");
            report.AppendLine($"Sessions: {sessions.Count}");
            foreach (var session in sessions)
            {
                var playback = session.GetPlaybackInfo();
                GlobalSystemMediaTransportControlsSessionMediaProperties? properties = null;
                try
                {
                    properties = await session.TryGetMediaPropertiesAsync();
                }
                catch (Exception ex)
                {
                    report.AppendLine($"  properties error: {ex.GetType().Name}: {ex.Message}");
                }

                var timeline = session.GetTimelineProperties();
                report.AppendLine(
                    $"- source={session.SourceAppUserModelId} status={playback.PlaybackStatus} " +
                    $"title={properties?.Title ?? "(none)"} artist={properties?.Artist ?? "(none)"} " +
                    $"play={playback.Controls.IsPlayEnabled} pause={playback.Controls.IsPauseEnabled} " +
                    $"toggle={playback.Controls.IsPlayPauseToggleEnabled} next={playback.Controls.IsNextEnabled} " +
                    $"seek={playback.Controls.IsPlaybackPositionEnabled}");
                report.AppendLine(
                    $"  timeline start={timeline.StartTime} end={timeline.EndTime} position={timeline.Position} " +
                    $"minSeek={timeline.MinSeekTime} maxSeek={timeline.MaxSeekTime} updated={timeline.LastUpdatedTime:o}");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"SMTC RequestAsync failed: {ex}");
        }

        return report.ToString();
    }

    public static async Task<string> RunSeekRoundtripAsync()
    {
        var report = new StringBuilder();
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetSessions().FirstOrDefault(item =>
            item.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            report.AppendLine("No Spotify session.");
            return report.ToString();
        }

        var playback = session.GetPlaybackInfo();
        var before = session.GetTimelineProperties();
        var original = before.Position;
        var wasPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var target = TimeSpan.FromSeconds(40);
        report.AppendLine($"before status={playback.PlaybackStatus} seek={playback.Controls.IsPlaybackPositionEnabled} position={original} start={before.StartTime} end={before.EndTime} min={before.MinSeekTime} max={before.MaxSeekTime}");
        if (!playback.Controls.IsPlaybackPositionEnabled)
        {
            report.AppendLine("Seek disabled.");
            return report.ToString();
        }

        var changed = await session.TryChangePlaybackPositionAsync(target.Ticks);
        await Task.Delay(900);
        var mid = session.GetTimelineProperties();
        var restored = await session.TryChangePlaybackPositionAsync(original.Ticks);
        await Task.Delay(700);
        var after = session.GetTimelineProperties();
        var stillPlaying = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        report.AppendLine($"seek40 ok={changed} position={mid.Position}");
        report.AppendLine($"restore {original} ok={restored} position={after.Position}");
        report.AppendLine($"playback preserved playingBefore={wasPlaying} playingAfter={stillPlaying}");
        return report.ToString();
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
