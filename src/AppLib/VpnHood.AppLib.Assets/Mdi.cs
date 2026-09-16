namespace VpnHood.AppLib.Assets;

// Material Design Icons, the web UI's icon set, drawn by code point from the icon font in the
// assets folder: the same glyph the web UI shows for the same name, and nothing to convert when a
// page is ported. The code points are the ones @mdi/font's stylesheet names
// (materialdesignicons.css), read from the web UI's node_modules - a guess at one draws the wrong
// glyph without a word of warning.
//
// That font is a subset: the SPA build cuts it to the icons its own pages name plus the ones
// listed in its build/native-ui-icons.txt, which _sync-native-assets.ps1 (in the Avalonia UI
// project) writes from THIS file. Add an icon here, run that script, and rebuild the SPA - or the
// glyph is a blank box.
public static class Mdi
{
    public const string FontFamily = AppFonts.IconFamily;

    public const string Account = "\U000F0004";
    public const string AccountCircle = "\U000F0009";
    public const string AlertCircle = "\U000F0028";
    public const string AlertCircleOutline = "\U000F05D6";
    public const string ArrowDownThin = "\U000F19B3";
    public const string ArrowUpThin = "\U000F19B2";
    public const string BookOpenPageVariantOutline = "\U000F15D6";
    public const string BugOutline = "\U000F0A30";
    public const string Bullhorn = "\U000F00E6";
    public const string CallSplit = "\U000F00FB";
    public const string Cancel = "\U000F073A";
    public const string Cellphone = "\U000F011C";
    public const string CellphoneArrowDown = "\U000F09D5";
    public const string CellphoneLink = "\U000F0121";
    public const string ChartTimelineVariant = "\U000F0E93";
    public const string Check = "\U000F012C";
    public const string CheckCircle = "\U000F05E0";
    public const string CheckCircleOutline = "\U000F05E1";
    public const string CheckboxBlankOutline = "\U000F0131";
    public const string CheckboxMarked = "\U000F0132";
    public const string ChevronDown = "\U000F0140";
    public const string ChevronLeft = "\U000F0141";
    public const string ChevronRight = "\U000F0142";
    public const string ChevronUp = "\U000F0143";
    public const string CircleOutline = "\U000F0766";
    public const string Close = "\U000F0156";
    public const string CloseCircle = "\U000F0159";
    public const string Cog = "\U000F0493";
    public const string ContentCopy = "\U000F018F";
    public const string Crown = "\U000F01A5";
    public const string CrownCircleOutline = "\U000F17DD";
    public const string Delete = "\U000F01B4";
    public const string DeleteAlert = "\U000F10A5";
    public const string DeleteForever = "\U000F05E8";
    public const string Diversify = "\U000F1877";
    public const string Dns = "\U000F01D6";
    public const string DotsVertical = "\U000F01D9";
    public const string Earth = "\U000F01E7";
    public const string EmailOutline = "\U000F01F0";
    public const string EmoticonSadOutline = "\U000F01F8";
    public const string Eye = "\U000F0208";
    public const string EyeOff = "\U000F0209";
    public const string EyeOffOutline = "\U000F06D1";
    public const string EyeOutline = "\U000F06D0";
    public const string FilterVariant = "\U000F0236";
    public const string Information = "\U000F02FC";
    public const string InformationOutline = "\U000F02FD";
    public const string Instagram = "\U000F02FE";
    public const string IpNetwork = "\U000F0A60";
    public const string IpOutline = "\U000F1982";
    public const string Key = "\U000F0306";
    public const string LightningBoltOutline = "\U000F140C";
    public const string Linkedin = "\U000F033B";
    public const string Loading = "\U000F0772";
    public const string Magnify = "\U000F0349";
    public const string Menu = "\U000F035C";
    public const string MessageAlert = "\U000F0362";
    public const string MinusCircleOutline = "\U000F0377";
    public const string OpenInNew = "\U000F03CC";
    public const string PartyPopper = "\U000F1056";
    public const string Pencil = "\U000F03EB";
    public const string PlayBoxLockOpenOutline = "\U000F1A18";
    public const string PlaylistPlus = "\U000F0412";
    public const string Plus = "\U000F0415";
    public const string PlusCircle = "\U000F0417";
    public const string PlusCircleOutline = "\U000F0419";
    public const string PowerPlugOff = "\U000F06A6";
    public const string RadioboxBlank = "\U000F043D";
    public const string RadioboxMarked = "\U000F043E";
    public const string Refresh = "\U000F0450";
    public const string RefreshAuto = "\U000F18F2";
    public const string SatelliteUplink = "\U000F0909";
    public const string SelectAll = "\U000F0486";
    public const string SelectRemove = "\U000F17C1";
    public const string SendOutline = "\U000F1165";
    public const string Server = "\U000F048B";
    public const string ServerOff = "\U000F048F";
    public const string ServerOutline = "\U000F1C9A";
    public const string ShieldAccount = "\U000F088F";
    public const string Speedometer = "\U000F04C5";
    public const string Stethoscope = "\U000F04D9";
    public const string SwordCross = "\U000F0787";
    public const string Television = "\U000F0502";
    public const string TelevisionPlay = "\U000F0ECF";
    public const string TimerLockOpenOutline = "\U000F1AD6";
    public const string TimerSand = "\U000F051F";
    public const string TransitConnectionVariant = "\U000F0D3D";
    public const string Update = "\U000F06B0";
    public const string Web = "\U000F059F";
}
