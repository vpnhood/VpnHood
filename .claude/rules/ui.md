---
paths:
  - "src/AppUi/**"
---

## UI

- The products' UI is the Avalonia presentation under `src/AppUi`. The look it draws with - images, flags, fonts, words - is the submodule at `src/AppUi/VpnHood.AppUi.Assets.Classic` (vpnhood/VpnHood.AppUi.Assets.Classic); a clone needs `--recurse-submodules` or a head starts with no images and no words. The web UI is kept as a sample in the sibling repo `..\VpnHood.AppUi.Spa\` (its SPA at `src\VpnHood.AppUi.Presentation.Classic.Spa`), where those files were authored and from which `_sync-assets.ps1` still mirrors a change across.
