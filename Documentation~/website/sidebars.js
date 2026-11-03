// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    'intro',
    {
      type: 'category',
      label: 'Getting started',
      collapsed: false,
      items: ['getting-started/installation', 'getting-started/quick-start'],
    },
    {
      type: 'category',
      label: 'Concepts',
      items: ['concepts/architecture', 'concepts/app-composition'],
    },
    {
      type: 'category',
      label: 'Core',
      items: [
        'core/di',
        'core/messaging',
        'core/state-machine',
        'core/simulation',
        'core/time-scheduling-random',
        'core/requirements-rewards',
        'core/diagnostics',
        'core/reactive-motion',
      ],
    },
    {
      type: 'category',
      label: 'Application',
      items: [
        'app/game-contexts',
        'app/game-modules',
        'app/save',
        'app/config',
        'app/content',
        'app/content-mirroring',
        'app/localization',
        'app/release',
      ],
    },
    {
      type: 'category',
      label: 'UI',
      items: ['ui/router', 'ui/ugui', 'ui/ulayout', 'ui/badges', 'ui/feedbacks'],
    },
    {
      type: 'category',
      label: 'Systems',
      items: ['systems/audio', 'systems/pooling', 'systems/sequences', 'systems/tutorial', 'systems/typed-ids'],
    },
    {
      type: 'category',
      label: 'Adapters',
      items: [
        'adapters/overview',
        'adapters/vcontainer',
        'adapters/serialization',
        'adapters/addressables',
        'adapters/yooasset',
      ],
    },
    {
      type: 'category',
      label: 'GameFramework',
      items: [
        'gameframework/overview',
        'gameframework/ads',
        'gameframework/analytics',
        'gameframework/cloud-save',
        'gameframework/services',
      ],
    },
    {
      type: 'category',
      label: 'Editor',
      items: ['editor/bootstrap', 'editor/workspace'],
    },
  ],
};

module.exports = sidebars;
