# Web accessibility audit checklist

Use this checklist to review a web interface against **WCAG 2.2 Level AA**. Audit representative pages and states, including responsive layouts, dialogs, menus, validation errors, and signed-in or loading states. Mark an item complete only after checking it in the running interface; automated scans alone do not establish conformance.

## Keyboard navigation

- [ ] Complete every task using only the keyboard: Tab, Shift+Tab, Enter, Space, arrow keys, and Escape where appropriate. (2.1.1)
- [ ] Tab order follows the visual and reading order; focus is never trapped, and leaving a component does not require an unusual key sequence. (2.1.2)
- [ ] Every interactive control has a visible focus indicator. Confirm it remains visible and is not hidden behind sticky headers, footers, or overlays. (2.4.7, 2.4.11)
- [ ] Opening a dialog or menu moves focus into it; closing it returns focus to the opener. Escape closes dismissible overlays, and background content cannot receive focus while a modal is open.
- [ ] Focus order and focus handling are predictable when content is inserted, removed, or updated.
- [ ] Functionality does not require dragging; provide a single-pointer alternative such as buttons or a click/tap interaction. (2.5.7)
- [ ] Pointer targets are at least 24 by 24 CSS pixels, or meet a WCAG exception such as sufficient spacing or an inline-text control. (2.5.8)
- [ ] At 200% zoom and at a 320 CSS-pixel viewport width, content remains usable without loss of information or two-dimensional scrolling, except where the content inherently requires it. (1.4.4, 1.4.10)
- [ ] Content shown on hover or keyboard focus can be dismissed, hovered, and kept visible until dismissed or no longer relevant. (1.4.13)

## Screen readers and semantics

- [ ] Check each page with a screen reader: page title, language, headings, landmarks, lists, tables, and controls are announced with meaningful semantics.
- [ ] Every control has an accessible name, role, and state; its name describes its purpose and agrees with any visible label. (4.1.2)
- [ ] Headings are descriptive and hierarchically organized; landmarks and labels make it easy to navigate by page region and form.
- [ ] Links make sense in context and distinguishably describe their destination or action.
- [ ] Images have appropriate text alternatives: informative images are described, decorative images are ignored, and complex images have an equivalent explanation.
- [ ] Custom widgets expose their current value and state and support the expected keyboard interaction pattern.
- [ ] Changes such as success messages, validation results, loading completion, and other status updates are announced without unexpectedly moving focus. (4.1.3)
- [ ] Read through important flows with speech output enabled; confirm instructions, errors, dialog changes, and results are announced in a sensible order.

## Color, contrast, and visual presentation

- [ ] Text contrast is at least **4.5:1** for normal text and **3:1** for large text (at least 18 pt, or 14 pt bold). Check every state, including placeholders and text over images; inactive controls are exempt from this criterion. (1.4.3)
- [ ] Essential visual information in controls, focus indicators, and meaningful graphics has at least **3:1** contrast against adjacent colors. (1.4.11)
- [ ] Color is not the only way to convey meaning, identify an error, or distinguish an item; add text, icons, patterns, or another cue. (1.4.1)
- [ ] At 200% text resizing and with user text-spacing overrides (line height 1.5×, paragraph spacing 2×, letter spacing 0.12× font size, and word spacing 0.16× font size), content is not clipped or obscured. (1.4.4, 1.4.12)
- [ ] At narrow viewport widths and high zoom, content reflows and controls remain available; test both portrait and landscape where relevant. (1.4.10)
- [ ] Focused, selected, error, and hover states remain distinguishable in forced-colors/high-contrast mode and do not rely on color alone.

## Forms and authentication

- [ ] Every input has a programmatically associated, persistent label; placeholder text is not the only label. (3.3.2)
- [ ] Required fields, accepted formats, and constraints are explained before input when possible; instructions are available to assistive technology.
- [ ] Invalid values are identified in text, the affected field is identified, and the correction is explained when known. (3.3.1, 3.3.3)
- [ ] On submission errors, provide a useful error summary and associate each field error with its input; move focus only when doing so helps users recover.
- [ ] Errors and success messages are announced to screen readers, and corrected fields do not retain stale error state.
- [ ] Important submissions can be reviewed, corrected, or confirmed before finalizing when the action has legal, financial, or data consequences. (3.3.4)
- [ ] Previously entered information is not requested again in the same process unless essential, security-related, or no longer valid. (3.3.7)
- [ ] Authentication does not require a cognitive function test such as solving a puzzle or transcribing a one-time code without an accessible alternative; support password managers and paste. (3.3.8)

## Audio and video

- [ ] Prerecorded video with audio has synchronized captions for speech and meaningful sounds. (1.2.2)
- [ ] Live synchronized media has captions. (1.2.4)
- [ ] Prerecorded video has audio description when visual information needed to understand it is not already conveyed by the soundtrack. (1.2.5)
- [ ] Prerecorded audio-only content has an equivalent text alternative; a transcript is one way to provide it. (1.2.1)
- [ ] Media players and all playback controls work by keyboard, expose accessible names and states, and can be operated with a screen reader.
- [ ] Audio does not play automatically for more than 3 seconds without a way to pause/stop it or control its volume independently. (1.4.2)
- [ ] Automatically moving, blinking, or scrolling content that starts automatically, lasts more than 5 seconds, and appears alongside other content can be paused, stopped, or hidden. (2.2.2)

## Testing tools and workflow

**Automated checks** can find many detectable issues, but cannot confirm keyboard usability, understandable labels, meaningful alternatives, or overall WCAG conformance.

- **axe DevTools** browser extension or **axe-core** in browser tests — run on key pages and interactive states.
- **Lighthouse Accessibility** in Chrome DevTools — use as a quick scan, not a conformance score.
- **WAVE** browser extension — inspect page structure, labels, and likely contrast issues.
- **Contrast checker** such as TPGi Colour Contrast Analyser — sample foreground/background pairs, including component states.

**Manual checks**

- Keyboard-only walkthrough in each supported browser; unplug or avoid the mouse.
- **NVDA** with Firefox or Chrome on Windows; **VoiceOver** with Safari on macOS/iOS; **TalkBack** with Chrome on Android for mobile interfaces.
- Browser zoom, responsive/emulated viewport, text-spacing overrides, and forced-colors/high-contrast mode.
- Review captions, transcripts, audio descriptions, labels, validation, focus movement, and announcements with the actual content and workflows.

Record the page, state, steps to reproduce, expected behavior, observed result, and relevant WCAG success criterion for each finding. Re-test fixes with both the original method and a keyboard/screen-reader check where applicable.
