---
"tktco-udonsharp-linter": patch
---

リリースワークフローの改善

- GitHub ReleaseのdescriptionにCHANGELOG.mdの内容を使用するように変更
- @changesets/changelog-githubを導入し、PRリンク・コミットリンク・貢献者表記付きのリッチなリリースノートを生成
- タグ存在チェックをリモート対応に修正（`git rev-parse` → `git ls-remote`）
- GitHub Releaseが既に存在する場合はスキップするように改善
