// Rich renderer: markdown-it (sanitized) + KaTeX auto-render + highlight.js.
// Receives {md, dark} from the host, renders into #content, and posts the content
// height back so the WinUI control can size itself inside the scrolling message list.
(function () {
    "use strict";

    const md = window.markdownit({
        html: false,        // sanitize: never trust model output as raw HTML
        linkify: true,
        breaks: false,
        highlight: function (code, lang) {
            if (lang && window.hljs && hljs.getLanguage(lang)) {
                try { return hljs.highlight(code, { language: lang }).value; } catch (e) { /* fall through */ }
            }
            return "";      // let markdown-it escape it
        }
    });

    const KATEX_DELIMS = [
        { left: "$$", right: "$$", display: true },
        { left: "\\[", right: "\\]", display: true },
        { left: "\\(", right: "\\)", display: false },
        { left: "$", right: "$", display: false }
    ];

    const content = document.getElementById("content");
    let lastHeight = -1;

    function postHeight() {
        // Round up; only post when it actually changes to avoid feedback churn.
        const h = Math.ceil(content.getBoundingClientRect().height);
        if (h !== lastHeight && h > 0) {
            lastHeight = h;
            window.chrome.webview.postMessage(String(h)); // string so the host reads it uniformly
        }
    }

    function render(payload) {
        let data;
        try { data = JSON.parse(payload); } catch (e) { data = { md: String(payload), dark: true }; }

        document.body.className = data.dark === false ? "theme-light" : "theme-dark";
        content.innerHTML = md.render(data.md || "");

        if (window.renderMathInElement) {
            try {
                renderMathInElement(content, { delimiters: KATEX_DELIMS, throwOnError: false, ignoredTags: ["script", "noscript", "style", "textarea", "pre", "code"] });
            } catch (e) { /* leave raw text rather than break the bubble */ }
        }

        // KaTeX swaps fonts in asynchronously; re-measure after they settle.
        postHeight();
        requestAnimationFrame(postHeight);
        if (document.fonts && document.fonts.ready) document.fonts.ready.then(postHeight);
    }

    window.chrome.webview.addEventListener("message", function (e) { render(e.data); });
    window.addEventListener("resize", postHeight);

    // Signal readiness so the host flushes any markdown queued before the page loaded.
    window.chrome.webview.postMessage("ready");
})();
