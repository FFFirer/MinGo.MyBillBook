// Inbox 通知中心：浏览器通知权限、桌面通知与未读徽标。
window.mbbNotifications = {
    permission: function () {
        return ('Notification' in window) ? Notification.permission : 'unsupported';
    },

    request: async function () {
        if (!('Notification' in window)) return 'unsupported';
        try {
            return await Notification.requestPermission();
        } catch (e) {
            return 'unsupported';
        }
    },

    show: function (title, body, url) {
        try {
            if (!('Notification' in window) || Notification.permission !== 'granted') return;
            // 页面可见时不弹桌面通知，避免与徽标/面板重复。
            if (document.visibilityState === 'visible') return;

            var n = new Notification(title, {
                body: body,
                icon: '/icon-192.png',
                tag: 'mbb-inbox-' + Date.now()
            });
            n.onclick = function () {
                window.focus();
                if (url) window.location.href = url;
                n.close();
            };
        } catch (e) { }
    },

    setBadge: function (count) {
        try {
            var base = document.title.replace(/^\(\d+\+?\)\s*/, '');
            document.title = count > 0 ? '(' + count + ') ' + base : base;
            if (navigator.setAppBadge) {
                if (count > 0) navigator.setAppBadge(count).catch(function () { });
                else if (navigator.clearAppBadge) navigator.clearAppBadge().catch(function () { });
            }
        } catch (e) { }
    }
};
