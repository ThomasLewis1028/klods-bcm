// Copies text to the clipboard; returns false when the browser denies access or the page is not a secure context.
window.klodsCopyText = async (text) => {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
};
