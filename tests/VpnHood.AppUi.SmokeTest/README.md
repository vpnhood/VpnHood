# UI smoke test

A vibe check on the Avalonia UI. It opens every page a fresh install can reach and fails when one
does not open, opens the wrong thing, or brings an error dialog with it. It is a smoke test, not a
unit test: it launches a real window and drives it the way a person would.

## Running it

It is kept out of the normal run by its category, so nothing you already run changes.

```
dotnet test tests/VpnHood.AppUi.SmokeTest --filter TestCategory=Ui
```

It takes about 40 seconds. It needs an interactive desktop to draw on; without one every case
reports inconclusive rather than failing. It does not need elevation and never connects, so no
WinDivert driver and no access key are involved.

Pictures of every page land in `bin/<config>/net10.0-windows/ui-walk/`, one per control, plus the
terms page and the home. They are artifacts for you to look at, never assertions: nothing here
compares pixels, so a theme or an asset change cannot fail the run.

## What it drives, and why that head

It runs `VpnHoodAvaloniaDev`, the developer tool under `src/Apps/Tools/AvaloniaUI.Dev`, with
`--connect` for the Connect product's look and `--storage VpnHood.UiSmokeTest` for a folder of its
own. Your installed client is never read, changed or deleted. The folder is removed before and
after each run, so every run is a first run and the terms page is part of what gets checked.

The project references that head only so building the test builds it too; no type of it is used.
Where its build put it is written into this assembly at build time, so the test searches for
nothing and a Release run never drives a Debug window by accident.

## What it asserts

For each page: the title appears, the title is the expected one, and no error dialog is on screen.
The title must **change** from the page the click started on. That last rule is the one that
catches a page which re-opens itself instead of moving on, which is a real defect this UI has had.

At the end it reads the head's own log and fails if it holds `The UI caught an error`, which is
what `MainView.ProcessError` writes whenever a page hands it an exception it did not expect.

## What it does not cover, and why

- **The location page and every connected screen.** A fresh install has no server profile, so the
  location row raises a notice instead of opening a page. Covering these means giving the head a
  profile, which it has no switch for yet.
- **The premium and account screens.** This head configures no premium tier and no account
  provider, so those pages and the paywall do not exist in it.
- **What's New, Send Feedback, the site and the privacy policy.** They open a browser, not a page.
- **The developer page.** Its button has no name of its own and wants five taps in a burst.

## Two things that will trip you up

- **The page titles below the data rows are English.** A machine whose UI language is not English
  fails on the title, not on the app.
- **A collapsed Avalonia control still says it is on screen.** The driver treats size as the test
  of what a person can touch; without it, the closed drawer's items and the statistics row that a
  disconnected home does not draw are all "found", and invoking them throws.
