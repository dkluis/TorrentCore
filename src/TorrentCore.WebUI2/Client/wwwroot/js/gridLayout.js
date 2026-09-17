const registrations = new WeakMap();

function readLayout(frame) {
    const headers = [...frame.querySelectorAll('thead th')]
        .filter(header => [...header.classList].some(name => name.startsWith('shows-column-')));
    const orderedColumnKeys = [];
    const widths = {};

    for (const header of headers) {
        const keyClass = [...header.classList].find(name => name.startsWith('shows-column-'));
        if (!keyClass) continue;
        const key = keyClass.substring('shows-column-'.length);
        orderedColumnKeys.push(key);
        widths[key] = Math.round(header.getBoundingClientRect().width * 10) / 10;
    }

    return { orderedColumnKeys, widths };
}

function readOrder(frame) {
    return readLayout(frame).orderedColumnKeys;
}

function sameOrder(left, right) {
    return left.length === right.length && left.every((key, index) => key === right[index]);
}

function reportError(receiver, error) {
    receiver.invokeMethodAsync('ReportLayoutErrorAsync', error?.message ?? String(error))
        .catch(reportError => console.error('Unable to report the table layout persistence error.', reportError));
}

export function attach(frame, receiver, layoutSignature, gridGeneration) {
    detach(frame);
    const registration = { receiver, layoutSignature, gridGeneration, pendingReorder: null, pendingResize: null, pendingViewport: null };
    let headerGesture = null;
    const beginHeaderGesture = event => {
        if (!event.target.closest('thead') || event.target.closest('button, a')) return;
        const resizer = event.target.closest('.mud-resizer');
        registration.pendingResize = null;
        headerGesture = {
            x: event.clientX,
            y: event.clientY,
            pointerId: event.pointerId,
            resizedHeader: resizer?.closest('th') ?? null,
            sawActiveResize: resizer?.classList.contains('mud-resizing') ?? false,
            layoutSignature: registration.layoutSignature,
            gridGeneration: registration.gridGeneration
        };
    };
    const endHeaderGesture = event => {
        if (!headerGesture || event.pointerId !== headerGesture.pointerId) return;
        const moved = Math.abs(event.clientX - headerGesture.x) > 3 || Math.abs(event.clientY - headerGesture.y) > 3;
        const gesture = headerGesture;
        headerGesture = null;
        if (!moved || !gesture.resizedHeader || !registrations.has(frame)) return;
        gesture.sawActiveResize ||= gesture.resizedHeader.querySelector('.mud-resizer')?.classList.contains('mud-resizing') ?? false;
        registration.pendingResize = gesture;
    };
    const cancelHeaderGesture = event => {
        if (headerGesture?.pointerId === event.pointerId) headerGesture = null;
        if (registration.pendingResize?.pointerId === event.pointerId) registration.pendingResize = null;
    };
    const beginReorder = event => {
        if (!event.target.closest('thead') || event.target.closest('.mud-resizer, button, a')) return;
        registration.pendingResize = null;
        registration.pendingReorder = {
            baselineOrder: readOrder(frame),
            layoutSignature: registration.layoutSignature,
            gridGeneration: registration.gridGeneration,
            dropped: false
        };
    };
    const awaitReorder = () => {
        if (registration.pendingReorder) registration.pendingReorder.dropped = true;
    };
    const cancelReorder = () => {
        if (registration.pendingReorder && !registration.pendingReorder.dropped) {
            registration.pendingReorder = null;
        }
    };
    const selectWithKeyboard = event => {
        if ((event.key === 'Enter' || event.key === ' ') && event.target === event.currentTarget) {
            event.preventDefault();
            event.currentTarget.click();
        }
    };
    const enhanceRows = () => frame.querySelectorAll('tbody tr').forEach(row => {
        row.setAttribute('aria-selected', row.classList.contains('vendor-selected-row') ? 'true' : 'false');
        if (row.dataset.keyboardSelection === 'true') return;
        row.dataset.keyboardSelection = 'true';
        row.tabIndex = 0;
        row.addEventListener('keydown', selectWithKeyboard);
    });
    const observer = new MutationObserver(() => {
        enhanceRows();
        if (headerGesture?.resizedHeader?.querySelector('.mud-resizer.mud-resizing')) {
            headerGesture.sawActiveResize = true;
        }
        const resize = registration.pendingResize;
        if (resize) {
            const resizer = resize.resizedHeader.querySelector('.mud-resizer');
            if (resizer?.classList.contains('mud-resizing')) {
                resize.sawActiveResize = true;
            }
            if (!resize.sawActiveResize || resizer?.classList.contains('mud-resizing')) return;
            registration.pendingResize = null;
            const keyClass = [...resize.resizedHeader.classList].find(name => name.startsWith('shows-column-'));
            if (keyClass) {
                const key = keyClass.substring('shows-column-'.length);
                const width = Math.round(resize.resizedHeader.getBoundingClientRect().width * 10) / 10;
                receiver.invokeMethodAsync('PersistColumnWidthAsync', key, width, resize.layoutSignature, resize.gridGeneration)
                    .catch(error => reportError(receiver, error));
            }
        }
        const pending = registration.pendingReorder;
        if (!pending?.dropped) return;
        const layout = readLayout(frame);
        if (sameOrder(layout.orderedColumnKeys, pending.baselineOrder)) return;
        registration.pendingReorder = null;
        const viewport = frame.querySelector('.mud-table-container');
        registration.pendingViewport = viewport
            ? { left: viewport.scrollLeft, top: viewport.scrollTop, gridGeneration: pending.gridGeneration }
            : null;
        receiver.invokeMethodAsync('PersistLayoutAsync', layout, pending.layoutSignature, pending.gridGeneration)
            .then(accepted => {
                if (!accepted) registration.pendingViewport = null;
            })
            .catch(error => {
                registration.pendingViewport = null;
                reportError(receiver, error);
            });
    });
    observer.observe(frame, { attributes: true, attributeFilter: ['class'], childList: true, subtree: true });
    enhanceRows();
    frame.addEventListener('pointerdown', beginHeaderGesture, true);
    document.addEventListener('pointerup', endHeaderGesture);
    document.addEventListener('pointercancel', cancelHeaderGesture);
    frame.addEventListener('dragstart', beginReorder, true);
    frame.addEventListener('drop', awaitReorder, true);
    frame.addEventListener('dragend', cancelReorder, true);
    Object.assign(registration, { observer, selectWithKeyboard, beginHeaderGesture, endHeaderGesture, cancelHeaderGesture, beginReorder, awaitReorder, cancelReorder, clearHeaderGesture: () => { headerGesture = null; } });
    registrations.set(frame, registration);
}

export function refresh(frame, layoutSignature, gridGeneration) {
    const registration = registrations.get(frame);
    if (registration) {
        registration.layoutSignature = layoutSignature;
        const previousGeneration = registration.gridGeneration;
        registration.gridGeneration = gridGeneration;
        registration.pendingReorder = null;
        registration.pendingResize = null;
        registration.clearHeaderGesture();
        if (gridGeneration !== previousGeneration && registration.pendingViewport?.gridGeneration === previousGeneration) {
            const viewport = frame.querySelector('.mud-table-container');
            if (viewport) {
                viewport.scrollLeft = registration.pendingViewport.left;
                viewport.scrollTop = registration.pendingViewport.top;
            }
            registration.pendingViewport = null;
        }
        frame.querySelectorAll('tbody tr').forEach(row => {
            row.setAttribute('aria-selected', row.classList.contains('vendor-selected-row') ? 'true' : 'false');
        });
    }
}

export function detach(frame) {
    const registration = registrations.get(frame);
    if (!registration) return;
    frame.removeEventListener('pointerdown', registration.beginHeaderGesture, true);
    document.removeEventListener('pointerup', registration.endHeaderGesture);
    document.removeEventListener('pointercancel', registration.cancelHeaderGesture);
    frame.removeEventListener('dragstart', registration.beginReorder, true);
    frame.removeEventListener('drop', registration.awaitReorder, true);
    frame.removeEventListener('dragend', registration.cancelReorder, true);
    registration.pendingReorder = null;
    registration.pendingResize = null;
    registration.pendingViewport = null;
    registration.observer.disconnect();
    frame.querySelectorAll('tbody tr').forEach(row => row.removeEventListener('keydown', registration.selectWithKeyboard));
    registrations.delete(frame);
}
