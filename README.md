# MoneyBoard 💰

> 個人の「お金」を一画面で管理する家計・資産管理 Web アプリ。
> 月末残高の予測から、クレジットカード明細の取込、証券ポートフォリオの評価、
> AI によるカード明細スクショ読み取りまでを 1 つのアプリに統合しています。

**Blazor WebAssembly + Azure Functions + Cosmos DB** で構築し、
**Azure Static Web Apps（無料枠）** 上で月額ほぼ 0 円で運用している個人開発プロダクトです。

---

## 📌 このリポジトリについて

技術ポートフォリオとして公開しています。実際に運用している個人向けアプリのため、
ログインは Google 認証＋オーナーによる承認制になっています。
**ソースコード・設計・テスト・CI/CD の進め方**をご覧いただくことを目的としています。

> デモのご利用を希望される場合は個別にご案内します。

---

## ✨ 主な機能

| 機能 | 概要 |
|---|---|
| 🏦 口座別 引き落とし管理 | 「確認時点の残高 ＋ 給料 − 引き落とし − 送金 ＋ 受取」で月末残高を予測し、残高不足の口座を警告。前月末残高を翌月へ自動連鎖。 |
| 💳 クレジットカード明細管理 | CSV 取込（重複明細の自動除外）、リボ請求額（CardBilled）対応、「利用」と「請求」の分離管理。 |
| 🤖 AI でカード明細を読み取り | カード明細のスクショを **Claude（Haiku 4.5 Vision）** で読み取り、構造化出力で明細を自動入力。複数枚・PC は Ctrl+V 貼り付け対応。 |
| 📊 支出グラフ・カテゴリ内訳 | 月次の支出をカテゴリ別に集計し、内訳をドーナツ／グラフで可視化。 |
| 📈 証券ポートフォリオ | 株式・投資信託・為替を一元管理。NISA 枠分割、配当再投資、約定為替、前日比・現在価格表示、資産構成ドーナツ、資産推移グラフ（期間切替）。価格は Yahoo Finance（株・為替）と投資信託協会（協会コード）から取得。 |
| 🔧 固定費管理 | 期間（開始〜終了）対応の固定費を管理し、月次計算に反映。 |
| 📱 モバイル UI 最適化 | PC／スマホをハイブリッド対応。スマホは下部タブナビ・ボトムシート編集・タッチ並べ替えに最適化。 |

---

## 🏗️ アーキテクチャ

> 📐 **基本・概要設計の詳細は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) を参照。** 機能ごとの作業は [GitHub Issues](https://github.com/MyNameIsToshi/moneyboardjp/issues) / [Milestones](https://github.com/MyNameIsToshi/moneyboardjp/milestones) で管理しています。

```
┌──────────────────────────────┐        ┌───────────────────────────────┐
│  MoneyBoard (Blazor WASM)    │        │  MoneyBoardApi                │
│  .NET 10 / SPA フロントエンド │  HTTPS │  (.NET 8 / Azure Functions    │
│                              │ ─────▶ │   Isolated Worker)            │
│  - 画面 (Razor Components)   │  JWT   │  - REST API (CRUD)            │
│  - ApexCharts でグラフ描画   │        │  - Firebase IDトークン検証    │
│  - API 経由でデータ永続化     │        │  - 承認制アクセスゲート       │
└──────────────┬───────────────┘        │  - Claude API（明細読取）     │
               │                        │  - 価格取得（Yahoo / 投信協会）│
               │                        └───────────────┬───────────────┘
               │                                        │
        ┌──────▼───────┐                        ┌───────▼────────┐
        │ Firebase Auth │                        │  Cosmos DB     │
        │ (Google ログイン)                       │ (ユーザー別データ)
        └──────────────┘                        └────────────────┘

         ┌──────────────────────────────────────────────┐
         │  MoneyBoardShared (.NET 8 共有ライブラリ)       │
         │  UI / 永続化に依存しない純粋ドメインロジック    │
         │  LedgerEngine / PortfolioMath / CardCsvParser  │
         │  / FixedCostPeriod / SchemaMigration / Ym ...  │
         │  → フロント・API の両方から参照、単体テスト対象 │
         └──────────────────────────────────────────────┘
```

### プロジェクト構成

| プロジェクト | 役割 |
|---|---|
| `MoneyBoard` | フロントエンド（Blazor WebAssembly / .NET 10）。SPA・グラフ描画・画面ロジック。 |
| `MoneyBoardApi` | バックエンド（Azure Functions Isolated / .NET 8）。REST API・認証・外部連携。 |
| `MoneyBoardShared` | フロント／API 共有の純粋ドメインロジックとモデル。**外部依存を持ち込まないテスト可能な層**。 |
| `MoneyBoardShared.Tests` / `MoneyBoardApi.Tests` | xUnit による単体テスト。 |

---

## 📖 API ドキュメント

**Swagger UI（GitHub Pages）**: https://mynameistoshi.github.io/moneyboardjp/swagger/

バックエンド API の全エンドポイント（認証方式・リクエスト/レスポンスのスキーマ）を OpenAPI 3.0 で公開しています。
ソース: [`docs/swagger/openapi.yaml`](docs/swagger/openapi.yaml)

---

## 🛠️ 技術スタック

**フロントエンド**
- Blazor WebAssembly（.NET 10）
- Blazor-ApexCharts（グラフ描画）
- JS Interop（Firebase Auth・クリップボード画像取得・Shift-JIS デコード・スクロール制御）

**バックエンド**
- Azure Functions（.NET 8 / Isolated Worker Model, v4）
- Azure Cosmos DB（ユーザー別データ・NoSQL）
- Anthropic Claude API（Haiku 4.5 Vision・構造化出力による明細読取）
- Firebase ID トークンの JWT 検証（`Microsoft.IdentityModel.*` / OpenID Connect）

**認証・セキュリティ**
- Firebase Authentication（Google ログイン）
- バックエンドでの ID トークン検証 ＋ オーナーによる承認制アクセスゲート
- シークレットはすべて環境変数管理（リポジトリにキーを含めない）

**インフラ・運用**
- Azure Static Web Apps（無料枠でホスティング）
- Application Insights / OpenTelemetry（監視・テレメトリ）
- GitHub Actions による CI/CD（後述）

---

## ✅ テストと CI/CD

品質と保守性を重視し、純粋ロジックを `MoneyBoardShared` に切り出して
**単体テスト計 122 件**（`MoneyBoardShared` 102 件／`MoneyBoardApi` 20 件）でカバーしています。

- **テストフレームワーク**: xUnit + coverlet（カバレッジ計測）
- **CI**: `dev` への push と `main` への PR で GitHub Actions が自動でテスト実行
- **マージゲート**: `main` への PR はテストが緑でないとマージ不可（ブランチ保護）
- **カバレッジ可視化**: PR に「テスト件数＋カバレッジ表＋合格ラインの根拠」を固定コメントで自動投稿
- **カバレッジ方針**:
  - `MoneyBoardShared`（純粋ドメインロジック層）= **約 90%**（本体の品質指標）
  - `MoneyBoardApi` = 約 40%（大半が Cosmos/HTTP の CRUD・認証＝結合テスト領域のため構造的に低く、テスト価値のあるパーサ・検証ロジックは高水準）

> 単に数値を追うのではなく、「どこをテストすべきか」を設計で切り分けている点を重視しています。

---

## 💡 設計上のこだわり

- **純粋ロジックの分離**: UI・永続化・HTTP・DB といった外部依存を `MoneyBoardShared` に持ち込まず、計算ロジックを純粋関数として隔離。テスト容易性と再利用性を確保。
- **シークレット管理の徹底**: API キーや接続文字列はすべて環境変数／GitHub Secrets で管理し、リポジトリには一切含めない（公開クライアント設定のみ同梱）。
- **コスト最適化**: Azure Static Web Apps の無料枠を活かし、個人運用で維持費をほぼ 0 円に。
- **AI のサーバーサイド集約**: Claude API キーは WASM クライアントに置かず、必ずサーバー関数経由で利用。
- **段階的な機能拡張**: 家計簿 → カード明細 → モバイル最適化 → 証券ポートフォリオ → AI 連携、とフェーズを分けてセマンティックバージョニングで管理。

---

## 🚀 ローカルでの動かし方

### 必要環境
- .NET 10 SDK（フロント）/ .NET 8 SDK（API）
- Azure Functions Core Tools v4
- Azurite（ローカルストレージエミュレータ）

### 手順

```bash
# 依存関係の復元
dotnet restore

# テストの実行
dotnet test

# フロントエンドの起動
cd MoneyBoard
dotnet run
```

API を含めて動かす場合は `MoneyBoardApi/local.settings.json` に各種シークレット
（Cosmos DB 接続文字列・Anthropic API キー等）を設定してください。
このファイルは `.gitignore` 済みでリポジトリには含まれません。

---

## 📂 ディレクトリ構成（抜粋）

```
moneyboard/
├── MoneyBoard/              # フロントエンド (Blazor WASM)
│   ├── Components/          # 画面コンポーネント (Razor)
│   ├── Pages/              # ページ
│   ├── Services/           # 状態管理・API/Storage クライアント・認証
│   └── wwwroot/            # 静的アセット・JS Interop
├── MoneyBoardApi/          # バックエンド (Azure Functions)
│   ├── DataApi*.cs         # 機能別 API（Access / CardImage / Portfolio / Quote）
│   └── FirebaseAuth.cs     # ID トークン検証
├── MoneyBoardShared/       # 共有純粋ロジック・モデル
├── MoneyBoardShared.Tests/ # 単体テスト
├── MoneyBoardApi.Tests/    # 単体テスト
└── .github/workflows/      # CI/CD（テスト・デプロイ）
```

---

## 📝 補足

本リポジトリは個人開発のポートフォリオです。技術的なご質問やお仕事のご相談など、
お気軽にお問い合わせください。

---

## 📄 ライセンス

**All rights reserved.**（独自・無償公開）

本リポジトリは技術ポートフォリオとしての **閲覧** を目的に公開しています。
コード・設計・ドキュメントの **複製・改変・再配布・商用利用は許可していません**。
詳細は [LICENSE](LICENSE) を参照してください。
