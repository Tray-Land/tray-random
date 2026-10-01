using Windows.ApplicationModel.DataTransfer;

namespace TrayRandom.Services;

internal static class ClipboardService
{
    /// <summary>Puts text on the clipboard. Returns false if another app holds the clipboard open.</summary>
    public static bool TryCopy(string text)
    {
        try
        {
            DataPackage package = new() { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);

            // Keep the text available after the flyout closes or the app exits.
            Clipboard.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
