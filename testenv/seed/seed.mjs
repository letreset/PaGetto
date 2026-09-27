// Builds the test packages and fills an empty PaGetto server with the test data set.
//
// Usage (see testenv/README.md): start an empty server, then run
//   node testenv/seed/seed.mjs [--url http://localhost:5000] [--packages-only]
//
// Needs Node 18+ and the .NET SDK (for `dotnet pack`). No npm packages.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const templateDir = join(here, 'template');
const packagesDir = resolve(here, '..', 'packages');

const args = process.argv.slice(2);
const argValue = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const baseUrl = (argValue('--url') ?? process.env.PAGETTO_URL ?? 'http://localhost:5000').replace(/\/$/, '');
const packagesOnly = args.includes('--packages-only');

// Test accounts. These are test-only credentials, documented in docs/docs/Advanced/test-environment.md.
const admin = { username: 'admin', password: 'Admin-Test-Password-1' };
const defaultAdminPassword = 'admin';
const userPassword = 'User-Test-Password-1';
const users = [
    { username: 'alice', displayName: 'Alice Martin', email: 'alice@example.com', canLoginToUI: true },
    { username: 'bob', displayName: 'Bob Chen', email: 'bob@example.com', canLoginToUI: true },
    { username: 'carol', displayName: 'Carol Diaz', email: 'carol@example.com', canLoginToUI: true },
    { username: 'build-agent', displayName: 'Build agent', email: '', canLoginToUI: false },
];

const feeds = [
    { slug: 'internal', name: 'Internal', description: 'Packages built by our own teams' },
    { slug: 'experimental', name: 'Experimental', description: 'Previews and spikes' },
    { slug: 'archive', name: 'Archive', description: 'Frozen packages, read-only' },
];

const groups = [
    {
        name: 'Developers', description: 'Everyone who writes code', members: ['alice', 'bob', 'carol'],
        permissions: { default: ['pull'], internal: ['pull'], experimental: ['pull', 'push', 'delete'], archive: ['pull'] },
    },
    {
        name: 'Package owners', description: 'Maintain the internal feed', members: ['alice'],
        permissions: { internal: ['pull', 'push', 'delete'] },
    },
    {
        name: 'Build agents', description: 'CI pipelines that publish packages', members: ['build-agent'],
        permissions: { default: ['pull'], internal: ['pull', 'push'], experimental: ['pull', 'push'] },
    },
];

// Downloads per version, so the download counts and the statistics page aren't all zero:
// [feed, id, version, downloads].
const downloads = [
    ['internal', 'Contoso.Core', '2.0.0', 42],
    ['internal', 'Contoso.Core', '1.1.0', 8],
    ['internal', 'Contoso.Logging', '2.0.0', 35],
    ['internal', 'Contoso.Logging', '1.5.0', 12],
    ['internal', 'Contoso.Testing', '5.0.0', 22],
    ['internal', 'Contoso.Http.Resilience', '3.2.0', 18],
    ['internal', 'Contoso.Configuration', '2.3.1', 15],
    ['internal', 'Contoso.Messaging', '1.0.0', 9],
    ['internal', 'Contoso.Data', '1.2.0', 6],
    ['internal', 'Contoso.Security', '4.0.0', 4],
    ['experimental', 'Contoso.Preview.Ai', '0.1.0-alpha.2', 3],
    ['archive', 'Contoso.Legacy', '1.0.0', 2],
];

// Test packages: [id, description, tags, versions, options]. Order matters for dependencies.
const packages = [
    ['Contoso.Core', 'Shared primitives used by all Contoso libraries.', 'core primitives', ['1.0.0', '1.1.0', '2.0.0']],
    ['Contoso.Logging', 'Structured logging defaults and enrichers for Contoso services.', 'logging serilog observability', ['1.4.0', '1.5.0', '2.0.0'], { deps: { 'Contoso.Core': '2.0.0' }, symbols: true }],
    ['Contoso.Configuration', 'Typed configuration and secret loading for Contoso apps.', 'configuration secrets', ['2.3.1'], { deps: { 'Contoso.Core': '2.0.0' } }],
    ['Contoso.Http.Resilience', 'Retry, timeout and circuit breaker policies for HttpClient.', 'http resilience polly', ['3.1.0', '3.2.0'], { deps: { 'Contoso.Logging': '2.0.0' } }],
    ['Contoso.Messaging', 'Publish and consume integration events over the Contoso service bus.', 'messaging events servicebus', ['0.9.0', '1.0.0']],
    ['Contoso.Messaging', 'Publish and consume integration events over the Contoso service bus.', 'messaging events servicebus', ['1.1.0-beta.2'], { feed: 'experimental' }],
    ['Contoso.Testing', 'Test fixtures and fakes shared across Contoso solutions.', 'testing xunit fixtures', ['5.0.0']],
    ['Contoso.Data', 'Database access helpers and migrations support.', 'data sql ef', ['1.2.0']],
    ['Contoso.Caching', 'Distributed cache helpers on top of IDistributedCache.', 'caching redis', ['0.4.0']],
    ['Contoso.Security', 'Token validation and permission checks.', 'security auth', ['4.0.0']],
    ['Contoso.Mail', 'Templated email sending.', 'email smtp', ['1.0.0']],
    ['Contoso.Storage', 'Blob storage abstraction for local disk and cloud.', 'storage blob', ['2.1.0']],
    ['Contoso.Jobs', 'Background jobs and schedules.', 'jobs scheduling', ['1.3.0']],
    ['Contoso.Search', 'Full-text search client.', 'search', ['0.2.0']],
    ['Contoso.Pdf', 'PDF rendering for invoices and reports.', 'pdf reports', ['3.0.0']],
    ['Contoso.Excel', 'Excel import and export.', 'excel xlsx', ['1.1.0']],
    ['Contoso.Metrics', 'Metrics and health checks.', 'metrics observability health', ['2.2.0']],
    ['Contoso.FeatureFlags', 'Feature flag evaluation.', 'feature-flags', ['1.0.0']],
    ['Contoso.Localization', 'Resource-based localization.', 'localization i18n', ['1.0.0']],
    ['Contoso.Validation', 'Validation rules for requests and commands.', 'validation', ['1.4.0']],
    ['Contoso.Auditing', 'Audit trail for domain changes.', 'audit', ['0.8.0']],
    ['Contoso.Maps', 'Geocoding and map tiles.', 'maps geo', ['1.0.0']],
    ['Contoso.Payments', 'Payment provider integration.', 'payments', ['2.0.0']],
    ['Contoso.Legacy', 'Old helpers kept for existing applications.', 'legacy', ['1.0.0'], { feed: 'archive' }],
    ['Contoso.Preview.Ai', 'Experimental AI helpers.', 'preview ai', ['0.1.0-alpha.1', '0.1.0-alpha.2'], { feed: 'experimental' }],
];

const nupkg = (id, version) => join(packagesDir, `${id}.${version}.nupkg`);

function buildPackages() {
    mkdirSync(packagesDir, { recursive: true });
    for (const [id, description, tags, versions, options = {}] of packages) {
        for (const version of versions) {
            if (existsSync(nupkg(id, version))) continue;
            const deps = Object.entries(options.deps ?? {})
                .map(([dep, v]) => `    <PackageReference Include="${dep}" Version="${v}" />`).join('\n');
            writeFileSync(join(templateDir, 'pkg.props'), `<Project>
  <PropertyGroup>
    <PackageId>${id}</PackageId>
    <Description>${description}</Description>
    <PackageTags>${tags}</PackageTags>
    <IncludeSymbols>${options.symbols ? 'true' : 'false'}</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <RestoreAdditionalProjectSources>${packagesDir}</RestoreAdditionalProjectSources>
  </PropertyGroup>
  <ItemGroup>
${deps}
  </ItemGroup>
</Project>
`);
            writeFileSync(join(templateDir, 'README.md'), `# ${id}\n\n${description}\n\n## Getting started\n\n\`\`\`shell\ndotnet add package ${id}\n\`\`\`\n\nMaintained by the Contoso platform team.\n`);
            console.log(`pack ${id} ${version}`);
            execFileSync('dotnet', ['pack', templateDir, '-c', 'Release', '-o', packagesDir, `-p:Version=${version}`, '--nologo', '-v', 'q'], { stdio: 'inherit' });
        }
    }
}

// A tiny HTTP client with a cookie jar, enough for the Razor forms.
const jar = new Map();
async function http(path, { method = 'GET', form, json, auth } = {}) {
    const headers = {};
    if (jar.size) headers.cookie = [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
    if (auth) headers.authorization = 'Basic ' + Buffer.from(`${auth.username}:${auth.password}`).toString('base64');
    let body;
    if (form) { body = new URLSearchParams(form); headers['content-type'] = 'application/x-www-form-urlencoded'; }
    if (json) { body = JSON.stringify(json); headers['content-type'] = 'application/json'; }
    const res = await fetch(baseUrl + path, { method, headers, body, redirect: 'manual' });
    for (const c of res.headers.getSetCookie?.() ?? []) {
        const [pair] = c.split(';');
        const eq = pair.indexOf('=');
        jar.set(pair.slice(0, eq), pair.slice(eq + 1));
    }
    return { status: res.status, location: res.headers.get('location'), text: await res.text() };
}

const token = html => html.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/)?.[1]
    ?? html.match(/value="([^"]+)" name="__RequestVerificationToken"/)?.[1];

async function postPage(page, handler, fields) {
    const get = await http(page);
    const t = token(get.text);
    if (!t) throw new Error(`No antiforgery token on ${page} (status ${get.status}).`);
    const res = await http(`${page}${handler ? `?handler=${handler}` : ''}`, { method: 'POST', form: { ...fields, __RequestVerificationToken: t } });
    if (res.status >= 400) throw new Error(`POST ${page} ${handler ?? ''} failed with ${res.status}.`);
    const error = res.text.match(/alert-danger[^>]*>\s*([^<]+)/)?.[1]?.trim();
    if (error) throw new Error(`POST ${page} ${handler ?? ''}: ${error}`);
    return res;
}

async function push(feed, file) {
    const form = new FormData();
    form.set('package', new Blob([readFileSync(file)]), file.split(/[\\/]/).pop());
    const path = (feed === 'default' ? '' : `/feeds/${feed}`) + (file.endsWith('.snupkg') ? '/api/v2/symbol' : '/api/v2/package');
    const res = await fetch(baseUrl + path, {
        method: 'PUT', body: form,
        headers: { authorization: 'Basic ' + Buffer.from(`${admin.username}:${admin.password}`).toString('base64') },
    });
    if (res.status !== 201 && res.status !== 409) throw new Error(`Push of ${file} to ${feed} failed with ${res.status}.`);
    console.log(`push ${feed} ${file.split(/[\\/]/).pop()} ${res.status}`);
}

async function seedServer() {
    const login = await http('/Login');
    const t = token(login.text);
    // An empty server has the default administrator admin/admin, which has to choose a new password first.
    const signIn = await http('/Login', { method: 'POST', form: { Username: admin.username, Password: defaultAdminPassword, __RequestVerificationToken: t } });
    if (signIn.status !== 302) throw new Error(`Sign-in as ${admin.username} failed (status ${signIn.status}). Is this an empty test server?`);
    await postPage('/Account/ChangePassword', null, {
        CurrentPassword: defaultAdminPassword, NewPassword: admin.password, ConfirmPassword: admin.password,
    });

    for (const feed of feeds) {
        const res = await http('/api/v1/feeds', { method: 'POST', json: feed });
        console.log(`feed ${feed.slug} ${res.status}`);
    }
    const feedIds = Object.fromEntries(JSON.parse((await http('/api/v1/feeds')).text).map(f => [f.slug, f.id]));

    for (const u of users) {
        await postPage('/Admin/Accounts', 'Create', {
            NewUsername: u.username, NewDisplayName: u.displayName, NewEmail: u.email,
            NewPassword: userPassword, NewCanLoginToUI: String(u.canLoginToUI),
        });
        console.log(`user ${u.username}`);
    }

    for (const g of groups) {
        await postPage('/Admin/Groups', 'CreateGroup', { NewGroupName: g.name, NewAppRoleValue: '', NewDescription: g.description });
    }
    const groupsHtml = (await http('/Admin/Groups')).text;
    const userIds = Object.fromEntries([...groupsHtml.matchAll(/<option value="([0-9a-f-]{36})">([^ <]+) \(/g)].map(m => [m[2], m[1]]));
    for (const g of groups) {
        const panel = groupsHtml.split('class="panel panel-default"').find(p => p.includes(`>\n                        ${g.name}\n`) || new RegExp(`panel-title[^>]*>\\s*${g.name}\\s*<`).test(p));
        const groupId = panel?.match(/name="groupId" value="([0-9a-f-]{36})"/)?.[1];
        if (!groupId) throw new Error(`Group ${g.name} not found.`);
        for (const m of g.members) await postPage('/Admin/Groups', 'AddUser', { groupId, userId: userIds[m] });
        const fields = { groupId };
        Object.entries(g.permissions).forEach(([slug, perms], i) => {
            fields[`permissions[${i}].FeedId`] = feedIds[slug];
            fields[`permissions[${i}].CanPull`] = String(perms.includes('pull'));
            fields[`permissions[${i}].CanPush`] = String(perms.includes('push'));
            fields[`permissions[${i}].CanDelete`] = String(perms.includes('delete'));
        });
        await postPage('/Admin/Groups', 'SavePermissions', fields);
        console.log(`group ${g.name}`);
    }

    for (const [id, , , versions, options = {}] of packages) {
        for (const version of versions) {
            await push(options.feed ?? 'internal', nupkg(id, version));
            if (options.symbols) await push(options.feed ?? 'internal', join(packagesDir, `${id}.${version}.snupkg`));
        }
    }

    // An unlisted version, for the relist flow.
    const unlist = await fetch(`${baseUrl}/feeds/internal/api/v2/package/Contoso.Logging/1.4.0`, {
        method: 'DELETE', headers: { authorization: 'Basic ' + Buffer.from(`${admin.username}:${admin.password}`).toString('base64') },
    });
    console.log(`unlist Contoso.Logging 1.4.0 ${unlist.status}`);

    // Every package download counts once.
    for (const [feed, id, version, count] of downloads) {
        const lower = id.toLowerCase();
        const url = `${baseUrl}/feeds/${feed}/v3/package/${lower}/${version}/${lower}.${version}.nupkg`;
        for (let i = 0; i < count; i++) {
            const res = await fetch(url, { headers: { authorization: 'Basic ' + Buffer.from(`${admin.username}:${admin.password}`).toString('base64') } });
            await res.arrayBuffer();
            if (res.status !== 200) throw new Error(`Download of ${id} ${version} from ${feed} failed with ${res.status}.`);
        }
        console.log(`download ${feed} ${id} ${version} x${count}`);
    }

    // Feed settings last, so the pushes above aren't blocked by read-only mode.
    const settings = (slug, name, description, overrides = {}, mirrors = []) => {
        const f = {
            Name: name, Description: description,
            UseGlobalReadOnly: 'true', UseGlobalOverwrite: 'true', UseGlobalDeletion: 'true', UseGlobalMaxSize: 'true',
            UseGlobalRetentionMajor: 'true', UseGlobalRetentionMinor: 'true', UseGlobalRetentionPatch: 'true',
            UseGlobalRetentionPrerelease: 'true', UseGlobalListingCache: 'true', ...overrides,
        };
        mirrors.forEach((m, i) => Object.assign(f, {
            [`Mirrors[${i}].Enabled`]: 'true', [`Mirrors[${i}].PackageSource`]: m, [`Mirrors[${i}].Legacy`]: 'false',
            [`Mirrors[${i}].DownloadTimeoutSeconds`]: '600', [`Mirrors[${i}].AuthType`]: 'None',
        }));
        return postPage(`/Admin/Feeds/${slug}/Settings`, null, f);
    };
    await settings('default', 'Default', 'Mirror of nuget.org', {}, ['https://api.nuget.org/v3/index.json']);
    await settings('internal', 'Internal', 'Packages built by our own teams', {
        UseGlobalOverwrite: 'false', AllowPackageOverwrites: 'PrereleaseOnly',
        UseGlobalRetentionMajor: 'false', RetentionMaxMajorVersions: '3',
    });
    await settings('experimental', 'Experimental', 'Previews and spikes', {
        UseGlobalDeletion: 'false', PackageDeletionBehavior: 'HardDelete',
        UseGlobalRetentionPrerelease: 'false', RetentionMaxPrereleaseVersions: '5',
    });
    await settings('archive', 'Archive', 'Frozen packages, read-only', { UseGlobalReadOnly: 'false', IsReadOnlyMode: 'true' });
    console.log('feed settings saved');
}

buildPackages();
if (!packagesOnly) await seedServer();
console.log('done');
