export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'scope-enum': [0],
    'subject-case': [0],
    'header-max-length': [2, 'always', 100],
  },
  // Optional trailing requirement ID, e.g. "feat(app): add thing [FR-LOG-01]".
  parserPreset: {
    parserOpts: {
      headerPattern: /^(\w*)(?:\(([^)]*)\))?!?: (.*?)(?: \[((?:FR|NFR|AC)-[A-Z]+-\d{2})\])?$/,
      headerCorrespondence: ['type', 'scope', 'subject', 'requirement'],
    },
  },
};
