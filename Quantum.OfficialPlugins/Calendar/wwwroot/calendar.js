const initializedDialogs = new WeakSet();

export function syncEditor(dialog, open) {
    if (!dialog?.isConnected) {
        return;
    }

    if (!initializedDialogs.has(dialog)) {
        dialog.addEventListener("cancel", event => {
            event.preventDefault();
            dialog.querySelector("[data-editor-close]")?.click();
        });
        initializedDialogs.add(dialog);
    }

    if (open && !dialog.open) {
        dialog.showModal();
    } else if (!open && dialog.open) {
        dialog.close();
    }
}
