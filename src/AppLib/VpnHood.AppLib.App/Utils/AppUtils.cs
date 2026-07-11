namespace VpnHood.AppLib.Utils;

public static class AppUtils
{
    // Cheap change signature over files (modified time + length; no content read) — one label:ticks:length
    // entry per file, a missing file counts as "none". Callers store it (e.g. as a split db's
    // source_signature meta) to detect stale derived data without parsing the sources.
    // Kept human-readable on purpose: the stored signature then shows exactly which file it was built
    // from, so a did-it-rebuild question is answered by inspection.
    public static string BuildFileSignature(params string[] filePaths)
    {
        return string.Join(',', filePaths.Select(filePath => {
            var fileInfo = new FileInfo(filePath);
            var fileTitle = Path.GetFileNameWithoutExtension(filePath);
            return fileInfo.Exists
                ? $"{fileTitle}:{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}"
                : $"{fileTitle}:none";
        }));
    }
}
