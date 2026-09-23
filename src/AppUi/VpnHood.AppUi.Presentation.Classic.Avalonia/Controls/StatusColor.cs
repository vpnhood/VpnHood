namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// The colour of a settings row's on-state chip: the healthy green as a rule, the switch colour
// for a choice rather than a state, and the warning orange where the on-state weakens protection
// (SettingsItem.vue's status.onColor).
public enum StatusColor
{
    EnablePremium,
    Switch,
    Warning
}
