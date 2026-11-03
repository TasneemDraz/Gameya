// Web tools run from src/gameya-web's local binaries so its eslint/stylelint/prettier configs apply.
const q = (files) => files.map((f) => `"${f}"`).join(' ');
const web = (tool) => `npm --prefix src/gameya-web exec --no -- ${tool}`;

export default {
  'src/gameya-web/**/*.{ts,html}': (files) => [
    `${web('eslint --fix')} ${q(files)}`,
    `prettier --write ${q(files)}`,
  ],
  '**/*.scss': (files) => `${web('stylelint --fix --config src/gameya-web/.stylelintrc.json')} ${q(files)}`,
  '**/*.{json,md}': (files) => `prettier --write ${q(files)}`,
  '**/*.cs': (files) => `dotnet format Gameya.sln --include ${q(files)}`,
  'src/gameya-web/public/i18n/*.json': () => 'node tools/check-i18n.mjs',
};
