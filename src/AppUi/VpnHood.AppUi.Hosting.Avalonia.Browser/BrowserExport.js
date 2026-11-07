// How the page hands the person something (BrowserExport.cs). A remote page is plain http, where a
// browser gives it no clipboard and no share, but these two still work.

// A text file into the browser's downloads.
export function download(fileName, text) {
    const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();

    // the browser reads the blob after the click has returned
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}

// The text on the clipboard, by the old command, which a browser allows only from the person's tap:
// nothing may wait between the tap and this.
export function copy(text) {
    const box = document.createElement('textarea');
    box.value = text;
    box.readOnly = true; // no phone keyboard for a box that is gone at once
    Object.assign(box.style, { position: 'fixed', top: '0', left: '0', opacity: '0' });
    document.body.appendChild(box);
    box.select();
    box.setSelectionRange(0, text.length);
    const isCopied = document.execCommand('copy');
    box.remove();
    return isCopied;
}
