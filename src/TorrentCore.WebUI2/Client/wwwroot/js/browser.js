window.tcBrowser = {
    overlayCleanups: new WeakMap(),
    menuCleanups: new WeakMap(),
    store(key, value) {
        window.localStorage.setItem(key, JSON.stringify(value));
    },
    load(key) {
        const value = window.localStorage.getItem(key);
        if (value === null) return null;
        try { return JSON.parse(value); }
        catch { throw new Error(`Saved table state '${key}' is invalid.`); }
    },
    remove(key) {
        window.localStorage.removeItem(key);
    },
    attachOverlayResize(panel, handle, dotnet) {
        window.tcBrowser.detachOverlayResize(panel);
        const pointerDown = event => {
            if (event.button !== 0) return;
            event.preventDefault();
            handle.setPointerCapture(event.pointerId);
            const startX = event.clientX;
            const startWidth = panel.getBoundingClientRect().width;
            const pointerMove = move => {
                const width = Math.max(320, Math.min(900, startWidth + startX - move.clientX));
                panel.style.width = `${width}px`;
            };
            const pointerUp = up => {
                handle.releasePointerCapture(up.pointerId);
                handle.removeEventListener("pointermove", pointerMove);
                handle.removeEventListener("pointerup", pointerUp);
                handle.removeEventListener("pointercancel", pointerUp);
                dotnet.invokeMethodAsync("SetOverlayWidthAsync", panel.getBoundingClientRect().width);
            };
            handle.addEventListener("pointermove", pointerMove);
            handle.addEventListener("pointerup", pointerUp);
            handle.addEventListener("pointercancel", pointerUp);
        };
        handle.addEventListener("pointerdown", pointerDown);
        window.tcBrowser.overlayCleanups.set(panel, () => handle.removeEventListener("pointerdown", pointerDown));
    },
    detachOverlayResize(panel) {
        if (!panel) return;
        const cleanup = window.tcBrowser.overlayCleanups.get(panel);
        if (cleanup) cleanup();
        window.tcBrowser.overlayCleanups.delete(panel);
    },
    attachMenuDismiss(container) {
        window.tcBrowser.detachMenuDismiss(container);
        const items = () => Array.from(container.querySelectorAll(":scope > details"));
        const clearPanelPosition = item => {
            const panel = item?.querySelector(":scope > .tool-panel");
            if (!panel) return;
            panel.style.removeProperty("bottom");
            panel.style.removeProperty("left");
            panel.style.removeProperty("max-height");
            panel.style.removeProperty("max-width");
            panel.style.removeProperty("min-width");
            panel.style.removeProperty("position");
            panel.style.removeProperty("right");
            panel.style.removeProperty("top");
            panel.style.removeProperty("width");
        };
        const positionPanel = item => {
            if (!item?.open) return;
            const summary = item.querySelector(":scope > summary");
            const panel = item.querySelector(":scope > .tool-panel");
            const page = container.closest(".shows-evaluation");
            if (!summary || !panel || !page) return;

            clearPanelPosition(item);
            panel.style.position = "fixed";
            panel.style.visibility = "hidden";

            const viewport = window.visualViewport;
            const viewportLeft = viewport?.offsetLeft ?? 0;
            const viewportTop = viewport?.offsetTop ?? 0;
            const viewportRight = viewportLeft + (viewport?.width ?? window.innerWidth);
            const viewportBottom = viewportTop + (viewport?.height ?? window.innerHeight);
            const pageBounds = page.getBoundingClientRect();
            const margin = 8;
            const leftBound = Math.max(viewportLeft, pageBounds.left) + margin;
            const topBound = Math.max(viewportTop, pageBounds.top) + margin;
            const rightBound = Math.min(viewportRight, pageBounds.right) - margin;
            const bottomBound = Math.min(viewportBottom, pageBounds.bottom) - margin;
            const availableWidth = Math.max(0, rightBound - leftBound);
            const availableHeight = Math.max(0, bottomBound - topBound);
            const anchor = summary.getBoundingClientRect();
            const desiredWidth = panel.scrollWidth;
            const desiredHeight = panel.scrollHeight;
            const width = Math.min(desiredWidth, availableWidth);
            const spaceBelow = Math.max(0, bottomBound - anchor.bottom - 4);
            const spaceAbove = Math.max(0, anchor.top - topBound - 4);
            const openBelow = desiredHeight <= spaceBelow || spaceBelow >= spaceAbove;
            const maxHeight = Math.min(availableHeight, openBelow ? spaceBelow : spaceAbove);
            const left = Math.min(Math.max(anchor.left, leftBound), Math.max(leftBound, rightBound - width));
            const top = openBelow
                ? Math.min(anchor.bottom + 4, bottomBound)
                : Math.max(topBound, anchor.top - 4 - Math.min(desiredHeight, maxHeight));

            panel.style.bottom = "auto";
            panel.style.left = `${left}px`;
            panel.style.maxHeight = `${maxHeight}px`;
            panel.style.maxWidth = `${availableWidth}px`;
            panel.style.minWidth = "0";
            panel.style.right = "auto";
            panel.style.top = `${top}px`;
            panel.style.width = `${width}px`;
            panel.style.removeProperty("visibility");
        };
        let positionFrame = 0;
        const positionOpenPanel = () => {
            window.cancelAnimationFrame(positionFrame);
            positionFrame = window.requestAnimationFrame(() => {
                positionFrame = 0;
                positionPanel(items().find(item => item.open));
            });
        };
        const closeOthers = except => {
            for (const item of items()) {
                if (item !== except) {
                    item.open = false;
                    clearPanelPosition(item);
                }
            }
        };
        const pointerDown = event => {
            const containingItem = event.target.closest?.("details");
            closeOthers(containingItem && container.contains(containingItem) ? containingItem : null);
        };
        const closeButtonClick = event => {
            const containingItem = event.target.closest?.("details");
            if (event.target.closest?.(".tool-panel-close") && containingItem && container.contains(containingItem)) {
                containingItem.open = false;
                containingItem.querySelector(":scope > summary")?.focus();
            }
        };
        const keyDown = event => {
            if (event.key !== "Escape") return;
            const openItem = items().find(item => item.open);
            if (!openItem) return;
            openItem.open = false;
            openItem.querySelector(":scope > summary")?.focus();
            event.preventDefault();
        };
        const toggle = event => {
            if (event.target.open) {
                closeOthers(event.target);
                positionOpenPanel();
            } else {
                clearPanelPosition(event.target);
            }
        };
        document.addEventListener("pointerdown", pointerDown, true);
        document.addEventListener("keydown", keyDown, true);
        window.addEventListener("resize", positionOpenPanel);
        window.visualViewport?.addEventListener("resize", positionOpenPanel);
        window.visualViewport?.addEventListener("scroll", positionOpenPanel);
        container.addEventListener("toggle", toggle, true);
        container.addEventListener("click", closeButtonClick);
        window.tcBrowser.menuCleanups.set(container, () => {
            window.cancelAnimationFrame(positionFrame);
            document.removeEventListener("pointerdown", pointerDown, true);
            document.removeEventListener("keydown", keyDown, true);
            window.removeEventListener("resize", positionOpenPanel);
            window.visualViewport?.removeEventListener("resize", positionOpenPanel);
            window.visualViewport?.removeEventListener("scroll", positionOpenPanel);
            container.removeEventListener("toggle", toggle, true);
            container.removeEventListener("click", closeButtonClick);
            for (const item of items()) clearPanelPosition(item);
        });
    },
    detachMenuDismiss(container) {
        if (!container) return;
        const cleanup = window.tcBrowser.menuCleanups.get(container);
        if (cleanup) cleanup();
        window.tcBrowser.menuCleanups.delete(container);
    },
    download(fileName, content) {
        const blob = new Blob([content], { type: "text/csv;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }
};

window.tcBrowser.isVisible = () => document.visibilityState === "visible";

window.tcBrowser.copyText = async text => {
    if (!navigator.clipboard?.writeText) return false;
    try { await navigator.clipboard.writeText(text); return true; }
    catch { return false; }
};
window.tcBrowser.watchAppearance = target => {
    window.tcBrowser.unwatchAppearance?.();
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    const changed = event => target.invokeMethodAsync('SystemAppearanceChanged', event.matches);
    const stored = event => {
        if (event.key === 'torrentcore.webui2.preferences') target.invokeMethodAsync('PreferencesChanged');
    };
    query.addEventListener('change', changed);
    window.addEventListener('storage', stored);
    window.tcBrowser.unwatchAppearance = () => {
        query.removeEventListener('change', changed);
        window.removeEventListener('storage', stored);
    };
    return query.matches;
};
window.tcBrowser.attachSidebar = (sidebar, target) => {
    if (window.tcBrowser.sidebarElement === sidebar) return;
    window.tcBrowser.detachSidebar();
    window.tcBrowser.sidebarElement = sidebar;
    const handle = sidebar.querySelector('.sidebar-resize');
    if (!handle) return;
    let dragging = false;
    const down = event => {
        if (event.button !== 0) return;
        dragging = true;
        handle.setPointerCapture(event.pointerId);
        event.preventDefault();
    };
    const move = event => {
        if (!dragging) return;
        const width = Math.max(180, Math.min(360, event.clientX));
        sidebar.closest('.desktop-shell').style.setProperty('--nav-width', width + 'px');
    };
    const end = event => {
        if (!dragging) return;
        dragging = false;
        target.invokeMethodAsync('SetSidebarWidth', Math.max(180, Math.min(360, Math.round(event.clientX))));
    };
    handle.addEventListener('pointerdown', down);
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', end);
    handle.addEventListener('pointercancel', end);
    window.tcBrowser.detachSidebar = () => {
        window.tcBrowser.sidebarElement = null;
        handle.removeEventListener('pointerdown', down);
        handle.removeEventListener('pointermove', move);
        handle.removeEventListener('pointerup', end);
        handle.removeEventListener('pointercancel', end);
    };
};
window.tcBrowser.detachSidebar = () => {};

window.tcBrowser.language = () => navigator.language || "en-US";

// A stable browser identity refers to profiles in the host's external Config directory.
// No deployment version is part of these keys: upgrades keep the same browser and profiles.
window.tcBrowser.owner = () => {
    const key = 'torrentcore.webui2.browser';
    let value = localStorage.getItem(key);
    if (!value) {
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const hex = Array.from(bytes, x => x.toString(16).padStart(2, '0')).join('');
        value = `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
        localStorage.setItem(key, value);
    }
    return value;
};
window.tcBrowser.unsubscribeConnections = () => {};
window.tcBrowser.subscribeConnections = target => {
    window.tcBrowser.unsubscribeConnections();
    const reload = () => target.invokeMethodAsync('ReloadAsync').catch(error => console.error('Connection refresh failed.', error));
    const storage = event => {
        if (event.key === 'torrentcore.webui2.connections.changed') reload();
    };
    const visible = () => { if (document.visibilityState === 'visible') reload(); };
    window.addEventListener('storage', storage);
    document.addEventListener('visibilitychange', visible);
    window.tcBrowser.unsubscribeConnections = () => {
        window.removeEventListener('storage', storage);
        document.removeEventListener('visibilitychange', visible);
    };
};
window.tcBrowser.connectionChanged = () => localStorage.setItem('torrentcore.webui2.connections.changed', `${Date.now()}-${Math.random()}`);
