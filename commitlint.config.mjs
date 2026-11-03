// Commit 775db06 is already pushed in an open PR with a 101-character header, and history is not rewritten,
// so exactly that header is exempt. Every other commit stays under header-max-length 100.
const PUSHED_LONG_HEADER =
  'feat(domain): user aggregate, refresh token, activity log entry [FR-AUTH-02] [FR-AUTH-04] [FR-LOG-02]';

export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'scope-enum': [0],
    'subject-case': [0],
    'header-max-length': [2, 'always', 100],
  },
  ignores: [(message) => message.split(String.fromCharCode(10))[0].trim() === PUSHED_LONG_HEADER],
  // Optional trailing requirement ID, e.g. "feat(app): add thing [FR-LOG-01]".
  parserPreset: {
    parserOpts: {
      headerPattern: /^(\w*)(?:\(([^)]*)\))?!?: (.*?)(?: \[((?:FR|NFR|AC)-[A-Z]+-\d{2})\])?$/,
      headerCorrespondence: ['type', 'scope', 'subject', 'requirement'],
    },
  },
};
