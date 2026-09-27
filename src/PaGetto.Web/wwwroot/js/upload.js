(function () {
    'use strict';

    // Reads the .nuspec of a .nupkg/.snupkg in the browser, so the upload page can show a
    // preview before anything is sent. Only the zip's central directory and the nuspec entry
    // are read (File.slice), so large packages never load into memory. The server validates
    // the package again on upload; this preview is only a convenience.

    var EOCD_SIGNATURE = 0x06054b50;
    var ZIP64_LOCATOR_SIGNATURE = 0x07064b50;
    var ZIP64_EOCD_SIGNATURE = 0x06064b50;
    var CENTRAL_SIGNATURE = 0x02014b50;
    var LOCAL_SIGNATURE = 0x04034b50;
    var MAX_32 = 0xffffffff;

    async function readBytes(file, start, end) {
        return new DataView(await file.slice(start, end).arrayBuffer());
    }

    function readUint64(view, offset) {
        return Number(view.getBigUint64(offset, true));
    }

    async function findCentralDirectory(file) {
        // The end-of-central-directory record is 22 bytes plus a comment of up to 64 KiB.
        var tailStart = Math.max(0, file.size - 22 - 0xffff);
        var tail = await readBytes(file, tailStart, file.size);
        for (var i = tail.byteLength - 22; i >= 0; i--) {
            if (tail.getUint32(i, true) !== EOCD_SIGNATURE) continue;

            var directory = {
                count: tail.getUint16(i + 10, true),
                size: tail.getUint32(i + 12, true),
                offset: tail.getUint32(i + 16, true),
            };

            if (directory.offset === MAX_32 || directory.size === MAX_32 || directory.count === 0xffff) {
                var locatorPos = tailStart + i - 20;
                var locator = await readBytes(file, locatorPos, locatorPos + 20);
                if (locator.getUint32(0, true) !== ZIP64_LOCATOR_SIGNATURE) throw new Error('bad zip64 locator');

                var recordPos = readUint64(locator, 8);
                var record = await readBytes(file, recordPos, recordPos + 56);
                if (record.getUint32(0, true) !== ZIP64_EOCD_SIGNATURE) throw new Error('bad zip64 record');

                directory.count = readUint64(record, 32);
                directory.size = readUint64(record, 40);
                directory.offset = readUint64(record, 48);
            }

            return directory;
        }

        throw new Error('not a zip file');
    }

    // Zip64 extra fields hold, in order, only the values that are 0xFFFFFFFF in the header.
    function applyZip64Extra(view, start, length, entry) {
        var pos = start;
        var end = start + length;
        while (pos + 4 <= end) {
            var id = view.getUint16(pos, true);
            var size = view.getUint16(pos + 2, true);
            if (id === 0x0001) {
                var field = pos + 4;
                if (entry.uncompressedSize === MAX_32) { entry.uncompressedSize = readUint64(view, field); field += 8; }
                if (entry.compressedSize === MAX_32) { entry.compressedSize = readUint64(view, field); field += 8; }
                if (entry.localOffset === MAX_32) { entry.localOffset = readUint64(view, field); }
                return;
            }
            pos += 4 + size;
        }
    }

    async function findNuspecEntry(file) {
        var directory = await findCentralDirectory(file);
        var view = await readBytes(file, directory.offset, directory.offset + directory.size);
        var decoder = new TextDecoder();
        var pos = 0;

        for (var i = 0; i < directory.count && pos + 46 <= view.byteLength; i++) {
            if (view.getUint32(pos, true) !== CENTRAL_SIGNATURE) throw new Error('bad central directory');

            var nameLength = view.getUint16(pos + 28, true);
            var extraLength = view.getUint16(pos + 30, true);
            var commentLength = view.getUint16(pos + 32, true);
            var name = decoder.decode(new Uint8Array(view.buffer, view.byteOffset + pos + 46, nameLength));

            // The nuspec is the only .nuspec file at the root of the package.
            if (name.indexOf('/') === -1 && /\.nuspec$/i.test(name)) {
                var entry = {
                    method: view.getUint16(pos + 10, true),
                    compressedSize: view.getUint32(pos + 20, true),
                    uncompressedSize: view.getUint32(pos + 24, true),
                    localOffset: view.getUint32(pos + 42, true),
                };
                applyZip64Extra(view, pos + 46 + nameLength, extraLength, entry);
                return entry;
            }

            pos += 46 + nameLength + extraLength + commentLength;
        }

        throw new Error('no nuspec');
    }

    async function readNuspecText(file) {
        var entry = await findNuspecEntry(file);
        var header = await readBytes(file, entry.localOffset, entry.localOffset + 30);
        if (header.getUint32(0, true) !== LOCAL_SIGNATURE) throw new Error('bad local header');

        var dataStart = entry.localOffset + 30 + header.getUint16(26, true) + header.getUint16(28, true);
        var data = file.slice(dataStart, dataStart + entry.compressedSize);

        if (entry.method === 0) return await data.text();
        if (entry.method !== 8) throw new Error('unsupported compression');

        var stream = data.stream().pipeThrough(new DecompressionStream('deflate-raw'));
        return await new Response(stream).text();
    }

    function child(parent, name) {
        if (!parent) return null;
        for (var i = 0; i < parent.children.length; i++) {
            if (parent.children[i].localName === name) return parent.children[i];
        }
        return null;
    }

    function children(parent, name) {
        var result = [];
        if (!parent) return result;
        for (var i = 0; i < parent.children.length; i++) {
            if (parent.children[i].localName === name) result.push(parent.children[i]);
        }
        return result;
    }

    function text(parent, name) {
        var element = child(parent, name);
        return element ? element.textContent.trim() : '';
    }

    function parseNuspec(xml) {
        var doc = new DOMParser().parseFromString(xml, 'application/xml');
        if (doc.getElementsByTagName('parsererror').length > 0) throw new Error('bad nuspec');

        var metadata = child(doc.documentElement, 'metadata');
        if (!metadata) throw new Error('bad nuspec');

        var dependencies = child(metadata, 'dependencies');
        var groups = children(dependencies, 'group').map(function (group) {
            return {
                framework: group.getAttribute('targetFramework') || 'Any framework',
                items: children(group, 'dependency').map(dependency),
            };
        });

        // Old nuspecs list dependencies without groups.
        var flat = children(dependencies, 'dependency');
        if (flat.length > 0) groups.push({ framework: 'Any framework', items: flat.map(dependency) });

        var license = child(metadata, 'license');

        return {
            id: text(metadata, 'id'),
            version: text(metadata, 'version'),
            authors: text(metadata, 'authors'),
            description: text(metadata, 'description'),
            license: license ? license.textContent.trim() : text(metadata, 'licenseUrl'),
            projectUrl: text(metadata, 'projectUrl'),
            tags: text(metadata, 'tags'),
            dependencyGroups: groups,
        };
    }

    function dependency(element) {
        return { id: element.getAttribute('id'), range: element.getAttribute('version') || '' };
    }

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
        return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
    }

    // Sends one file with XMLHttpRequest, which (unlike fetch) reports upload progress.
    function send(url, file, headers, onProgress) {
        return new Promise(function (resolve) {
            var request = new XMLHttpRequest();
            request.open('POST', url);
            Object.keys(headers).forEach(function (name) {
                if (headers[name]) request.setRequestHeader(name, headers[name]);
            });
            request.upload.onprogress = function (e) {
                if (e.lengthComputable) onProgress(Math.round(e.loaded * 100 / e.total));
            };
            request.onload = function () {
                var body = null;
                try { body = JSON.parse(request.responseText); } catch (e) { }
                resolve({ status: request.status, body: body });
            };
            request.onerror = function () { resolve({ status: 0, body: null }); };

            var form = new FormData();
            form.append('package', file, file.name);
            request.send(form);
        });
    }

    // Alpine component of the "Upload from browser" card. The config comes from data
    // attributes on the card: handler URLs and the feed's size limit.
    window.pagettoUploader = function (element) {
        var config = element.dataset;
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        var token = tokenInput ? tokenInput.value : '';
        var maxBytes = Number(config.maxBytes);
        var nextKey = 1;

        return {
            items: [],
            apiKey: '',
            dragging: false,
            busy: false,
            apiKeyRequired: config.apiKeyRequired === 'true',

            get canUpload() {
                return !this.busy
                    && (!this.apiKeyRequired || this.apiKey.trim() !== '')
                    && this.items.some(function (item) { return item.state === 'ready'; });
            },

            pick: function (event) {
                this.add(event.target.files);
                event.target.value = '';
            },

            drop: function (event) {
                this.dragging = false;
                this.add(event.dataTransfer.files);
            },

            add: function (files) {
                for (var i = 0; i < files.length; i++) {
                    var file = files[i];
                    var kind = /\.snupkg$/i.test(file.name) ? 'symbol' : /\.nupkg$/i.test(file.name) ? 'package' : null;
                    this.items.push({
                        key: nextKey++,
                        file: file,
                        name: file.name,
                        size: formatSize(file.size),
                        kind: kind,
                        state: 'reading',
                        note: '',
                        noteLevel: '',
                        metadata: null,
                        progress: 0,
                        url: null,
                    });

                    // Only changes made through Alpine's proxy of the item re-render the list.
                    var item = this.items[this.items.length - 1];
                    if (!kind) {
                        this.fail(item, 'Only .nupkg and .snupkg files can be uploaded.');
                    } else if (file.size > maxBytes) {
                        this.fail(item, 'The file is larger than this feed\'s ' + config.maxSize + ' limit.');
                    } else {
                        this.preview(item);
                    }
                }
            },

            preview: async function (item) {
                try {
                    item.metadata = parseNuspec(await readNuspecText(item.file));
                } catch (e) {
                    this.fail(item, item.kind === 'symbol'
                        ? 'The file is not a valid symbol package.'
                        : 'The file is not a valid package: it has no readable .nuspec.');
                    return;
                }

                if (!item.metadata.id || !item.metadata.version) {
                    this.fail(item, 'The .nuspec has no id or version.');
                    return;
                }

                await this.check(item);
            },

            check: async function (item) {
                var url = config.checkUrl + (config.checkUrl.indexOf('?') === -1 ? '?' : '&')
                    + 'id=' + encodeURIComponent(item.metadata.id)
                    + '&version=' + encodeURIComponent(item.metadata.version);

                var result = null;
                try {
                    var response = await fetch(url, { headers: { 'Accept': 'application/json' } });
                    if (response.ok) result = await response.json();
                } catch (e) { }

                item.state = 'ready';
                if (!result) return;

                if (item.kind === 'package' && result.exists) {
                    if (result.canOverwrite) {
                        this.note(item, 'warn', 'This version already exists and will be replaced.');
                    } else {
                        this.fail(item, 'This version already exists in the feed.');
                    }
                } else if (item.kind === 'symbol' && !result.exists && !this.hasPackageFor(item)) {
                    this.note(item, 'warn', 'The package for these symbols isn\'t in the feed yet. Upload the .nupkg first.');
                }
            },

            hasPackageFor: function (symbolItem) {
                var id = symbolItem.metadata.id.toLowerCase();
                var version = symbolItem.metadata.version.toLowerCase();
                return this.items.some(function (item) {
                    return item.kind === 'package' && item.metadata && item.state !== 'failed'
                        && item.metadata.id.toLowerCase() === id && item.metadata.version.toLowerCase() === version;
                });
            },

            fail: function (item, message) {
                item.state = 'failed';
                this.note(item, 'danger', message);
            },

            note: function (item, level, message) {
                item.note = message;
                item.noteLevel = level;
            },

            remove: function (item) {
                this.items = this.items.filter(function (other) { return other !== item; });
            },

            clearFinished: function () {
                this.items = this.items.filter(function (item) {
                    return item.state !== 'published' && item.state !== 'failed';
                });
            },

            get hasFinished() {
                return this.items.some(function (item) { return item.state === 'published' || item.state === 'failed'; });
            },

            upload: async function () {
                this.busy = true;

                // Packages go first, so symbols uploaded in the same batch find their package.
                var queue = this.items
                    .filter(function (item) { return item.state === 'ready'; })
                    .sort(function (a, b) { return (a.kind === 'symbol') - (b.kind === 'symbol'); });

                for (var i = 0; i < queue.length; i++) {
                    var item = queue[i];
                    item.state = 'uploading';
                    item.progress = 0;

                    var url = item.kind === 'symbol' ? config.symbolUrl : config.packageUrl;
                    var result = await send(url, item.file, {
                        'RequestVerificationToken': token,
                        'X-NuGet-ApiKey': this.apiKeyRequired ? this.apiKey.trim() : '',
                    }, function (percent) { item.progress = percent; });

                    if (result.status === 201) {
                        item.state = 'published';
                        item.url = result.body && result.body.url;
                        this.note(item, 'ok', 'Published.');
                    } else if (result.body && result.body.message) {
                        this.fail(item, result.body.message);
                    } else if (result.status === 413) {
                        this.fail(item, 'The file is larger than the server accepts.');
                    } else if (result.status === 400) {
                        // Antiforgery failures return an empty 400; the session probably expired.
                        this.fail(item, 'The upload was rejected. Reload the page and try again.');
                    } else {
                        this.fail(item, 'The upload failed' + (result.status ? ' (HTTP ' + result.status + ').' : ': the server could not be reached.'));
                    }

                    // A wrong API key fails every file the same way; stop and let the user fix it.
                    if (result.status === 401) break;
                }

                this.busy = false;
            },
        };
    };
})();
