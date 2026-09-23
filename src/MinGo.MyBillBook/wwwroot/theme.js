// ============================================================
// Theme manager for MinGo.MyBillBook
// Modes: 'auto' (follow system) | 'light' | 'dark'
// Persisted in localStorage under key 'mbb-theme'.
// Applied by toggling .light / .dark class on <html>.
// ============================================================
(function (global) {
    'use strict';

    var STORAGE_KEY = 'mbb-theme';
    var VALID_MODES = ['auto', 'light', 'dark'];

    function readMode() {
        try {
            var v = global.localStorage.getItem(STORAGE_KEY);
            return VALID_MODES.indexOf(v) >= 0 ? v : 'auto';
        } catch (e) {
            return 'auto';
        }
    }

    function applyMode(mode) {
        var root = global.document.documentElement;
        root.classList.remove('light', 'dark');
        if (mode === 'light') root.classList.add('light');
        else if (mode === 'dark') root.classList.add('dark');
        // 'auto': no class — CSS @media (prefers-color-scheme: dark) takes over
    }

    function setMode(mode) {
        if (VALID_MODES.indexOf(mode) < 0) mode = 'auto';
        try { global.localStorage.setItem(STORAGE_KEY, mode); } catch (e) { }
        applyMode(mode);
        return mode;
    }

    function cycleMode() {
        var current = readMode();
        var idx = VALID_MODES.indexOf(current);
        var next = VALID_MODES[(idx + 1) % VALID_MODES.length];
        return setMode(next);
    }

    function systemPrefersDark() {
        return global.matchMedia && global.matchMedia('(prefers-color-scheme: dark)').matches;
    }

    function effectiveIsDark() {
        var m = readMode();
        if (m === 'dark') return true;
        if (m === 'light') return false;
        return systemPrefersDark();
    }

    global.mbbTheme = {
        get: readMode,
        set: setMode,
        cycle: cycleMode,
        isDark: effectiveIsDark,
        apply: applyMode
    };

    // Reapply on system change if in auto mode
    if (global.matchMedia) {
        var mq = global.matchMedia('(prefers-color-scheme: dark)');
        var handler = function () {
            if (readMode() === 'auto') applyMode('auto');
        };
        if (mq.addEventListener) mq.addEventListener('change', handler);
        else if (mq.addListener) mq.addListener(handler);
    }
})(typeof window !== 'undefined' ? window : this);
