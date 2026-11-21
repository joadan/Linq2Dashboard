// Small enhancements for the docs site that need no state in Blazor: the theme toggle, syntax highlighting, copy
// buttons on code blocks and the "On this page" table of contents. Blazor renders the elements; this script only adds
// to elements Blazor leaves alone (code text that never changes, the empty table-of-contents aside).
(() => {
    const themeKey = "l2d-docs-theme";
    const root = document.documentElement;
    const systemDark = window.matchMedia("(prefers-color-scheme: dark)");

    function storedTheme() {
        try {
            return localStorage.getItem(themeKey);
        } catch {
            return null;
        }
    }

    function applyTheme(theme) {
        root.dataset.theme = theme;
    }

    // index.html sets the theme inline before the first paint; this follows the system while nothing is stored.
    systemDark.addEventListener("change", e => {
        if (!storedTheme()) {
            applyTheme(e.matches ? "dark" : "light");
        }
    });

    document.addEventListener("click", async e => {
        if (e.target.closest("[data-theme-toggle]")) {
            const next = root.dataset.theme === "dark" ? "light" : "dark";
            applyTheme(next);
            try {
                localStorage.setItem(themeKey, next);
            } catch {
                // storage blocked: the choice lasts for this visit
            }
            return;
        }

        const copy = e.target.closest(".code-copy");
        if (copy) {
            const code = copy.closest(".code-block")?.querySelector("pre");
            if (!code) {
                return;
            }
            try {
                await navigator.clipboard.writeText(code.innerText.replace(/\n$/, ""));
                copy.dataset.copied = "";
                setTimeout(() => delete copy.dataset.copied, 1600);
            } catch {
                // clipboard refused: nothing to do
            }
        }
    });

    // A link to a heading on the same page scrolls there. With <base href="/"> a bare "#id" would resolve to the home
    // page, and the Blazor router would navigate; capture the click before it does.
    document.addEventListener("click", e => {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) {
            return;
        }
        const link = e.target.closest("a[href]");
        if (!link) {
            return;
        }
        const href = link.getAttribute("href");
        let id = null;
        if (href.startsWith("#")) {
            id = href.slice(1);
        } else {
            const url = new URL(link.href, document.baseURI);
            if (url.hash && url.origin === location.origin && url.pathname === location.pathname) {
                id = url.hash.slice(1);
            }
        }
        const target = id && document.getElementById(decodeURIComponent(id));
        if (!target) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        history.replaceState(history.state, "", location.pathname + location.search + "#" + id);
        target.scrollIntoView({ behavior: "smooth", block: "start" });
    }, true);

    function slug(text) {
        return text.toLowerCase().trim()
            .replace(/[^\p{L}\p{N}\s-]/gu, "")
            .replace(/\s+/g, "-");
    }

    function highlight() {
        if (!window.hljs) {
            return;
        }
        // Only blocks that name a language: their text is fixed, so Blazor never rewrites the nodes highlight.js replaces.
        document.querySelectorAll("pre code[class*='language-']:not([data-highlighted])").forEach(el => {
            window.hljs.highlightElement(el);
        });
    }

    let tocSignature = "";
    let tocLinks = [];

    function buildToc() {
        const toc = document.querySelector("[data-toc]");
        const content = document.querySelector(".site-content");
        if (!toc || !content) {
            tocSignature = "";
            tocLinks = [];
            return;
        }

        const headings = [...content.querySelectorAll("h2, h3")].filter(h => !h.closest(".l2d-dashboard, .code-block"));
        const used = new Set();
        for (const h of headings) {
            if (!h.id) {
                let id = slug(h.textContent) || "section";
                while (used.has(id) || document.getElementById(id)) {
                    id += "-";
                }
                h.id = id;
            }
            used.add(h.id);
        }

        const signature = location.pathname + "|" + headings.map(h => h.tagName + h.id).join(",");
        if (signature === tocSignature && toc.childElementCount > 0) {
            return;
        }
        tocSignature = signature;

        toc.replaceChildren();
        if (headings.length < 2) {
            tocLinks = [];
            return;
        }

        const title = document.createElement("div");
        title.className = "toc-title";
        title.textContent = "On this page";
        const list = document.createElement("ul");
        tocLinks = headings.map(h => {
            const item = document.createElement("li");
            const link = document.createElement("a");
            link.href = location.pathname + "#" + h.id;
            link.textContent = h.textContent;
            link.className = h.tagName === "H3" ? "toc-h3" : "toc-h2";
            item.append(link);
            list.append(item);
            return { link, heading: h };
        });
        toc.append(title, list);
        markActive();
    }

    function markActive() {
        if (tocLinks.length === 0) {
            return;
        }
        const offset = parseFloat(getComputedStyle(root).scrollPaddingTop) || 80;
        let active = tocLinks[0];
        for (const entry of tocLinks) {
            if (entry.heading.getBoundingClientRect().top - offset - 8 <= 0) {
                active = entry;
            } else {
                break;
            }
        }
        // At the bottom of the page the last headings may never reach the top; mark the last one.
        if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4) {
            active = tocLinks[tocLinks.length - 1];
        }
        for (const entry of tocLinks) {
            entry.link.classList.toggle("active", entry === active);
        }
    }

    let pendingHash = location.hash;

    function scrollToPendingHash() {
        if (!pendingHash) {
            return;
        }
        const target = document.getElementById(decodeURIComponent(pendingHash.slice(1)));
        if (target) {
            pendingHash = "";
            target.scrollIntoView({ block: "start" });
        }
    }

    let scheduled = false;

    function refresh() {
        scheduled = false;
        highlight();
        buildToc();
        scrollToPendingHash();
    }

    function schedule() {
        if (!scheduled) {
            scheduled = true;
            requestAnimationFrame(refresh);
        }
    }

    new MutationObserver(mutations => {
        // Ignore what this script does to the table of contents.
        if (mutations.every(m => m.target.closest?.("[data-toc]"))) {
            return;
        }
        schedule();
    }).observe(document.body, { childList: true, subtree: true });

    let scrollQueued = false;
    window.addEventListener("scroll", () => {
        if (!scrollQueued) {
            scrollQueued = true;
            requestAnimationFrame(() => {
                scrollQueued = false;
                markActive();
            });
        }
    }, { passive: true });

    schedule();
})();
