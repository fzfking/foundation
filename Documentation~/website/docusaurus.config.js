// @ts-check
const { themes } = require('prism-react-renderer');

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Foundation',
  tagline: 'A reusable application foundation for Unity: DI port, messaging, save, content, UI, audio and more.',
  favicon: 'img/favicon.svg',
  url: 'https://fzfking.github.io',
  baseUrl: '/foundation/',
  organizationName: 'fzfking',
  projectName: 'foundation',
  onBrokenLinks: 'throw',
  trailingSlash: false,

  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'ru'],
    localeConfigs: {
      en: { label: 'English', htmlLang: 'en' },
      ru: { label: 'Русский', htmlLang: 'ru' },
    },
  },

  markdown: {
    format: 'detect',
    mermaid: true,
    hooks: { onBrokenMarkdownLinks: 'throw' },
  },
  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      { hashed: true, language: ['en', 'ru'], docsRouteBasePath: '/', indexBlog: false, highlightSearchTermsOnTargetPage: true },
    ],
  ],

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          routeBasePath: '/',
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/fzfking/foundation/tree/main/Documentation~/website/',
        },
        blog: false,
        theme: { customCss: './src/css/custom.css' },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      navbar: {
        title: 'Foundation',
        logo: { alt: 'Foundation', src: 'img/logo.svg' },
        items: [
          { type: 'docSidebar', sidebarId: 'docs', position: 'left', label: 'Documentation' },
          { type: 'localeDropdown', position: 'right' },
          { href: 'https://github.com/fzfking/foundation/blob/main/CHANGELOG.md', label: 'Changelog', position: 'right' },
          { href: 'https://github.com/fzfking/foundation', label: 'GitHub', position: 'right' },
        ],
      },
      footer: {
        style: 'dark',
        copyright: `MIT License. Built with Docusaurus.`,
      },
      prism: {
        theme: themes.github,
        darkTheme: themes.dracula,
        additionalLanguages: ['csharp', 'json', 'bash', 'diff'],
      },
      colorMode: { respectPrefersColorScheme: true },
    }),
};

module.exports = config;
