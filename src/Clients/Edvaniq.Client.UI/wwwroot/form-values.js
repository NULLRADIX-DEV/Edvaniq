// What the given fields hold right now. A password manager or autofill can fill a field without any event, so the
// binding never hears of it. Reads only the ids asked for, never logs or keeps a value (one of them is a password).
export function read(ids) {
    return ids.map(id => document.getElementById(id)?.value ?? null);
}
