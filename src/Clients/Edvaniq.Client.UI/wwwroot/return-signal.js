// Tells .NET once per return: the tab is visible again or the window got the focus back. Switching tabs fires both
// events, so only the step from away to back counts. The connection coming back counts too: while offline the page may
// have missed the newest state, or shows that it couldn't load it.
let target = null;
let away = false;

function leave() {
    away = true;
}

function back() {
    if (!away || document.visibilityState !== 'visible') {
        return;
    }

    away = false;
    target?.invokeMethodAsync('Returned');
}

// A hidden tab gets its turn when it is shown again.
function onOnline() {
    if (document.visibilityState === 'visible') {
        target?.invokeMethodAsync('Returned');
    }
}

function onVisibilityChange() {
    if (document.visibilityState === 'hidden') {
        leave();
    } else {
        back();
    }
}

export function start(dotNet) {
    target = dotNet;
    away = false;
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('blur', leave);
    window.addEventListener('focus', back);
    window.addEventListener('online', onOnline);
}

export function stop() {
    document.removeEventListener('visibilitychange', onVisibilityChange);
    window.removeEventListener('blur', leave);
    window.removeEventListener('focus', back);
    window.removeEventListener('online', onOnline);
    target = null;
}
