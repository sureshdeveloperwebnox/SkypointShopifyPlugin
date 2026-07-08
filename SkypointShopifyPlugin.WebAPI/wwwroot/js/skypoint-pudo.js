// DIAGNOSTIC: runs synchronously the instant the script parses
(function(){
    var d=document.createElement('div');
    d.id='sp-diag';
    d.textContent='✅ SkyPoint JS loaded — waiting for DOM...';
    d.style.cssText='position:fixed!important;top:0!important;left:0!important;right:0!important;'+
        'background:#7c3aed!important;color:#fff!important;z-index:2147483647!important;'+
        'text-align:center!important;padding:8px!important;font-size:14px!important;'+
        'font-family:monospace!important;';
    // Append to <html> since <body> may not exist yet
    (document.body || document.documentElement).appendChild(d);
})();

(function () {
    'use strict';

    // Prevent double execution
    if (window.SkypointPudoIntegrated) return;
    window.SkypointPudoIntegrated = true;

    // =========================================================================
    // CONFIG
    // =========================================================================
    var _scriptSrc = '';
    if (document.currentScript && document.currentScript.src) {
        _scriptSrc = document.currentScript.src;
    } else {
        var scripts = document.getElementsByTagName('script');
        for (var si = 0; si < scripts.length; si++) {
            if (scripts[si].src && (scripts[si].src.indexOf('skypoint-pudo') !== -1 || scripts[si].src.indexOf('pudo') !== -1)) {
                _scriptSrc = scripts[si].src;
                break;
            }
        }
    }

    var backendUrl = '';
    try {
        backendUrl = _scriptSrc ? new URL(_scriptSrc).origin : window.location.origin;
    } catch (e) {
        backendUrl = window.location.origin;
    }

    var shopDomain = (window.Shopify && window.Shopify.shop)
        ? window.Shopify.shop
        : window.location.hostname;

    var rootPath = (window.Shopify && window.Shopify.routes && window.Shopify.routes.root) || '/';
    var normalizedRoot = rootPath.replace(/\/$/, '');

    var path = window.location.pathname;
    var isCartPage     = (path === '/cart' || path === '/cart/');
    var isCheckoutPage = path.indexOf('/checkouts') !== -1 || path.indexOf('/checkout') !== -1;

    var currentPudo = null;
    var inlineInjected = false;

    var BTN_SELECTORS = [
        'button[name="checkout"]',
        'input[name="checkout"]',
        '#checkout',
        '.cart__checkout-button',
        '.checkout-button',
        'a.cart__checkout',
        'button.cart__checkout',
        'a[href="/checkout"]',
        'a[href*="/checkout"]',
        'a[href*="/checkouts"]',
        '[data-action="checkout"]',
        '.cart-checkout-button',
        '.btn--full.cart__submit',
        '[data-testid="Checkout-button"]'
    ];

    console.log('[SkyPoint] Script loaded. path=' + path + ' isCart=' + isCartPage + ' backend=' + backendUrl);

    // =========================================================================
    // WAIT FOR DOM READY — then boot
    // =========================================================================
    function domReady(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn);
        } else {
            fn();
        }
    }

    function updateDiag(msg) {
        var d = document.getElementById('sp-diag');
        if (d) d.textContent = '🔵 SkyPoint: ' + msg;
    }

    // Generic, robust XHR request wrapper (bypasses any global fetch overrides/hooks from other apps)
    function makeRequest(method, url, data, callback) {
        var xhr = new XMLHttpRequest();
        xhr.open(method, url, true);
        
        // Bypass ngrok warning page during development (only for absolute backend URLs)
        if (url && url.indexOf('http') === 0) {
            xhr.setRequestHeader('ngrok-skip-browser-warning', '69420');
        }

        if (data) {
            xhr.setRequestHeader('Content-Type', 'application/json');
        }
        xhr.onreadystatechange = function () {
            if (xhr.readyState === 4) {
                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        var json = JSON.parse(xhr.responseText);
                        callback(null, json);
                    } catch (e) {
                        callback(new Error('Invalid JSON: ' + e.message), null);
                    }
                } else {
                    callback(new Error('HTTP ' + xhr.status), null);
                }
            }
        };
        xhr.onerror = function () {
            callback(new Error('Network error'), null);
        };
        xhr.send(data ? JSON.stringify(data) : null);
    }

    // =========================================================================
    // FETCH CART STATE THEN BOOT
    // =========================================================================
    domReady(function () {
        try {
            updateDiag('DOM ready. Booting UI immediately...');
            
            // Boot UI immediately so it is functional and not blocked by cart fetch latency
            boot();

            updateDiag('Fetching cart state...');
            makeRequest('GET', normalizedRoot + '/cart.js', null, function (err, cart) {
                try {
                    if (err) {
                        updateDiag('Cart fetch optional step: ' + err.message);
                        return;
                    }
                    updateDiag('Cart fetch OK.');
                    if (cart && cart.attributes && cart.attributes.pudo_code) {
                        currentPudo = {
                            code:     cart.attributes.pudo_code     || '',
                            name:     cart.attributes.pudo_name     || '',
                            addr1:    cart.attributes.pudo_addr1    || '',
                            addr2:    cart.attributes.pudo_addr2    || '',
                            city:     cart.attributes.pudo_city     || '',
                            pcode:    cart.attributes.pcode         || cart.attributes.pudo_zip || '',
                            provider: cart.attributes.pudo_provider || ''
                        };
                        renderInlineWidget();
                        var fab = document.getElementById('sp-float');
                        if (fab) renderFloatingWidget(fab);
                    }
                } catch (eInner) {
                    console.error('[SkyPoint] Inner error in cart callback:', eInner);
                    updateDiag('Cart callback error: ' + eInner.message);
                }
            });
        } catch (eBoot) {
            console.error('[SkyPoint] Boot error:', eBoot);
            updateDiag('Boot error: ' + eBoot.message);
        }
    });

    function boot() {
        if (isCartPage) {
            injectStyles();
            showFloatingWidget();     // ALWAYS show the floating widget first
            tryInlineInject();        // Also try to inject inline above checkout btn
            interceptCheckout();
        } else if (isCheckoutPage) {
            startCheckoutMode();
        }
    }

    // =========================================================================
    // FLOATING WIDGET — always visible, bottom-right corner
    // Fixed position so it's never affected by parent transforms/opacity
    // =========================================================================
    function showFloatingWidget() {
        var fab = document.getElementById('sp-float');
        if (!fab) {
            fab = document.createElement('div');
            fab.id = 'sp-float';
            (document.body || document.documentElement).appendChild(fab);
        }
        renderFloatingWidget(fab);
    }

    function renderFloatingWidget(fab) {
        if (!fab) return;

        if (currentPudo) {
            fab.innerHTML =
                '<div style="font-size:11px;font-weight:700;letter-spacing:.05em;color:rgba(255,255,255,.8);margin-bottom:4px;">✅ PUDO COUNTER</div>' +
                '<div style="font-weight:700;font-size:13px;">' + esc(currentPudo.name) + '</div>' +
                '<div style="font-size:11px;color:rgba(255,255,255,.75);margin-top:2px;">' + esc(currentPudo.city) + '</div>' +
                '<button id="sp-float-btn" type="button" style="' +
                  'margin-top:8px;width:100%;background:#fff;color:#16a34a;border:none;border-radius:6px;' +
                  'padding:7px 12px;font-weight:700;font-size:12px;cursor:pointer;' +
                '">✏️ Change Counter</button>';
            fab.style.background = '#16a34a';
        } else {
            fab.innerHTML =
                '<div style="font-size:11px;font-weight:700;letter-spacing:.05em;color:rgba(255,255,255,.85);margin-bottom:6px;">📦 PUDO DELIVERY</div>' +
                '<div style="font-size:12px;color:rgba(255,255,255,.9);margin-bottom:10px;line-height:1.4;">Pick up your order at a<br>nearby PUDO counter</div>' +
                '<button id="sp-float-btn" type="button" style="' +
                  'width:100%;background:#fff;color:#ff1e27;border:none;border-radius:6px;' +
                  'padding:9px 12px;font-weight:800;font-size:13px;cursor:pointer;' +
                  'box-shadow:0 2px 6px rgba(0,0,0,.15);' +
                '">🗺️ Select PUDO Counter</button>';
            fab.style.background = '#ff1e27';
        }

        Object.assign(fab.style, {
            position:   'fixed',
            bottom:     '24px',
            right:      '16px',
            width:      '200px',
            padding:    '14px',
            borderRadius: '14px',
            zIndex:     '2147483647',   // max z-index
            boxShadow:  '0 8px 30px rgba(0,0,0,.35)',
            fontFamily: 'Outfit,Inter,-apple-system,sans-serif',
            color:      '#fff',
            cursor:     'default'
        });

        var btn = document.getElementById('sp-float-btn');
        if (btn) btn.onclick = launchSelector;
    }

    // =========================================================================
    // INLINE WIDGET — tries to place widget above the checkout button
    // This is a nice-to-have. The float widget is the primary UX.
    // =========================================================================


    function isVisible(el) {
        try {
            var r = el.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) return false;
            var s = window.getComputedStyle(el);
            if (s.display === 'none' || s.visibility === 'hidden') return false;
            if (r.left < -100 || r.left > window.innerWidth + 100) return false;
            return true;
        } catch (e) { return false; }
    }

    function findCheckoutBtn() {
        for (var i = 0; i < BTN_SELECTORS.length; i++) {
            try {
                var els = document.querySelectorAll(BTN_SELECTORS[i]);
                for (var j = 0; j < els.length; j++) {
                    if (isVisible(els[j])) return els[j];
                }
            } catch (e) {}
        }
        // Fallback: any submit inside a cart form
        try {
            var forms = document.querySelectorAll('form[action*="cart"], form[action*="checkout"]');
            for (var fi = 0; fi < forms.length; fi++) {
                var btns = forms[fi].querySelectorAll('button[type="submit"],input[type="submit"]');
                for (var bi = 0; bi < btns.length; bi++) {
                    if (isVisible(btns[bi])) return btns[bi];
                }
            }
        } catch (e) {}
        return null;
    }



    function tryInlineInject() {
        if (inlineInjected && document.getElementById('sp-inline')) return;

        var btn = findCheckoutBtn();
        if (!btn) return;

        // Don't duplicate
        if (document.getElementById('sp-inline')) {
            inlineInjected = true;
            renderInlineWidget();
            return;
        }

        // Create inline container and insert BEFORE checkout button
        var container = document.createElement('div');
        container.id = 'sp-inline';

        var parent = btn.parentElement;
        if (parent) {
            parent.insertBefore(container, btn);
        } else {
            (document.body || document.documentElement).appendChild(container);
        }

        inlineInjected = true;
        renderInlineWidget();

        console.log('[SkyPoint] Inline widget injected above checkout button');
    }

    function renderInlineWidget() {
        var c = document.getElementById('sp-inline');
        if (!c) return;

        if (currentPudo) {
            c.innerHTML =
                '<div class="sp-badge">📍 PUDO COUNTER SELECTED</div>' +
                '<div style="display:flex;align-items:flex-start;justify-content:space-between;gap:10px;flex-wrap:wrap;">' +
                  '<div>' +
                    '<div style="font-weight:700;font-size:15px;color:#0f172a;">' + esc(currentPudo.name) +
                      ' <span style="color:#94a3b8;font-weight:400;font-size:13px;">(' + esc(currentPudo.code) + ')</span></div>' +
                    '<div style="font-size:12px;color:#475569;margin-top:3px;">' + esc(currentPudo.addr1) + ', ' + esc(currentPudo.city) + ' ' + esc(currentPudo.pcode) + '</div>' +
                  '</div>' +
                  '<div style="display:flex;gap:8px;flex-shrink:0;">' +
                    '<button type="button" class="sp-btn" id="sp-inline-select">✏️ Change</button>' +
                    '<button type="button" class="sp-btn-clear" id="sp-inline-clear">Clear</button>' +
                  '</div>' +
                '</div>';
            var clearBtn = document.getElementById('sp-inline-clear');
            if (clearBtn) clearBtn.onclick = clearPudo;
        } else {
            c.innerHTML =
                '<div style="font-weight:700;font-size:14px;color:#1e293b;margin-bottom:4px;">📦 Collect from a PUDO Counter?</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:12px;line-height:1.4;">Save on shipping — choose a nearby pickup point instead of home delivery.</div>' +
                '<button type="button" class="sp-btn" id="sp-inline-select">🗺️ Select PUDO Counter</button>';
        }

        var selectBtn = document.getElementById('sp-inline-select');
        if (selectBtn) selectBtn.onclick = launchSelector;
    }

    // Poll to inject inline widget
    var pollCount = 0;
    var pollTimer = setInterval(function () {
        pollCount++;
        if (pollCount > 30) { clearInterval(pollTimer); return; } // stop after 15s
        if (!isCartPage) { clearInterval(pollTimer); return; }
        tryInlineInject();
        if (inlineInjected) clearInterval(pollTimer);
    }, 500);

    // MutationObserver for dynamic cart updates
    if (window.MutationObserver) {
        var obs = new MutationObserver(function () {
            if (isCartPage && !inlineInjected) tryInlineInject();
            // Refresh floating widget state
            var fab = document.getElementById('sp-float');
            if (fab) renderFloatingWidget(fab);
        });
        domReady(function () {
            obs.observe(document.documentElement, { childList: true, subtree: true });
        });
    }

    // =========================================================================
    // CHECKOUT INTERCEPTION
    // =========================================================================
    function interceptCheckout() {
        document.addEventListener('click', function (e) {
            var target = e.target;
            for (var i = 0; i < 4 && target; i++) {
                for (var k = 0; k < BTN_SELECTORS.length; k++) {
                    try {
                        if (target.matches && target.matches(BTN_SELECTORS[k])) {
                            e.preventDefault();
                            e.stopImmediatePropagation();

                            if (!currentPudo) {
                                pulseFloat();
                                showBlockedToast();
                                return;
                            }

                            // Redirect to checkout with prefilled PUDO address parameters
                            var checkoutUrl = rootPath + 'checkout'
                                + '?checkout[shipping_address][company]='  + encodeURIComponent(currentPudo.name + ' (' + currentPudo.code + ')')
                                + '&checkout[shipping_address][address1]=' + encodeURIComponent(currentPudo.addr1)
                                + '&checkout[shipping_address][address2]=' + encodeURIComponent(currentPudo.addr2 || '')
                                + '&checkout[shipping_address][city]='     + encodeURIComponent(currentPudo.city)
                                + '&checkout[shipping_address][zip]='      + encodeURIComponent(currentPudo.pcode)
                                + '&checkout[shipping_address][country]='  + encodeURIComponent('South Africa');
                            
                            console.log('[SkyPoint] Redirecting to checkout with PUDO address:', checkoutUrl);
                            window.location.href = checkoutUrl;
                            return;
                        }
                    } catch (e2) {}
                }
                target = target.parentElement;
            }
        }, true);

        document.addEventListener('submit', function (e) {
            var form = e.target;
            if (form && form.action &&
                (form.action.indexOf('/cart') !== -1 || form.action.indexOf('/checkout') !== -1)) {
                
                e.preventDefault();
                e.stopImmediatePropagation();

                if (!currentPudo) {
                    pulseFloat();
                    showBlockedToast();
                    return;
                }

                // Redirect to checkout with prefilled PUDO address parameters
                var checkoutUrl = rootPath + 'checkout'
                    + '?checkout[shipping_address][company]='  + encodeURIComponent(currentPudo.name + ' (' + currentPudo.code + ')')
                    + '&checkout[shipping_address][address1]=' + encodeURIComponent(currentPudo.addr1)
                    + '&checkout[shipping_address][address2]=' + encodeURIComponent(currentPudo.addr2 || '')
                    + '&checkout[shipping_address][city]='     + encodeURIComponent(currentPudo.city)
                    + '&checkout[shipping_address][zip]='      + encodeURIComponent(currentPudo.pcode)
                    + '&checkout[shipping_address][country]='  + encodeURIComponent('South Africa');
                
                console.log('[SkyPoint] Redirecting to checkout with PUDO address:', checkoutUrl);
                window.location.href = checkoutUrl;
            }
        }, true);

        // Global delegation for PUDO buttons to withstand dynamic theme re-renders
        document.addEventListener('click', function (e) {
            var target = e.target;
            while (target && target !== document.documentElement) {
                if (target.id === 'sp-inline-select' || target.id === 'sp-float-btn') {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    launchSelector();
                    return;
                }
                if (target.id === 'sp-inline-clear') {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    clearPudo();
                    return;
                }
                target = target.parentElement;
            }
        }, true);
    }

    function pulseFloat() {
        var fab = document.getElementById('sp-float');
        if (!fab) return;
        fab.style.transform = 'scale(1.05)';
        fab.style.transition = 'transform .15s';
        setTimeout(function () { fab.style.transform = ''; }, 400);
    }

    function showBlockedToast() {
        if (document.getElementById('sp-toast')) return;
        var t = document.createElement('div');
        t.id = 'sp-toast';
        t.innerHTML = '<span style="font-size:17px">📍</span> Please <strong>select a PUDO counter</strong> before checking out. <button onclick="this.parentElement.remove()" style="background:none;border:none;color:#fff;font-size:15px;cursor:pointer;margin-left:10px;padding:0">&#x2715;</button>';
        Object.assign(t.style, {
            position: 'fixed', bottom: '20px', left: '50%', transform: 'translateX(-50%)',
            background: '#ff1e27', color: '#fff', padding: '13px 20px', borderRadius: '10px',
            display: 'flex', alignItems: 'center', gap: '8px', zIndex: '2147483646',
            fontSize: '14px', fontFamily: 'Outfit,Inter,system-ui,sans-serif',
            boxShadow: '0 4px 20px rgba(0,0,0,.3)', maxWidth: '88vw'
        });
        document.body.appendChild(t);
        setTimeout(function () { if (t.parentNode) t.remove(); }, 5000);
    }

    // =========================================================================
    // CHECKOUT PAGE BANNER
    // =========================================================================
    function startCheckoutMode() {
        var render = function () {
            if (document.getElementById('sp-checkout-banner')) return;
            var b = document.createElement('div');
            b.id = 'sp-checkout-banner';
            if (currentPudo) {
                b.innerHTML = '✅ <strong>PUDO Counter:</strong> ' + esc(currentPudo.name) + ' (' + esc(currentPudo.code) + ') — ' + esc(currentPudo.city);
                b.style.background = '#16a34a';
            } else {
                b.innerHTML = '📍 No PUDO counter selected. <a href="/cart" style="color:#fff;font-weight:700;text-decoration:underline">Back to cart</a> to choose one.';
                b.style.background = '#ff1e27';
            }
            Object.assign(b.style, {
                position: 'fixed', top: '0', left: '0', right: '0', color: '#fff',
                padding: '11px 16px', zIndex: '99999', fontSize: '14px',
                fontFamily: 'Outfit,Inter,system-ui,sans-serif',
                display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                boxShadow: '0 2px 8px rgba(0,0,0,.2)'
            });
            document.body.prepend(b);
        };
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', render);
        } else {
            render();
        }
    }

    // =========================================================================
    // STYLES
    // =========================================================================
    function injectStyles() {
        if (document.getElementById('sp-styles')) return;
        var s = document.createElement('style');
        s.id = 'sp-styles';
        s.textContent = [
            '#sp-inline {',
            '  padding:16px !important;',
            '  border:2px solid #e2e8f0 !important;',
            '  border-radius:10px !important;',
            '  background:#fffafa !important;',
            '  font-family:Outfit,Inter,system-ui,sans-serif !important;',
            '  box-sizing:border-box !important;',
            '  margin-bottom:12px !important;',
            '  color:#1e293b !important;',
            '  box-shadow:0 2px 12px rgba(0,0,0,0.08) !important;',
            '}',
            '.sp-badge {',
            '  display:inline-block !important;',
            '  background:#fef2f2 !important; color:#dc2626 !important;',
            '  font-size:11px !important; font-weight:700 !important;',
            '  padding:2px 9px !important; border-radius:999px !important;',
            '  letter-spacing:.04em !important; margin-bottom:10px !important;',
            '}',
            '.sp-btn {',
            '  background:#ff1e27 !important; color:#fff !important;',
            '  border:none !important; border-radius:6px !important;',
            '  padding:10px 16px !important; font-weight:700 !important;',
            '  font-size:13px !important; cursor:pointer !important;',
            '  font-family:inherit !important;',
            '  transition:background .2s !important; white-space:nowrap !important;',
            '}',
            '.sp-btn:hover { background:#cc1219 !important; }',
            '.sp-btn-clear {',
            '  background:none !important; border:none !important;',
            '  color:#94a3b8 !important; font-size:12px !important;',
            '  cursor:pointer !important; text-decoration:underline !important;',
            '  padding:0 !important; font-family:inherit !important;',
            '}'
        ].join('\n');
        (document.head || document.documentElement).appendChild(s);
    }

    // =========================================================================
    // PUDO SELECTOR POPUP
    // =========================================================================
    function launchSelector() {
        // Open a blank popup synchronously to bypass browser popup blocker
        var W = 820, H = 660;
        var popup = window.open('about:blank', 'SkyPointPudo',
            'width=' + W + ',height=' + H +
            ',left=' + Math.round((screen.width - W) / 2) +
            ',top=' + Math.round((screen.height - H) / 2) +
            ',resizable=yes,scrollbars=yes');

        if (!popup) {
            alert('Popup was blocked! Please allow popups for this site and try again.');
            return;
        }

        try {
            var doc = popup.document;
            doc.open();
            doc.write('<html><head><title>Loading PUDO Selector...</title>' +
                '<style>body{font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;background:#f8fafc;color:#64748b;}</style>' +
                '</head><body><div>' +
                '<h3 style="margin:0 0 8px 0;color:#0f172a;">Loading PUDO Selector...</h3>' +
                '<p style="margin:0;font-size:14px;">Connecting to SkyPoint shipping services...</p>' +
                '</div></body></html>');
            doc.close();
        } catch (e) {
            // Ignore cross-origin issues
        }

        var apiUrl = backendUrl + '/api/pudo/widget-url'
            + '?shop='    + encodeURIComponent(shopDomain)
            + '&address=' + encodeURIComponent('South Africa')
            + '&domain='  + encodeURIComponent(window.location.origin);

        console.log('[SkyPoint] Fetching widget URL from:', apiUrl);

        makeRequest('GET', apiUrl, null, function (err, resp) {
            if (err) {
                console.error('[SkyPoint] Could not get widget URL:', err);
                try { popup.close(); } catch (e) {}
                alert('Could not load PUDO selector. Please try again.\n\nError: ' + err.message);
                return;
            }
            if (!resp.success || !resp.widget_url) {
                console.error('[SkyPoint] Invalid widget URL response');
                try { popup.close(); } catch (e) {}
                alert('Could not load PUDO selector. Invalid response from server.');
                return;
            }
            openPudoPopup(resp.guid, resp.widget_url, popup);
        });
    }

    function openPudoPopup(guid, url, popup) {
        if (!popup || popup.closed) {
            var W = 820, H = 660;
            popup = window.open(url, 'SkyPointPudo',
                'width=' + W + ',height=' + H +
                ',left=' + Math.round((screen.width - W) / 2) +
                ',top=' + Math.round((screen.height - H) / 2) +
                ',resizable=yes,scrollbars=yes');
            if (!popup) {
                alert('Popup was blocked! Please allow popups for this site and try again.');
                return;
            }
        } else {
            try {
                popup.location.href = url;
            } catch (e) {
                var W = 820, H = 660;
                popup = window.open(url, 'SkyPointPudo',
                    'width=' + W + ',height=' + H +
                    ',left=' + Math.round((screen.width - W) / 2) +
                    ',top=' + Math.round((screen.height - H) / 2) +
                    ',resizable=yes,scrollbars=yes');
            }
        }
        try {
            popup.focus();
        } catch (e) {}

        function onMessage(event) {
            var data = event.data;
            if (typeof data === 'string') { try { data = JSON.parse(data); } catch (e) {} }
            if (!data || data.result !== 'success') return;

            try { popup.close(); } catch (e) {}
            window.removeEventListener('message', onMessage);
            console.log('[SkyPoint] Selection received, GUID:', guid);

            var detailsUrl = backendUrl + '/api/pudo/selected/' + encodeURIComponent(guid) + '?shop=' + encodeURIComponent(shopDomain);
            makeRequest('GET', detailsUrl, null, function (err, json) {
                if (err) {
                    console.error('[SkyPoint] Error fetching PUDO details:', err);
                    alert('Could not connect to PUDO server. Please try again.');
                    return;
                }
                if (json.success && json.pudo_point) {
                    savePudo(json.pudo_point);
                } else {
                    alert('Could not retrieve PUDO point details. Please try again.');
                }
            });
        }
        window.addEventListener('message', onMessage);
    }

    // =========================================================================
    // SAVE / CLEAR
    // =========================================================================
    function savePudo(point) {
        var updateUrl = normalizedRoot + '/cart/update.js';
        var data = {
            attributes: {
                pudo_code:     point.code     || '',
                pudo_name:     point.name     || '',
                pudo_addr1:    point.addr1    || '',
                pudo_addr2:    point.addr2    || '',
                pudo_city:     point.city     || '',
                pudo_zip:      point.pcode    || '',
                pudo_provider: point.provider || ''
            }
        };

        makeRequest('POST', updateUrl, data, function (err, cart) {
            if (err) {
                console.error('[SkyPoint] Failed to save cart attributes:', err);
                alert('Failed to save PUDO selection. Please try again.');
                return;
            }
            currentPudo = point;
            renderInlineWidget();
            var fab = document.getElementById('sp-float');
            if (fab) renderFloatingWidget(fab);
            console.log('[SkyPoint] Cart attributes saved.');
        });
    }

    function clearPudo() {
        var updateUrl = normalizedRoot + '/cart/update.js';
        var data = {
            attributes: {
                pudo_code: '', pudo_name: '', pudo_addr1: '',
                pudo_addr2: '', pudo_city: '', pudo_zip: '', pudo_provider: ''
            }
        };

        makeRequest('POST', updateUrl, data, function (err, cart) {
            if (err) {
                console.error('[SkyPoint] Clear failed:', err);
                return;
            }
            currentPudo = null;
            renderInlineWidget();
            var fab = document.getElementById('sp-float');
            if (fab) renderFloatingWidget(fab);
        });
    }

    function esc(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

})();
