namespace VpnHood.App.StoreScreenshots;

// What a device's captures are dressed in before they go to its store.
internal enum FrameKind
{
    // the bare capture at store size - what Google Play shows, and the only honest treatment for a
    // television, whose screenshot IS the whole panel
    None,

    // a phone or tablet mockup drawn around the capture: bezel, status bar, home indicator
    Phone,

    // the app's window on a backdrop: the Microsoft Store's floor is far above the window's own size
    Desktop
}
