window.setFocus = (element) => {
    if (element) {
        element.focus();
    }
};

window.scrollTo = (element) => {
    if (element) {
        element.scrollIntoView();
    }
}

function getLocalTime(utcDateTime) {
    return new Date(utcDateTime).toLocaleString();
}