using Android.Content;
using Android.Database;
using Android.OS;
using AndroidUri = Android.Net.Uri;
using JavaFile = Java.IO.File;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

// A minimal, dependency-free FileProvider for exporting the on-device report/log via the share sheet.
// Sharing a file:// URI throws FileUriExposedException on modern Android, and putting the whole log in
// Intent.ExtraText makes text-first apps (e.g. Telegram) paste the entire huge log and can exceed the Binder
// transaction limit. So we hand out a content:// URI backed by a copy in the app cache instead, and grant the
// receiving app a temporary read permission. We roll our own instead of AndroidX FileProvider to keep this
// library free of AndroidX (the app variants are sensitive to AndroidX version conflicts).
//
// The authority is "<applicationId>.reportprovider" so multiple VpnHood apps can coexist on one device.
[ContentProvider(["${applicationId}.reportprovider"], Exported = false, GrantUriPermissions = true)]
public sealed class ReportFileProvider : ContentProvider
{
    private const string AuthoritySuffix = ".reportprovider";
    private const string ReportsDirName = "reports";

    // Column names from Android.Provider.OpenableColumns; inlined to avoid the binding constant lookup.
    private const string ColumnDisplayName = "_display_name";
    private const string ColumnSize = "_size";

    public override bool OnCreate() => true;

    // Copy the report into the cache and return a content:// URI the share sheet can grant to other apps.
    public static AndroidUri SaveAndGetUri(Context context, string fileName, byte[] content)
    {
        var dir = GetReportsDir(context);
        var file = new JavaFile(dir, fileName);
        File.WriteAllBytes(file.AbsolutePath, content);
        var ret = new AndroidUri.Builder()
            .Scheme("content")?
            .Authority(context.PackageName + AuthoritySuffix)?
            .Path(fileName)?
            .Build();

        return ret ?? throw new Exception("Failed to build content URI");
    }

    public override ParcelFileDescriptor? OpenFile(AndroidUri uri, string mode)
    {
        var file = ResolveFile(uri) ??
            throw new Java.IO.FileNotFoundException(uri.ToString());
        return ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
    }

    // Consumers (mail/messengers) query the display name and size before attaching; supply just those.
    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection,
        string[]? selectionArgs, string? sortOrder)
    {
        var file = ResolveFile(uri);
        if (file == null)
            return null;

        var requested = projection ?? [ColumnDisplayName, ColumnSize];
        var names = new List<string>();
        var values = new List<Java.Lang.Object>();
        foreach (var column in requested) {
            if (string.Equals(column, ColumnDisplayName, StringComparison.OrdinalIgnoreCase)) {
                names.Add(ColumnDisplayName);
                values.Add(new Java.Lang.String(file.Name));
            }
            else if (string.Equals(column, ColumnSize, StringComparison.OrdinalIgnoreCase)) {
                names.Add(ColumnSize);
                values.Add(Java.Lang.Long.ValueOf(file.Length()));
            }
        }

        var cursor = new MatrixCursor(names.ToArray(), 1);
        cursor.AddRow(values.ToArray());
        return cursor;
    }

    public override string GetType(AndroidUri uri) => "text/plain";

    // Read-only provider: mutations are unsupported.
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(AndroidUri uri, ContentValues? values, string? selection,
        string[]? selectionArgs) => 0;

    // Map the URI's file name back onto the cache directory, refusing anything that escapes it.
    private JavaFile? ResolveFile(AndroidUri uri)
    {
        var name = uri.LastPathSegment;
        if (string.IsNullOrEmpty(name) || Context == null)
            return null;

        var dir = GetReportsDir(Context);
        var file = new JavaFile(dir, name);
        if (!file.CanonicalPath.StartsWith(dir.CanonicalPath + JavaFile.Separator, StringComparison.Ordinal))
            return null;

        return file.Exists() ? file : null;
    }

    private static JavaFile GetReportsDir(Context context)
    {
        var dir = new JavaFile(context.CacheDir, ReportsDirName);
        dir.Mkdirs();
        return dir;
    }
}
