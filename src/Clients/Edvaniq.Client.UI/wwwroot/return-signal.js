// Tells .NET once per return: the tab is visible again or the window got the focus back. Switching tabs fires both
// events, so only the step from away to back counts.
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
}

export function stop() {
    document.removeEventListener('visibilitychange', onVisibilityChange);
    window.removeEventListener('blur', leave);
    window.removeEventListener('focus', back);
    target = null;
}
