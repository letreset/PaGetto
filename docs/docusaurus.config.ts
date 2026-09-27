import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'PaGetto',
  tagline: 'A lightweight, self-hosted NuGet and symbol server',
  favicon: 'img/favicon.svg',

  url: 'https://letreset.github.io',
  baseUrl: '/PaGetto/',

  organizationName: 'letreset',
  projectName: 'PaGetto',

  // Deployed by .github/workflows/docs.yml (GitHub Pages via Actions).
  trailingSlash: false,

  onBrokenLinks: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },
  onBrokenAnchors: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  themes: [
    [
      require.resolve('@easyops-cn/docusaurus-search-local'),
      {
        hashed: true,
        indexBlog: false,
        docsRouteBasePath: '/docs',
        highlightSearchTermsOnTargetPage: true,
      },
    ],
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/letreset/PaGetto/tree/main/docs/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/social-preview.png',
    colorMode: {
      defaultMode: 'light',
      disableSwitch: false,
      respectPrefersColorScheme: true,
    },
    announcementBar: {
      id: 'migrating-from-bagetter',
      content: '🔄 <b>Coming from BaGetter?</b> PaGetto takes over your existing database and packages in place. <a href="/PaGetto/docs/migrating-from-bagetter">Read the migration guide</a>.',
      // Theme variables, so the bar follows the light and dark theme (the default is white).
      backgroundColor: 'var(--bgt-cream)',
      textColor: 'var(--ifm-font-color-base)',
      isCloseable: true,
    },
    docs: {
      sidebar: {
        hideable: true,
        autoCollapseCategories: true,
      },
    },
    navbar: {
      title: 'PaGetto',
      logo: {
        alt: 'PaGetto logo',
        src: 'img/logo.svg',
      },
      style: 'dark',
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'tutorialSidebar',
          position: 'left',
          label: 'Documentation',
        },
        {
          href: 'https://github.com/letreset/PaGetto/releases',
          label: 'Releases',
          position: 'left',
        },
        {
          href: 'https://hub.docker.com/r/letreset/pagetto',
          'aria-label': 'Docker Hub',
          className: 'header-docker-link',
          position: 'right',
        },
        {
          href: 'https://github.com/letreset/PaGetto',
          'aria-label': 'GitHub repository',
          className: 'header-github-link',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'light',
      links: [
        {
          title: 'Documentation',
          items: [
            {label: 'Get started', to: '/docs'},
            {label: 'Docker', to: '/docs/Installation/docker'},
            {label: 'Kubernetes', to: '/docs/Installation/kubernetes'},
            {label: 'Configuration', to: '/docs/configuration'},
          ],
        },
        {
          title: 'Community',
          items: [
            {label: 'Issues', href: 'https://github.com/letreset/PaGetto/issues'},
            {label: 'Contributing', href: 'https://github.com/letreset/PaGetto/blob/main/CONTRIBUTING.md'},
          ],
        },
        {
          title: 'More',
          items: [
            {label: 'GitHub', href: 'https://github.com/letreset/PaGetto'},
            {label: 'Releases', href: 'https://github.com/letreset/PaGetto/releases'},
            {label: 'Docker Hub', href: 'https://hub.docker.com/r/letreset/pagetto'},
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} PaGetto contributors. Built with <a href="https://docusaurus.io">Docusaurus</a>.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.vsDark,
      additionalLanguages: ['csharp', 'json', 'powershell', 'bash', 'yaml', 'docker', 'diff', 'ini'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
