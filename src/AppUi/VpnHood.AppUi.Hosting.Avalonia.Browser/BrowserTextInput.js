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
    // a key or a code is taken as typed: no capitals, corrections or suggestions of a phone's keyboard
    input.autocomplete = 'off';
    input.spellcheck = false;
    input.setAttribute('autocapitalize', 'off');
    input.setAttribute('autocorrect', 'off');
    input.enterKeyHint = 'go';
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

export function focus(input) {
    if (document.activeElement !== input)
        input.focus({ preventScroll: true });
}

// The keys back to Avalonia, whose handlers are on the page's container: once Avalonia's focus has
// moved on, the input must not keep the browser's.
export function release(input) {
    if (document.activeElement === input)
        input.closest('.avalonia-container')?.focus({ preventScroll: true });
}
