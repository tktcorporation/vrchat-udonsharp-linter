const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

async function createRelease() {
  try {
    // Read version from package.json
    const packageJson = JSON.parse(
      fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8')
    );
    const version = packageJson.version;
    const tagName = `v${version}`;

    // Check if tag already exists locally
    try {
      execSync(`git rev-parse ${tagName}`, { stdio: 'ignore' });
      console.log(`Tag ${tagName} already exists locally. Skipping release creation.`);
      return;
    } catch (error) {
      // Tag doesn't exist locally
    }

    // Check if tag already exists on remote
    try {
      const remoteTag = execSync(`git ls-remote --tags origin ${tagName}`, { encoding: 'utf8' });
      if (remoteTag.trim()) {
        console.log(`Tag ${tagName} already exists on remote. Skipping release creation.`);
        return;
      }
    } catch (error) {
      // Tag doesn't exist on remote
    }

    // Read CHANGELOG.md to get release notes
    let releaseNotes = '';
    const changelogPath = path.join(__dirname, '..', 'CHANGELOG.md');

    if (fs.existsSync(changelogPath)) {
      const changelog = fs.readFileSync(changelogPath, 'utf8');
      const versionHeader = `## ${version}`;
      const startIndex = changelog.indexOf(versionHeader);

      if (startIndex !== -1) {
        const endIndex = changelog.indexOf('\n## ', startIndex + 1);
        releaseNotes = changelog.substring(
          startIndex + versionHeader.length,
          endIndex !== -1 ? endIndex : changelog.length
        ).trim();
      }
    }

    if (!releaseNotes) {
      releaseNotes = `Release ${version}`;
    }

    // Create and push tag
    console.log(`Creating tag ${tagName}...`);
    execSync(`git tag -a ${tagName} -m "Release ${version}"`, { stdio: 'inherit' });
    execSync(`git push origin ${tagName}`, { stdio: 'inherit' });

    // Create GitHub Release using gh CLI
    console.log(`Creating GitHub release ${tagName}...`);
    const releaseNotesFile = path.join(__dirname, '..', '.release-notes.tmp');
    fs.writeFileSync(releaseNotesFile, releaseNotes, 'utf8');

    execSync(
      `gh release create ${tagName} --title "${tagName}" --notes-file "${releaseNotesFile}"`,
      { stdio: 'inherit' }
    );

    // Clean up temp file
    fs.unlinkSync(releaseNotesFile);

    console.log(`✓ Successfully created release ${tagName}`);
  } catch (error) {
    console.error('Error creating release:', error.message);
    process.exit(1);
  }
}

createRelease();
