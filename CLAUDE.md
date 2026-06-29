# CLAUDE.md — MoneyBoard

家計・資産管理 Web アプリ（Blazor WebAssembly + Azure Functions + Cosmos DB）。
**公開リポジトリ（技術ポートフォリオ）。シークレットをコード・ドキュメントに含めない。**

## まず読む
- **設計の網羅資料**: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)（基本・概要設計・ADR・申し送り）
- 概要 / 主な機能 / 動かし方: [README.md](README.md)

## プロジェクト構成
- `MoneyBoard/` … フロントエンド（Blazor WASM・.NET 10）
- `MoneyBoardApi/` … バックエンド（Azure Functions Isolated・.NET 8）
- `MoneyBoardShared/` … 純粋ドメインロジック＋モデル（**テスト対象の中心**）
- `MoneyBoardShared.Tests/` `MoneyBoardApi.Tests/` … xUnit（**sln なし**。各 `.csproj` を個別に `dotnet test`）

## 開発フロー（個人標準に準拠）
- ブランチ: `dev` で作業 → `main` へ PR → CI 緑でマージ → SWA 自動デプロイ（**`main` へ直接 push しない**）
- コミット / PR タイトル: `type(scope): 日本語要約`（type 必須）。本文は markdown 箇条書き。AI 支援時は末尾に `Co-Authored-By`
- タスク: **GitHub Issues**（1 機能 = 1 issue）／関連は **Milestone**／恒久的な設計判断は `docs/ARCHITECTURE.md` の **ADR**（＋ issue# 相互参照）
- バージョン: SemVer（現行 2.2.0）。`docs`/`chore`/`refactor`（挙動不変）のみの変更では上げない

## ローカル
- 起動: `launch.bat`（Azurite + API:7071 + フロント:5000）
- テスト: `dotnet test MoneyBoardShared.Tests/MoneyBoardShared.Tests.csproj` ／ `dotnet test MoneyBoardApi.Tests/MoneyBoardApi.Tests.csproj`
- シークレット: `MoneyBoardApi/local.settings.json`（**.gitignore 済・コミット禁止**）。本番は SWA アプリ設定。

## 運用方針

| 項目 | 内容 |
|------|------|
| **可視性** | PUBLIC（技術ポートフォリオ） |
| **ブランチ** | `dev` で作業 → `main` へ PR → CI 緑でマージ（`main` への直接 push 禁止） |
| **CI / マージゲート** | `dotnet-test.yml` を必須チェックとする |
| **デプロイ** | Azure Static Web Apps（SWA）に `main` マージで自動デプロイ |
| **バージョニング** | SemVer。`feat`→MINOR / `fix`→PATCH / 非互換→MAJOR。`docs`/`chore`/`refactor`（挙動不変）のみは据え置き。版上げは単独コミット |
| **タスク運用** | GitHub Issues（1 機能 = 1 issue）／Milestone でフェーズ束ね／Label で分類／恒久的設計判断は `docs/ARCHITECTURE.md` の ADR（issue# 相互参照） |

## 注意点
- AI（Claude）の API キーは**サーバー側のみ**（WASM に置かない）。「取得」と「解析」を分離し、解析部を `internal static` でテスト可能にする。
- カバレッジは計測スコープを自アセンブリに限定（他アセンブリ・生成コードの誤計上に注意）。
- 落とし穴・詳細仕様はすべて `docs/ARCHITECTURE.md` に集約。作業前に該当箇所を参照する。
