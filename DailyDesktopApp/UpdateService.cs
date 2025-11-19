using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DailyDesktopApp
{
    public static class UpdateService
    {
        public static async Task CheckForUpdatesAsync()
        {
            // Correct constructor for your Velopack build
            var source = new GithubSource(
                "https://github.com/roydestroy/eurognosi-online-classroom",
                null,     // repoTag (null = auto)
                false,    // include prereleases?
                null      // downloader (null = default)
            );

            var mgr = new UpdateManager(source);

            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
                return; // no updates found

            // download delta/full update
            await mgr.DownloadUpdatesAsync(info);

            // apply update and restart the app
            mgr.ApplyUpdatesAndRestart(info);
        }
    }
}
