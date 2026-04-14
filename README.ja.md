# UdonSharpLinterCLI

[![NuGet](https://img.shields.io/nuget/v/tktco.UdonSharpLinter)](https://www.nuget.org/packages/tktco.UdonSharpLinter)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

🌐 **Language:** [English](README.md) | 日本語

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

| エラーコード | 説明 |
|---|---|
| UDON029 | async/await は使用できません |
| UDON008 | コレクション初期化子は使用できません |
| UDON005 | コンストラクタは使用できません |
| UDON018 | ジェネリッククラスは使用できません |
| UDON006 | ジェネリックメソッドは使用できません |
| UDON030 | goto文およびラベル文は使用できません |
| UDON003 | ローカル関数は使用できません |
| UDON009 | 多次元配列は使用できません |
| UDON012 | ネストした型は使用できません |
| UDON027 | null条件演算子 (?.) は使用できません |
| UDON007 | オブジェクト初期化子は使用できません |
| UDON011 | staticフィールドは使用できません（constは除く） |
| UDON002 | Throw文は使用できません |
| UDON001 | Try/Catch/Finally文は使用できません |

### API・属性の制限

| エラーコード | 説明 |
|---|---|
| UDON019 | Udonに公開されていない一般的なAPIの使用を検出します |
| UDON017 | インターフェースの実装は使用できません |
| UDON016 | メソッドオーバーロードは使用できません |
| UDON013 | [NetworkCallable]属性付きメソッドには厳しい制約があります |
| UDON026 | SendCustomEvent系メソッドで指定したメソッド名が存在するか検証 |
| UDON014 | TextMeshProの未公開APIの使用を検出します |

### クロスファイル・セマンティック解析

| エラーコード | 説明 |
|---|---|
| UDON020 | 別ファイルで定義されたカスタムクラスのフィールドアクセスは非サポート |
| UDON022 | 別ファイルで定義されたカスタムクラスのメソッド呼び出しをチェック |
| UDON031 | UdonSharpから参照されるユーザー定義型内の静的フィールド定義は使用できません |
| UDON021 | UdonSharpから呼び出される静的メソッド内でのカスタムクラスフィールドアクセスは非サポート |
| UDON025 | UdonSharpBehaviour内での[System.Serializable]クラス使用をチェック（UDON025） |
| UDON031 | ユーザー定義型の静的フィールドへのアクセスは使用できません |

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
