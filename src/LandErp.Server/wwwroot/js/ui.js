window.LandErpUi = {
    open: function (dialog) {
        if (!dialog.open) dialog.showModal();
        dialog.addEventListener("cancel", function (event) { event.preventDefault(); });
    }
};
