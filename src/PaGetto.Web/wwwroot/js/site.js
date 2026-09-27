(function () {
    'use strict';

    var pagetto = {};
    window.pagetto = pagetto;

    function store(key, value) {
        try { localStorage.setItem(key, value); } catch (e) { }
    }

    // Filter the items of a scrollable tag dropdown as the user types. Items carry their
    // value in a data-tag attribute; the "Any" reset item has none and always stays visible.
    pagetto.filterTags = function (input) {
        var query = input.value.trim().toLowerCase();
        var menu = input.closest('.dropdown-menu');
        if (!menu) return;

        var items = menu.querySelectorAll('li[data-tag]');
        for (var i = 0; i < items.length; i++) {
            var tag = items[i].getAttribute('data-tag').toLowerCase();
            items[i].style.display = (query === '' || tag.indexOf(query) !== -1) ? '' : 'none';
        }
    };

    function fallbackCopy(text) {
        var textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.setAttribute('readonly', '');
        textArea.style.position = 'fixed';
        textArea.style.top = '0';
        textArea.style.left = '0';
        textArea.style.opacity = '0';
        document.body.appendChild(textArea);
        textArea.select();
        try { document.execCommand('copy'); } catch (e) { }
        document.body.removeChild(textArea);
    }

    // Copies text and shows "Copied" on the button for a moment. The label is the button's
    // .bgt-copy-label element, or the whole button when it has none.
    pagetto.copy = function (text, button) {
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).catch(function () { fallbackCopy(text); });
        } else {
            fallbackCopy(text);
        }

        if (!button) return;
        var label = button.querySelector('.bgt-copy-label');
        if (label && !label.hasAttribute('data-label')) label.setAttribute('data-label', label.textContent);
        if (label) label.textContent = 'Copied';
        button.classList.add('bgt-copied');
        clearTimeout(button._copyTimer);
        button._copyTimer = setTimeout(function () {
            if (label) label.textContent = label.getAttribute('data-label');
            button.classList.remove('bgt-copied');
        }, 1400);
    };

    // Kept for existing callers.
    pagetto.copyTextToClipboard = function (text) {
        pagetto.copy(text);
    };

    document.addEventListener('click', function (event) {
        if (!event.target.closest) return;

        // Buttons with data-copy copy that text; data-copy-target copies the text of an element.
        var copyButton = event.target.closest('[data-copy], [data-copy-target]');
        if (copyButton) {
            event.preventDefault();
            event.stopPropagation();
            var text = copyButton.getAttribute('data-copy');
            if (text === null) {
                var target = document.querySelector(copyButton.getAttribute('data-copy-target'));
                text = target ? target.textContent : '';
            }
            pagetto.copy(text, copyButton);
            return;
        }

        // Switch between the light and the dark theme. The inline script in _Layout applies
        // the saved choice before the page renders.
        var toggle = event.target.closest('[data-theme-toggle]');
        if (toggle) {
            var theme = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-bs-theme', theme);
            store('pagetto-theme', theme);
            updateThemeToggles();
            return;
        }

        // List or grid view of the package list, remembered per browser.
        var viewButton = event.target.closest('[data-view]');
        if (viewButton) {
            var grid = viewButton.getAttribute('data-view') === 'grid';
            document.documentElement.classList.toggle('bgt-view-grid', grid);
            store('pagetto-view', grid ? 'grid' : 'list');
        }
    });

    function updateThemeToggles() {
        var dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
        var label = dark ? 'Switch to light mode' : 'Switch to dark mode';
        var toggles = document.querySelectorAll('[data-theme-toggle]');
        for (var i = 0; i < toggles.length; i++) {
            toggles[i].setAttribute('title', label);
            toggles[i].setAttribute('aria-label', label);
        }
    }

    updateThemeToggles();

    // "/" focuses the package search, unless the user is typing somewhere. On pages without the
    // search field it opens the package list, which then focuses its search field.
    var focusSearchKey = 'pagetto-focus-search';

    document.addEventListener('keydown', function (event) {
        if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
        var active = document.activeElement;
        if (active && (active.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName))) return;
        if (document.querySelector('.modal.show')) return;

        var search = document.querySelector('[data-search-input]');
        if (search) {
            event.preventDefault();
            search.focus();
            search.select();
            return;
        }

        // Set by _Layout. Only a path on this site is followed, never a scheme or another host.
        var packagesUrl = window.pagettoPackagesUrl;
        if (typeof packagesUrl !== 'string' || !/^\/(?![\/\\])/.test(packagesUrl)) return;
        event.preventDefault();
        try { sessionStorage.setItem(focusSearchKey, '1'); } catch (e) { }
        window.location.href = packagesUrl;
    });

    (function () {
        var wanted = false;
        try {
            wanted = sessionStorage.getItem(focusSearchKey) === '1';
            sessionStorage.removeItem(focusSearchKey);
        } catch (e) { }
        var search = wanted && document.querySelector('[data-search-input]');
        if (search) search.focus();
    })();

    // Ask for confirmation before submitting a form with a data-confirm attribute. The text
    // comes from an HTML attribute, never from JavaScript built on the server, so usernames,
    // slugs and package ids with quotes are shown literally and can't inject code.
    document.addEventListener('submit', function (event) {
        var form = event.target;
        var message = form.getAttribute && form.getAttribute('data-confirm');
        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });

    // A modal opened from a dropdown item returns the focus to the dropdown's button when it
    // closes: the item itself is hidden by then. Bootstrap already handles other triggers.
    document.addEventListener('show.bs.modal', function (event) {
        var trigger = event.relatedTarget;
        var dropdown = trigger && trigger.closest && trigger.closest('.dropdown');
        var toggle = dropdown && dropdown.querySelector('[data-bs-toggle="dropdown"]');
        if (!toggle) return;
        event.target.addEventListener('hidden.bs.modal', function () { toggle.focus(); }, { once: true });
    });

    // Esc closes the mobile menu and puts the focus back on the menu button.
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        var drawer = document.getElementById('bgt-drawer');
        if (!drawer || !drawer.classList.contains('show') || !window.bootstrap) return;
        bootstrap.Collapse.getOrCreateInstance(drawer, { toggle: false }).hide();
        var button = document.querySelector('[data-bs-target="#bgt-drawer"]');
        if (button) button.focus();
    });

    // A tab row that scrolls sideways fades out on the right while more tabs are hidden there.
    // Rows filled by Alpine get their tabs after this runs, so they are watched for changes.
    function updateTabFade(row) {
        var hiddenRight = row.scrollWidth - row.clientWidth - row.scrollLeft > 1;
        row.classList.toggle('bgt-fade', hiddenRight);
    }

    var tabRows = document.querySelectorAll('.bgt-tabs');
    var resizeObserver = window.ResizeObserver && new ResizeObserver(function (entries) {
        entries.forEach(function (entry) { updateTabFade(entry.target); });
    });
    for (var r = 0; r < tabRows.length; r++) {
        (function (row) {
            row.addEventListener('scroll', function () { updateTabFade(row); }, { passive: true });
            new MutationObserver(function () { updateTabFade(row); }).observe(row, { childList: true });
            if (resizeObserver) resizeObserver.observe(row);
            updateTabFade(row);
        })(tabRows[r]);
    }

    // Toasts hide themselves after a moment.
    var toasts = document.querySelectorAll('.bgt-toast');
    for (var t = 0; t < toasts.length; t++) {
        (function (toast) {
            setTimeout(function () {
                toast.classList.add('hide');
                setTimeout(function () { toast.remove(); }, 400);
            }, 2600);
        })(toasts[t]);
    }
})();
