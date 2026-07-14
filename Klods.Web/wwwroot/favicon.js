// Composites the Klods body + eye layers onto a canvas and points the browser's
// favicon at the result — the tab icon then follows the signed-in user's bot.
window.klodsFavicon = (function () {
    let canvas;

    function ensureCanvas() {
        if (!canvas) {
            canvas = document.createElement("canvas");
            canvas.width = 64;
            canvas.height = 64;
        }
        return canvas;
    }

    function loadImage(src) {
        return new Promise((resolve, reject) => {
            const img = new Image();
            img.onload = () => resolve(img);
            img.onerror = reject;
            img.src = src;
        });
    }

    async function set(bodyUrl, eyesUrl) {
        try {
            const c = ensureCanvas();
            const ctx = c.getContext("2d");
            const [body, eyes] = await Promise.all([loadImage(bodyUrl), loadImage(eyesUrl)]);
            ctx.clearRect(0, 0, c.width, c.height);
            ctx.drawImage(body, 0, 0, c.width, c.height);
            ctx.drawImage(eyes, 0, 0, c.width, c.height);
            const dataUrl = c.toDataURL("image/png");

            const links = document.querySelectorAll('link[rel="icon"]');
            if (links.length === 0) {
                const link = document.createElement("link");
                link.rel = "icon";
                link.type = "image/png";
                document.head.appendChild(link);
                link.href = dataUrl;
            } else {
                links.forEach(l => { l.href = dataUrl; });
            }
        } catch {
            // Leave the static favicon in place on any failure.
        }
    }

    return { set };
})();
