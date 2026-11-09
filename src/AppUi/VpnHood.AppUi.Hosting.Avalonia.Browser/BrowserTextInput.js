// The page's own text input, in an Avalonia text box's frame (BrowserTextInput.cs).

// Its events stay its own: Avalonia's handlers on the page's container would take the keys and the
// pointer and cancel them, and refuse the browser's menu, which is the paste this input is for.
const ownEvents = ['keydown', 'keyup', 'keypress', 'beforeinput', 'input', 'compositionstart', 'compositionupdate',
    'compositionend', 'paste', 'copy', 'cut', 'contextmenu', 'pointerdown', 'pointermove', 'pointerup', 'pointercancel',
    'dragenter', 'dragover', 'dragleave', 'drop'];

// the colours only a rule can give: the placeholder's and the selection's
let ruled = false;

export function create(multiline, acceptsReturn, onInput, onKey, onFocus) {
    if (!ruled) {
        const rule = document.createElement('style');
        rule.textContent = '.vh-text-input::placeholder { color: var(--vh-placeholder); opacity: 1; } ' +
            '.vh-text-input::selection { background: var(--vh-selection); }';
        document.head.appendChild(rule);
        ruled = true;
    }

    // a box that wraps gets a textarea, which wraps a long key as the box does
    const input = document.createElement(multiline ? 'textarea' : 'input');
    if (!multiline)
        input.type = 'text';
    input.className = 'vh-text-input';
    Object.assign(input.style, {
        boxSizing: 'border-box', margin: '0', padding: '0', border: '0', outline: 'none', background: 'transparent',
        userSelect: 'text', webkitUserSelect: 'text', touchAction: 'manipulation', resize: 'none',
        overflowWrap: 'anywhere', scrollbarWidth: 'thin'
    });

    for (const type of ownEvents)
        input.addEventListener(type, e => e.stopPropagation());
    input.addEventListener('input', () => {
        // a box that takes no return takes a key's lines as one, as an <input> does by itself: a key
        // pasted out of a mail often comes broken into lines
        if (!acceptsReturn && /[\r\n]/.test(input.value))
            input.value = input.value.replace(/[\r\n]+/g, '');
        onInput(input.value);
    });
    input.addEventListener('focus', () => onFocus());

    // the keys that act on the page are Avalonia's: Enter submits, Tab moves on, Escape goes back
    input.addEventListener('keydown', e => {
        if (e.isComposing || (e.key !== 'Enter' && e.key !== 'Tab' && e.key !== 'Escape') ||
            (e.key === 'Enter' && acceptsReturn))
            return;
        e.preventDefault();
        onKey(e.key, e.shiftKey);
    });
    return input;
}

export function setText(input, text) {
    if (input.value !== text)
        input.value = text;
}

export function setLook(input, font, color, caret, placeholder, selection, align, direction) {
    input.style.font = font;
    input.style.color = color;
    input.style.caretColor = caret;
    input.style.textAlign = align;
    input.style.setProperty('--vh-placeholder', placeholder);
    input.style.scrollbarColor = placeholder ? `${placeholder} transparent` : '';
    input.style.setProperty('--vh-selection', selection);
    input.dir = direction;
}

export function setWords(input, placeholder, label) {
    input.placeholder = placeholder;
    if (label)
        input.setAttribute('aria-label', label);
    else
        input.removeAttribute('aria-label');
}

// What the box takes (BrowserTextInput.SetOptions): the phone's keyboard for it, what the phone may fill
// it with, and whether its keyboard capitalizes, corrects and suggests.
export function setOptions(input, isPassword, inputMode, autoComplete, suggestions, capitalize, enterKeyHint, maxLength) {
    if (isPassword && input instanceof HTMLInputElement)
        input.type = 'password';
    input.inputMode = inputMode;
    input.autocomplete = autoComplete;
    input.spellcheck = suggestions;
    input.setAttribute('autocorrect', suggestions ? 'on' : 'off');
    input.setAttribute('autocapitalize', capitalize ? 'sentences' : 'off');
    input.enterKeyHint = enterKeyHint;
    if (maxLength > 0)
        input.maxLength = maxLength;
}

// The part of the box the app shows (BrowserTextInput.UpdateClip), in the input's own pixels from each edge: the
// rest is cut, for the eye and for a tap alike.
export function setClip(input, top, right, bottom, left) {
    input.style.clipPath = top > 0 || right > 0 || bottom > 0 || left > 0
        ? `inset(${top}px ${right}px ${bottom}px ${left}px)` : '';
}

// Only when it moves here: a tap in the input has placed its own caret by then.
export function focus(input, selectAll) {
    if (document.activeElement === input)
        return;
    input.focus({ preventScroll: true });
    if (selectAll)
        input.select();
}

// The keys back to Avalonia, whose handlers are on the page's container: once Avalonia's focus has
// moved on, the input must not keep the browser's.
export function release(input) {
    if (document.activeElement === input)
        input.closest('.avalonia-container')?.focus({ preventScroll: true });
}
