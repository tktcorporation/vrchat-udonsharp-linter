# UdonSharpLinterCLI

[![NuGet](https://img.shields.io/nuget/v/tktco.UdonSharpLinter)](https://www.nuget.org/packages/tktco.UdonSharpLinter)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

VRChat の UdonSharp スクリプトを対象とした静的コード解析ツールです。UdonSharp がサポートしていない言語機能・パターンをコンパイル前に検出します。

---

## インストール

> **前提条件:** .NET 6.0 以降が必要です。

```bash
dotnet tool install -g tktco.UdonSharpLinter
```

### アップデート

```bash
dotnet tool update -g tktco.UdonSharpLinter
```

---

## クイックスタート

```bash
# Assets フォルダ以下の UdonSharp スクリプトをすべて解析
udonsharp-lint Assets

# テストスクリプトを除外して解析
udonsharp-lint Assets --exclude-test-scripts
```

---

## 使い方

```
udonsharp-lint <directory_path> [--exclude-test-scripts]
```

| 引数 / オプション | 説明 |
|---|---|
| `<directory_path>` | 解析対象ディレクトリのパス |
| `--exclude-test-scripts` | `TestScripts` / `Tests` / `Test` ディレクトリを除外 |

### 出力フォーマット

標準的なコンパイラ形式で出力されるため、多くの IDE や CI/CD ツールとそのまま連携できます。

```
path/to/file.cs(line,column): error UDON###: Error message
path/to/file.cs(line,column): warning UDON###: Warning message
```

### 終了コード

| コード | 意味 |
|---|---|
| `0` | エラーなし（警告があっても 0） |
| `1` | エラーあり、または実行失敗 |

---

## 検出できる問題

### 基本言語機能の制限

| エラーコード | 内容 |
|---|---|
| UDON001 | Try/Catch 文 |
| UDON002 | Throw 文 |
| UDON003 | ローカル関数 |
| UDON005 | コンストラクター |
| UDON006 | ジェネリックメソッド |
| UDON007 | オブジェクト初期化子 |
| UDON008 | コレクション初期化子 |
| UDON009 | 多次元配列（ジャグ配列は使用可） |
| UDON011 | static フィールド（const は使用可） |
| UDON012 | ネスト型 |
| UDON018 | ジェネリッククラス |
| UDON027 | Null 条件演算子（`?.` / `?[]`） |
| UDON029 | async / await |
| UDON030 | goto 文 |

### API・属性の制限

| エラーコード | 内容 |
|---|---|
| UDON013 | `[NetworkCallable]` メソッドの制約違反 |
| UDON014 | TextMesh Pro API（警告） |
| UDON016 | メソッドオーバーロード |
| UDON017 | インターフェイス |
| UDON019 | 未公開 API の使用 |

### クロスファイル・セマンティック解析

| エラーコード | 内容 |
|---|---|
| UDON020 | 別ファイルの UdonSharpBehaviour フィールドへのアクセス |
| UDON021 | static メソッドからのフィールドアクセス |
| UDON022 | 別ファイルのメソッド呼び出し |
| UDON025 | `[UdonBehaviourSyncMode]` を持つクラスの Serializable 使用 |
| UDON026 | SendCustomEvent で存在しないメソッド名を指定 |

---

## インテグレーション

### Visual Studio Code (`tasks.json`)

問題パネルへの統合と Ctrl+Shift+B での実行を設定できます。

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "UdonSharp Lint",
      "type": "shell",
      "command": "udonsharp-lint",
      "args": ["${workspaceFolder}/Assets"],
      "problemMatcher": {
        "owner": "udonsharp",
        "fileLocation": ["relative", "${workspaceFolder}"],
        "pattern": {
          "regexp": "^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+UDON(\\d+):\\s+(.+)$",
          "file": 1,
          "line": 2,
          "column": 3,
          "severity": 4,
          "code": 5,
          "message": 6
        }
      }
    }
  ]
}
```

### GitHub Actions

```yaml
- name: Install UdonSharp Linter
  run: dotnet tool install -g tktco.UdonSharpLinter

- name: Run UdonSharp Linter
  run: udonsharp-lint Assets --exclude-test-scripts
```

### mise (`mise.toml`)

```toml
[tasks.lint-udon]
run = "udonsharp-lint Assets"
```

---

## ライセンス

[MIT](LICENSE)
