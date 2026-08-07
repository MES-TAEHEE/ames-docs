window.pdaScan = (() => {
    let dotNetRef = null;
    let buffer = "";
    let lastKeyAt = 0;
    let idleTimer = null;
    let inputTimer = null;
    let lastEmitText = "";
    let lastEmitAt = 0;
    let options = {};
    let captureInput = null;

    const defaultOptions = {
        minLength: 3,
        gapMs: 85,
        idleMs: 130,
        inputIdleMs: 450,
        duplicateMs: 1200,
        scanInputSelector: ".pda-scan-input",
        developerMode: false
    };

    function activeOptions() {
        return { ...defaultOptions, ...(options || {}) };
    }

    function normalize(value) {
        return (value || "")
            .replace(/\u0002/g, "")
            .replace(/\u0003/g, "")
            .replace(/[\r\n]/g, "")
            .trim();
    }

    function isDeveloperMode() {
        const opt = activeOptions();
        return opt.developerMode === true || opt.developerMode === "true" || opt.developerMode === 1;
    }

    function isScanInput(element) {
        return element
            && typeof element.matches === "function"
            && element.matches(activeOptions().scanInputSelector);
    }

    function isCaptureInput(element) {
        return captureInput && element === captureInput;
    }

    function nearestScanInput(target) {
        if (!target || typeof target.closest !== "function") {
            return null;
        }

        return target.closest(activeOptions().scanInputSelector);
    }

    function applyScanInputMode() {
        const selector = activeOptions().scanInputSelector;
        if (!selector) return;

        document.querySelectorAll(selector).forEach((element) => {
            if (isDeveloperMode()) {
                element.classList.remove("pda-scan-only");
                element.removeAttribute("data-scan-only");
                element.removeAttribute("inputmode");
                return;
            }

            element.classList.add("pda-scan-only");
            element.setAttribute("data-scan-only", "true");
            element.setAttribute("inputmode", "none");
            element.setAttribute("autocomplete", "off");
            element.setAttribute("autocorrect", "off");
            element.setAttribute("autocapitalize", "off");
            element.setAttribute("spellcheck", "false");
        });
    }

    function ensureCaptureInput() {
        if (captureInput && document.body.contains(captureInput)) {
            return captureInput;
        }

        captureInput = document.createElement("input");
        captureInput.type = "text";
        captureInput.autocomplete = "off";
        captureInput.setAttribute("inputmode", "none");
        captureInput.setAttribute("autocorrect", "off");
        captureInput.setAttribute("autocapitalize", "off");
        captureInput.setAttribute("spellcheck", "false");
        captureInput.setAttribute("aria-hidden", "true");
        captureInput.setAttribute("virtualkeyboardpolicy", "manual");
        captureInput.className = "pda-hidden-scan-capture";
        Object.assign(captureInput.style, {
            position: "fixed",
            left: "-1000px",
            top: "0",
            width: "1px",
            height: "1px",
            opacity: "0.01",
            border: "0",
            padding: "0",
            zIndex: "-1"
        });

        document.body.appendChild(captureInput);
        return captureInput;
    }

    function focusCaptureInput() {
        const input = ensureCaptureInput();
        try {
            input.focus({ preventScroll: true });
        } catch {
            input.focus();
        }
    }

    function clearBuffer() {
        buffer = "";
        if (idleTimer) {
            clearTimeout(idleTimer);
            idleTimer = null;
        }
    }

    function emitText(text) {
        if (!dotNetRef || text.length < activeOptions().minLength) {
            return;
        }

        const opt = activeOptions();
        const now = performance.now();
        if (text === lastEmitText && now - lastEmitAt < opt.duplicateMs) {
            return;
        }

        lastEmitText = text;
        lastEmitAt = now;
        if (captureInput) {
            captureInput.value = "";
        }
        dotNetRef.invokeMethodAsync("OnHardwareScanAsync", text).catch(() => {});
    }

    function emitScan() {
        const text = normalize(buffer);
        clearBuffer();
        emitText(text);
    }

    function onKeyDown(event) {
        if (!dotNetRef || event.defaultPrevented) {
            return;
        }

        const target = event.target;
        if (target) {
            const tagName = (target.tagName || "").toLowerCase();
            const isEditable = tagName === "input"
                || tagName === "textarea"
                || tagName === "select"
                || target.isContentEditable;

            if (isEditable && !isCaptureInput(target) && (!isScanInput(target) || isDeveloperMode())) {
                return;
            }
        }

        if (event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        const opt = activeOptions();
        const key = event.key || "";

        if (key === "Enter" || key === "Tab") {
            if (buffer.length >= opt.minLength) {
                event.preventDefault();
                event.stopImmediatePropagation();
                emitScan();
            } else {
                clearBuffer();
            }
            return;
        }

        if (key.length !== 1) {
            return;
        }

        const now = performance.now();
        if (buffer && now - lastKeyAt > opt.gapMs) {
            buffer = "";
        }

        buffer += key;
        lastKeyAt = now;

        if (idleTimer) {
            clearTimeout(idleTimer);
        }

        idleTimer = setTimeout(() => {
            if (buffer.length >= opt.minLength && performance.now() - lastKeyAt >= opt.idleMs) {
                emitScan();
            } else {
                clearBuffer();
            }
        }, opt.idleMs);
    }

    function onInput(event) {
        if (!dotNetRef) {
            return;
        }

        const target = event.target;
        if (!target || (!isCaptureInput(target) && (typeof target.matches !== "function" || !target.matches(activeOptions().scanInputSelector)))) {
            return;
        }

        const text = normalize(target.value);
        if (text.length < activeOptions().minLength) {
            return;
        }

        if (inputTimer) {
            clearTimeout(inputTimer);
        }

        inputTimer = setTimeout(() => {
            const current = normalize(target.value);
            if (current === text && current.length >= activeOptions().minLength) {
                emitText(current);
            }
            target.value = "";
        }, activeOptions().inputIdleMs);
    }

    function onScanInputPointer(event) {
        if (isDeveloperMode()) {
            return;
        }

        const element = nearestScanInput(event.target);
        if (!element) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        focusCaptureInput();
    }

    document.addEventListener("keydown", onKeyDown, true);
    document.addEventListener("input", onInput, true);
    document.addEventListener("pointerdown", onScanInputPointer, true);
    document.addEventListener("mousedown", onScanInputPointer, true);
    document.addEventListener("touchstart", onScanInputPointer, true);
    document.addEventListener("click", onScanInputPointer, true);

    function focus(selector) {
        if (!selector) return;

        setTimeout(() => {
            applyScanInputMode();
            if (!isDeveloperMode()) {
                focusCaptureInput();
                return;
            }

            const element = document.querySelector(selector);
            if (!element || element.disabled) return;

            if (document.activeElement === element) return;

            element.focus({ preventScroll: true });
            if (typeof element.select === "function") {
                element.select();
            }
        }, 50);
    }

    return {
        register(ref, opts) {
            dotNetRef = ref;
            options = opts || {};
            clearBuffer();
            lastEmitText = "";
            lastEmitAt = 0;
            applyScanInputMode();
            if (!isDeveloperMode()) {
                focusCaptureInput();
            }
        },
        unregister() {
            dotNetRef = null;
            options = {};
            clearBuffer();
            if (inputTimer) {
                clearTimeout(inputTimer);
                inputTimer = null;
            }
            if (captureInput) {
                captureInput.value = "";
            }
        },
        focus
    };
})();
