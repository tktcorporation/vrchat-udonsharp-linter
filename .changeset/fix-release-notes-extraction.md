---
"tktco-udonsharp-linter": patch
---

リリースノート抽出処理の修正

- インラインNode.jsコードを外部スクリプト（scripts/extract-release-notes.js）に移動
- 正規表現のエスケープ問題を解決し、CHANGELOG.mdからの抽出が正しく動作するように修正
- READMEから旧パッケージ（VRChat.UdonSharp.Linter）に関する注記を削除
