window.jiroad = {
    save: function (json) {
        localStorage.setItem("jiroad_state_v1", json);
    },
    load: function () {
        return localStorage.getItem("jiroad_state_v1");
    },
    saveToken: function (token) {
        localStorage.setItem("jiroad_token_v1", token);
    },
    loadToken: function () {
        return localStorage.getItem("jiroad_token_v1");
    },
    clearToken: function () {
        localStorage.removeItem("jiroad_token_v1");
    },
    download: function (fileName, content) {
        const blob = new Blob([content], { type: "application/json;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = fileName;
        a.click();
        URL.revokeObjectURL(url);
    },
    canvasRect: function (element) {
        if (!element) {
            return { left: 0, top: 0 };
        }
        const r = element.getBoundingClientRect();
        return { left: r.left, top: r.top };
    },
    centerCanvas: function (element) {
        if (!element) return;
        const x = Math.max(0, (element.scrollWidth - element.clientWidth) / 2);
        const y = Math.max(0, (element.scrollHeight - element.clientHeight) / 2);
        element.scrollTo({ left: x, top: y, behavior: "smooth" });
    }
};

window.jiroad = window.jiroad || {};

// Добавь вот этот метод для точного расчёта позиции мыши на холсте
window.jiroad.getCanvasBoundingRect = function (selector) {
    const el = document.querySelector(selector);
    if (!el) return { left: 0, top: 0 };
    const rect = el.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top
    };
};

(function () {
    const HIDE_DELAY_MS = 3000;
    const bootIntro = () => {
        if (document.getElementById("introSplash")) {
            document.body.classList.remove("intro-complete");
            window.setTimeout(() => document.body.classList.add("intro-complete"), HIDE_DELAY_MS);
        } else {
            document.body.classList.add("intro-complete");
        }
    };
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bootIntro, { once: true });
    } else bootIntro();
})();
