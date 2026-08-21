window.tabletRack = {
    scrollTo: function (elementId) {
        document.getElementById(elementId)?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
    }
};
