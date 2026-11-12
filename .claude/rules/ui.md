---
paths:
  - "src/AppUi/**"
---

## UI

- The products' UI is the Avalonia presentation under `src/AppUi`. The look it draws with - images, flags, fonts, words - is the submodule at `src/AppUi/VpnHood.AppUi.Assets.Classic` (vpnhood/VpnHood.AppUi.Assets.Classic); a clone needs `--recurse-submodules` or a head starts with no images and no words. All of it is written and edited in the submodule itself, the words by hand in all 12 languages before the commit (its README, "The words").
- A button takes one of four looks: `btn-main` (the one thing a screen is for), `btn-secondary` (any other action), `btn-text` (the way out), `btn-danger` (what cannot be undone). Their shape is under "Button looks" in `Styles/AppTheme.axaml`, and their colours are the `Btn` keys of the two themes, so a page names no button colour and adds no button style. A yes to a risky question stays `btn-text`: Main says go ahead. A surface whose colours would hide a look restyles the look itself, scoped to that surface in `AppTheme.axaml`, as the light error card does with its `DialogAlertBtn` keys; the button keeps its look's name. Rows, icon buttons, links, chips and Home's own buttons (Connect, Go Premium, the premium and countdown pills) are not looks.
