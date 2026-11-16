namespace VpnHood.App.Client;

// The forms an access key comes to the product in from outside the app: a link's schemes, and a file's
// MIME types, as Android names them (https://android.googlesource.com/platform/libcore/+/android-5.0.2_r1/luni/src/main/java/libcore/net/MimeUtils.java).
// CLIENT's own registration, never the shared libraries': an app that takes no access keys, as CONNECT,
// registers none, and a fork chooses its own. Constants, since Android's intent filters take nothing else.
public static class ClientAccessKeys
{
    public const string Scheme1 = "vh";

    // a form keys may switch to, so an install from before the switch opens its links
    public const string Scheme2 = "vhkey";

    public const string Mime1 = "application/vhkey";
    public const string Mime2 = "application/pgp-keys"; // .key
    public const string Mime3 = "application/vnd.cinderella"; // .cdy
}
