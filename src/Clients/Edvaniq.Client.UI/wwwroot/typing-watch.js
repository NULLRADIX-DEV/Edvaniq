// Tells .NET when a field turns from empty to filled or back. Its text stays in the page, sending a long text across on
// every key would make typing lag. Listens on the document, so a field that is rendered anew is still watched.
export function watch(id, dotNet) {
    let filled = false;
    const onInput = event => {
        if (event.target?.id !== id) {
            return;
        }

        const now = event.target.value.trim() !== '';
        if (now !== filled) {
            filled = now;
            dotNet.invokeMethodAsync('Filled', now);
        }
    };

    // .NET may have emptied the field meanwhile, after sending its text, so each visit starts anew.
    const onFocus = event => {
        if (event.target?.id === id) {
            filled = false;
        }
    };

    document.addEventListener('input', onInput, true);
    document.addEventListener('focusin', onFocus, true);
    return {
        stop: () => {
            document.removeEventListener('input', onInput, true);
            document.removeEventListener('focusin', onFocus, true);
        },
    };
}
