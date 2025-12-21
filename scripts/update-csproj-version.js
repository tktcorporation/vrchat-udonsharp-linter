const fs = require('fs');
const path = require('path');

// Read version from package.json
const packageJson = JSON.parse(
  fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8')
);
const version = packageJson.version;

// Update .csproj file
const csprojPath = path.join(
  __dirname,
  '..',
  'src',
  'tktco.UdonSharpLinter',
  'tktco.UdonSharpLinter.csproj'
);

let csprojContent = fs.readFileSync(csprojPath, 'utf8');
csprojContent = csprojContent.replace(
  /<Version>.*<\/Version>/,
  `<Version>${version}</Version>`
);

fs.writeFileSync(csprojPath, csprojContent, 'utf8');

console.log(`Updated .csproj version to ${version}`);
