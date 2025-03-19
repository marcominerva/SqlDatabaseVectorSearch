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

window.resetFileInput = (elementId) => {
    document.getElementById(elementId).value = '';
};

function getLocalTime(utcDateTime) {
    return new Date(utcDateTime).toLocaleString();
}