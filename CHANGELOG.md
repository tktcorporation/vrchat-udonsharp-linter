# tktco-udonsharp-linter

## 0.1.2

### Patch Changes

- [`c3dfc54`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/c3dfc5466881a1d9fedaf3d8bf1ce33f5845c07e) Thanks [@tktcorporation](https://github.com/tktcorporation)! - リリースノート抽出処理の修正

  - インライン Node.js コードを外部スクリプト（scripts/extract-release-notes.js）に移動
  - 正規表現のエスケープ問題を解決し、CHANGELOG.md からの抽出が正しく動作するように修正
  - README から旧パッケージ（VRChat.UdonSharp.Linter）に関する注記を削除

## 0.1.1

### Patch Changes

- [`0ce0fc6`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/0ce0fc63bb790a1e24209259a3fd515bee00b642) Thanks [@tktcorporation](https://github.com/tktcorporation)! - リリースワークフローの改善

  - GitHub Release の description に CHANGELOG.md の内容を使用するように変更
  - @changesets/changelog-github を導入し、PR リンク・コミットリンク・貢献者表記付きのリッチなリリースノートを生成
  - タグ存在チェックをリモート対応に修正（`git rev-parse` → `git ls-remote`）
  - GitHub Release が既に存在する場合はスキップするように改善

## 0.1.0

### Breaking Changes

- Package renamed from `VRChat.UdonSharp.Linter` to `tktco.UdonSharpLinter`
- Namespace changed from `UdonSharpLinterCLI` to `tktco.UdonSharpLinter`
- Version reset to 0.1.0 to indicate pre-release status

### Migration Guide

If you were using the previous package `VRChat.UdonSharp.Linter`, please uninstall it and install the new package:

```bash
dotnet tool uninstall VRChat.UdonSharp.Linter
dotnet tool install tktco.UdonSharpLinter
```

The command name remains the same: `udonsharp-lint`

### Features

- Static analysis for VRChat UdonSharp scripts
- Detection of common UdonSharp restrictions and unsupported features
- CLI tool for linting UdonSharp code
- All features from VRChat.UdonSharp.Linter v1.1.2
