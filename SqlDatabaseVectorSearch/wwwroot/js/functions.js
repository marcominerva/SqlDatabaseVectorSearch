window.setFocus = (element) => {
    if (element) {
        element.focus();
    }
};
function getLocalTime(utcDateTime) {
    return new Date(utcDateTime).toLocaleString();
}
