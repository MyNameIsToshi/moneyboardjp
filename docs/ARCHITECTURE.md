# MoneyBoard 基本・概要設計（Architecture）

> 本ドキュメントは MoneyBoard の基本設計・概要設計をまとめた技術資料です。
> リポジトリは技術ポートフォリオとして **公開** しています。
> シークレット（接続文字列・APIキー等）は一切記載せず、**Azure アプリ設定／`local.settings.json`（gitignore 済）でのみ管理**します。
> タスク管理は **GitHub Issues**、恒久的な設計判断は本ドキュメントの **ADR 節** で行います。

## プロジェクト概要

**MoneyBoard** - 口座別の収支管理・家計簿Webアプリ（給料日15日サイクル・カード明細・統計）

- GitHub: https://github.com/MyNameIsToshi/moneyboardjp
- 本番URL: https://purple-stone-08eacab00.7.azurestaticapps.net
- ローカルパス: `C:\Development\moneyboard\`
- **現行バージョン: `2.2.0`**（2026-06-27 本番リリース・PR #62。金額マスク機能＝アプリ全体トグル・リロード後も反映・入力欄/ダイアログ/ドーナツにも適用）。`2.1.0`（2026-06-27 本番リリース・PR #60。マイページ口座並べ替え▲▼/D&D・設定行アイコン統一・起点月より過去へ戻れる不具合修正・追加時空データ出現不具合修正）。`2.0.0`（2026-06-26・PR #58。全画面リデザイン＆PCサイドバーナビ＝ヒーロー集約＋線アイコン統一、月次/カード/統計/資産/マイページ全画面刷新、PC左サイドバー、ポートフォリオ市場指数3列グリッド。メジャー番号＝UIの大節目・API非互換なし）。`1.5.0`（2026-06-21・PR #35。市場指標バー=NYダウ/ナスダック/S&P500/日経/KOSPI 5本・AI読取エラー可視化）。`1.4.0`（2026-06-21・PR #22。**Phase 4 土台＝Claude Vision でカード明細スクショをAI読み取り→当月へ取込**。詳細は「Phase 4」節）。`1.3.4`（2026-06-20・PR #21。CIカバレッジをPRコメント＋Job Summaryに出力）／`1.3.3`（PR #20。Step4前クリーンアップ＝テスト基盤整備・純粋ロジック抽出・巨大razor code-behind分離）／`1.3.2`=証券ポートフォリオ表示改善＋深いURL404修正／`1.3.1`=スマホ実機修正／`1.3.0`=スマホUI全面最適化／`1.2.0`=Phase 3 証券ポートフォリオ。
- 次の AI 機能（C案カテゴリ推定・月次コメント・FABチャット 等）は Phase 4 の土台を再利用して順次追加。

---

## 技術スタック

```
MoneyBoard/         Blazor WASM (.NET 10) フロントエンド
MoneyBoardApi/      Azure Functions v4 Isolated (.NET 8) バックエンド
MoneyBoardShared/   共通モデルライブラリ (.NET 8)
```

| 項目 | 内容 |
|------|------|
| ホスティング | Azure Static Web Apps (Free) |
| DB | Azure Cosmos DB for NoSQL (サーバーレス) Japan East |
| CI/CD | GitHub Actions (main へのマージで自動デプロイ) |
| 監視 | Application Insights (dev/prod 分離) |
| 認証 | Firebase Authentication (Google)・承認制（オーナーが承認したユーザーのみ利用可） |

---

## Azure リソース一覧

| リソース | 名前 | リソースグループ |
|---------|------|----------------|
| Static Web Apps | moneyboard-swa | rg-moneyboard |
| Cosmos DB | moneyboard-cosmos | rg-moneyboard |
| Application Insights (prod) | moneyboard-insights | rg-moneyboard |
| Application Insights (dev) | moneyboard-insights-dev | rg-moneyboard |

### Cosmos DB データベース
- `moneyboard-dev` … ローカル開発用
- `moneyboard-prod` … 本番用
- コンテナ名: `userdata`、パーティションキー: `/userId`

#### ドキュメント構造（1ユーザー = 複数ドキュメント / 同一 `/userId` パーティション）
- **`/userId` は Firebase の uid**（認証導入前は固定 `"default"`）。各ユーザーが自分の uid パーティションを持つ＝データ分離。
- `settings` … 口座・固定費・カテゴリ・カード・店名→カテゴリルール・SchemaVersion
- `month:yyyyMM` … 月ごとの月次データ（Ledgers・Transfers・CardDetails・CardBilled）
- `access-control`（partition `__system__`・id `access-control`）… アクセス承認の管理（`approved[]`＝AccessUser{uid,email,name}, `pending[]`）。オーナーのみ操作可
- GET `/api/data` は全ドキュメントを集約して返す
- POST `/api/data` は**変更があったドキュメントのみ** TransactionalBatch で原子的に保存（per-item If-Match で楽観的並行制御 / 競合は 412）
- ※ 旧形式（単一ドキュメント `id=userId`）／旧 `"default"` パーティションからの移行コードは無し。**本番はオーナーが自分の uid で1からデータ作成**（旧 `default` データは未使用で残置）

---

## ローカル開発起動手順

```
C:\Development\moneyboard\launch.bat をダブルクリック
```

内部で以下を起動：
1. Azurite (localhost:10000)
2. MoneyBoardApi (localhost:7071)
3. MoneyBoard (localhost:5000)

---

## 環境変数

### local.settings.json (MoneyBoardApi)
```json
{
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;...",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDb__ConnectionString": "（Cosmos DB 接続文字列）",
    "CosmosDb__DatabaseName": "moneyboard-dev",
    "APPLICATIONINSIGHTS_CONNECTION_STRING": "（dev用接続文字列）",
    "Firebase__ProjectId": "money-board-jp",
    "AuthBypass": "true",
    "OwnerEmail": "（ローカルは AuthBypass=true なので未使用。非バイパス検証時のみ意味あり）"
  },
  "Host": {
    "CORS": "http://localhost:5000,http://localhost:5001",
    "CORSCredentials": false
  }
}
```

### SWA 環境変数 (本番)
- `CosmosDb__ConnectionString`
- `CosmosDb__DatabaseName` = `moneyboard-prod`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` (prod用)
- `Firebase__ProjectId` = `money-board-jp`（IDトークン検証用・**必須**）
- `OwnerEmail` = （オーナーの Google アカウント・**実値は SWA アプリ設定で管理**。承認なしで使えるオーナー）
- ⚠️ `AuthBypass` は**本番では設定しない**（＝JWT検証必須）。ローカルのみ `true`。未設定でデプロイすると projectId 不一致で全員ログイン不可になるので注意
- `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` … 旧SWA-Google認証用で**現在は未使用**（残置可）

### GitHub Secrets
- `AZURE_STATIC_WEB_APPS_API_TOKEN_PURPLE_STONE_08EACAB00`
- `APPINSIGHTS_CONNECTION_STRING_PROD`

---

## Application Insights 接続文字列

- **prod / dev の接続文字列は本ドキュメントに記載しない**（公開リポジトリのため）。
  - dev → `MoneyBoardApi/local.settings.json`（gitignore 済）
  - prod → **SWA アプリ設定** `APPLICATIONINSIGHTS_CONNECTION_STRING`
  - 実値は **Azure ポータル（各 Application Insights リソース）** で確認

---

## ファイル構成

```
C:\Development\moneyboard\
  MoneyBoard\
    Components\
      AccountsTab.razor       口座管理（マイページ内に内包・口座番号は廃止・チップ＋枠付き行/列見出し整列・#50。スマホ=▲▼/PC=D&D 並べ替え・#53）
      FixedCostTab.razor      固定費設定タブ（口座フィルター[Excel風複数選択]・D&D並び替え）
      MonthlyTab.razor        月次管理タブ（収入[給料/ボーナス/ATM入金/臨時収入]・支出[固定費/カード/ATM出金/手入力]・送金。サマリ=ヒーロー、口座カード/固定費/カードは折りたたみ・#43 リデザイン）
      CardTab.razor           カードタブ（明細の手入力/カードCSV取込[JCB/三井住友/PayPay/au PAY/楽天]/AIで読取[スクショ]/一括カテゴリ・カードごと折りたたみ・#49でリデザイン）
      MyPageTab.razor         マイページタブ（プロフィールヒーロー＋設定カードのグリッド[PC]/折りたたみ[スマホ]・アクセス管理は2列色分け・#50でリデザイン）
      CategorySettings.razor  カテゴリ管理（12色・追加/編集/削除・D&D並び替え）
      CardSettings.razor      カード管理（名前＋口座・追加/削除[確認ダイアログ]・D&D並び替え）
      CycleInfo.razor         月ナビ横の ⓘ ツールチップ（当月=15日サイクルの実期間を表示）
      AmountInput.razor       金額入力共通（フォーカス中はカンマ無し・空欄許可・blurで確定）
      SpendBreakdownCard.razor 統計のドーナツ＋一覧カード（⑥カテゴリ別/⑦カード別で共用）
      DetailDialog.razor      統計の明細ドリルダウンモーダル
      BreakdownDialog.razor   統計の項目別内訳モーダル
      SpendSlice.cs           統計ドーナツ＋一覧の行ビューモデル
      Dialog\
        ConfirmDialog.razor   削除確認ダイアログ
        WarnDialog.razor      警告ダイアログ
    Pages\
      Home.razor              タブシェル（月次/カード/固定費設定/マイページ・読込中はスピナー+操作不可）
      GraphPage.razor         統計ページ（7種・期間指定・sticky ヘッダー・内訳ドリルダウンモーダル）
      Portfolio.razor / GraphPage.razor / Components/CardTab.razor・FixedCostTab.razor は markup と
        code-behind を分離（`*.razor.cs` の partial class）。v1.3.3 で導入＝挙動不変・見通し改善。
        @page/@inject/@implements/@using は .razor 側、ロジックは .razor.cs。.cs は `_Imports` が
        効かないため using を明示する。
      Portfolio.Disp.cs は Portfolio.razor.cs の同一 partial を補完する表示ヘルパファイル（v2.1.0・issue #57）。
        表示フォーマッタ・通貨切替・評価額・総資産・グループ小計・市場指標の表示補助を集約。
        ライフサイクル・価格更新・チャートビルド・D&D・ダイアログは Portfolio.razor.cs が保持。
    Services\
      LedgerService.cs        家計簿ドメインロジック（年月・月次展開・残高計算・口座/固定費/カード/カテゴリ操作）
      AppStateStore.cs        状態保持＋永続化（読込/保存/デバウンス/直列化/競合処理・IsPending/IsOwner）
      StorageService.cs       API通信（エンベロープ⇄AppState変換・etag保持・Bearer添付・403→AccessPendingException・`ExtractCardImageAsync`=スクショAI読取の /api/extract-card 呼出）
      AuthService.cs          Firebase認証ラッパー（ログイン/ログアウト/IDトークン・localhostバイパス）
      AccessService.cs        /api/access クライアント（オーナーの承認管理）
    App.razor                 認証ゲート（未ログイン→ログイン画面 / ログイン後→Router）
    wwwroot\
      css\                    スタイル（役割別に分割: base/monthly/fixedcost/settings/cards/dialog/graph/mypage、index.html がソース順で読込）
      js\                     storage.js（Shift-JISデコード・スクロール）, auth.js（Firebase compat ラッパー）, cardimage.js（スクショAI読取：canvas縮小→JPEG base64・複数枚＋Ctrl+V貼付）
      index.html              JS SDK含む（Firebase compat CDN 10.12.0 + auth.js）
      staticwebapp.config.json ルーティング設定
    Program.cs
  MoneyBoardApi\
    DataApi.cs                GET/POST /api/data（設定＋月次の集約取得 / 差分の原子的保存）
    DataApi.Access.cs         認証＋アクセス承認（partial・AuthorizeAsync・GET/POST /api/access・承認DTO）
    DataApi.CardImage.cs      POST /api/extract-card（partial・Claude Vision でカード明細スクショ→CardDetail[]。Anthropic SDK・解析部 ParseCardImageResponse は internal でテスト可）
    FirebaseAuth.cs           Firebase IDトークン(JWT/RS256)検証→uid抽出（OIDC構成キャッシュ・AuthBypass対応）
    Program.cs                DI登録 (CosmosClient・AppInsights・FirebaseAuth)
    host.json
    local.settings.json       ※.gitignore対象（`Anthropic__ApiKey` もここに置く）
  MoneyBoardShared\           ※ 役割は下記「MoneyBoardShared の憲章」を参照
    Models.cs                 AppState・Account・FixedCost等
    Ym.cs                     年月(yyyyMM)の値型（パース/整形/比較）
    LedgerMath.cs             月末残高の計算式（実行時 CloseOf と移行で共有しドリフト防止）
    LedgerEngine.cs           残高の前月末連鎖(OpeningOf/CloseOf)・カード明細の月次反映(ExpandCards)・取込重複除外(DedupAgainstEarlierMonths)・固定費計算（純粋ロジック・LedgerService が委譲）
    CardCsvParser.cs          カード明細CSVを種別ごとの列マッピングでパース（JCB/三井住友/PayPay/au PAY/楽天）
    SchemaMigration.cs        スキーマ移行の足場（SchemaVersion管理）
    StorageContracts.cs       GET/POST DTO（DataEnvelope/SettingsPart/MonthPart）
    StatsMath.cs              統計（グラフ）の純粋ロジック（SelectPeriodYms=期間選択。GraphPage が委譲・v1.3.3）
    FixedCostPeriod.cs        固定費の有効期間 StartYm/EndYm の解析・組み立て・表示整形（YearPart/MonthPart/ComposeYm/FmtBound/Summary。FixedCostTab が委譲・v1.3.3）
    Portfolio.cs / PortfolioMath.cs  証券ポートフォリオのモデルと集計計算（Phase 3）。PortfolioMath に CostBasisJpyAsOf（指定日元本・円換算）/ YahooSymbol（日本株 .T 付与）を v1.3.3 で抽出。v2.1.0（issue #57）で PnlPct・DayChangePct・GroupValuationJpy を追加（テスト 118件）。issue #36 で BuildSnapshot（スナップショット構築）を追加（テスト 125件）
  MoneyBoardShared.Tests\     ※ xUnit(net8.0)。LedgerMath / LedgerEngine / PortfolioMath / StatsMath / FixedCostPeriod / CardCsvParser / Ym / SchemaMigration / FixedCost のユニットテスト（計125・`dotnet test`／カバレッジは `--collect:"XPlat Code Coverage"`）
```

### MoneyBoardShared の憲章（役割定義）
- **= フロント(Blazor WASM)／バック(Functions API) の共有ライブラリ。**「Shared＝契約だけ」ではなく **契約＋純粋ドメインロジック** を載せる場と定義する（命名は据え置き）。
  - **共通の契約・モデル**（`Models` / `StorageContracts`）… **API も使用**。
  - **UI/永続化に依存しない純粋ドメインロジック**（`LedgerMath` / `LedgerEngine` / `PortfolioMath` / `StatsMath` / `FixedCostPeriod` / `CardCsvParser` / `SchemaMigration` / `Ym`）… **現状フロント専用だが**「純粋＝テスト可能」な置き場としてここに集約。`MoneyBoardShared.Tests` から検証する。razor から純粋ロジックを抽出するときはここに足し、薄いラッパーで委譲する（v1.3.3 で StatsMath/FixedCostPeriod/PortfolioMath.CostBasisJpyAsOf 等を追加）。
- **持ち込まない**：UI(Razor)・JS interop・HTTP・Cosmos など外部依存。これらは各プロジェクト側に置く。
- 経緯：テスト可能化のため `LedgerService`(フロント) の純粋部分を `LedgerEngine` として切り出した。既に `LedgerMath`/`PortfolioMath` 等の純粋ロジックが Shared にあった慣例に沿った判断（同憲章は `MoneyBoardShared.csproj` 冒頭コメントにも記載）。

### テスト方針
- **対象＝自動テスト可能な純粋ロジック**。**API の CRUD/認証は Cosmos オーケストレーションのため対象外**（結合テスト領域・ROI低）。Blazor UI も自動化困難で対象外。
- **テストプロジェクトは2つ**（いずれも xUnit・net8.0）：
  - `MoneyBoardShared.Tests`：`LedgerMath` / `LedgerEngine`（残高連鎖・ExpandCards・重複除外・固定費）/ `PortfolioMath`（集計・Valuation・CostBasisJpyAsOf・YahooSymbol・PnlPct・DayChangePct・GroupValuationJpy・BuildSnapshot）/ `StatsMath`（期間選択）/ `FixedCostPeriod`（年月の解析・整形）/ `CardCsvParser` / `Ym` / `SchemaMigration` / `FixedCost`（計**125**・v1.3.3 で 63→102・v2.1.0 で 102→118・issue #36 で 118→125）。
  - `MoneyBoardApi.Tests`：API の**純粋ロジックのみ**（計20）。`DataApi.IsStructurallyValid`（保存前データ健全性ガード）／価格パーサ `ParseYahooQuote`・`ParseFundCsv`（取得=HTTPと分離した解析部）／`ParseCardImageResponse`（スクショAI応答JSON→CardDetail[]・日付正規化/金額/不正行スキップ）。テストのため対象は `internal static`＋`InternalsVisibleTo("MoneyBoardApi.Tests")`。
- **カバレッジ**：`--collect:"XPlat Code Coverage"`（coverlet）。ロジック層は行/分岐とも高水準（LedgerMath/SchemaMigration=100% など）。DTO/モデルやCRUD/HTTP部は対象外のため class 全体の数値は薄く出る点に注意（=想定どおり）。**カバレッジ100%でもバグ不在の証明ではない**点は前提として共有。
- **CI**：`.github/workflows/dotnet-test.yml` が dev push / main への PR で**両テストプロジェクト**を `dotnet test`（カバレッジ収集）。main への PR で「必須チェック」に設定すればマージゲートになる（要：Settings→Branches の保護ルール）。

---

## データモデル (MoneyBoardShared/Models.cs)

```csharp
AppState
  ├─ SchemaVersion              // スキーマ版数（移行判定用・現状 3）
  ├─ List<Account> Accounts
  ├─ List<FixedCost> FixedCosts
  ├─ List<Category> Categories
  ├─ List<Card> Cards
  ├─ Dictionary<string,string> CategoryRules  // 店名 → categoryId（自動分類ルール）
  └─ Dictionary<string, MonthData> Months  // key: "yyyyMM"

Account
  ├─ Id, Name, AccountNumber  // AccountNumber は UI 廃止（モデルのみ残置）
  ├─ SortOrder, IsDeleted, IsBonusAccount

FixedCost
  ├─ Id, Name, AccountId, Amount
  ├─ StartYm, EndYm  // "yyyyMM" or "yyyy" or null
  ├─ SortOrder
  └─ List<BonusSetting> BonusSettings

Category
  ├─ Id, Name, Color, SortOrder

Card
  ├─ Id, Name, AccountId, SortOrder
  └─ IsDeleted            // ソフト削除（過去明細の名前引き用に残す。口座と同流儀）

MonthData
  ├─ Dictionary<string, Ledger> Ledgers  // key: accountId
  ├─ List<Transfer> Transfers
  ├─ List<CardDetail> CardDetails
  └─ Dictionary<string,decimal> CardBilled  // key: cardId → 実請求額（リボ/分割で利用額≠引落額の月のみ）

Ledger
  ├─ Confirmed   // 月初残高の起点（開始残高）。起点月のみ使用、他月は前月末から自動計算され無視
  ├─ Salary, Bonus
  ├─ List<Debit> Debits         // 支出（カード由来・固定費由来・手入力）
  ├─ List<IncomeItem> Incomes   // 臨時収入（給料/ボーナス以外）
  ├─ AtmDeposit                 // ATM入金（口座増・資産移動 → 統計には含めない）
  └─ AtmWithdraw                // ATM出金（口座減・資産移動 → 統計には含めない）

IncomeItem
  ├─ Id, Name, Amount     // 臨時収入の1項目（入力名ごとに統計へ内訳表示）

Debit
  ├─ Id, Name, Amount
  ├─ IsFixed, FixedCostId  // 固定費マスタ由来
  └─ CardId               // カード由来（その月のカード明細合計・読み取り専用）

CardDetail
  ├─ Id, CardId, Date, Name, Amount
  └─ CategoryId?          // 未分類は null

Transfer
  ├─ Id, From, To, Amount
```

### 月初残高（OpeningOf）／月末残高（CloseOf）
- **月初残高は前月末から自動連鎖**（`OpeningOf`）。手入力は廃止し、**起点月（前月の同口座台帳が無い最古月）の「開始残高」(`Confirmed`)のみ**入力する。過去月を直すと将来月の月初・末残高が自動追従する。
- 月末残高 `CloseOf` ＝ `月初残高 + 給料 + ボーナス + 臨時収入合計 + ATM入金 − 支出(Debits)合計 − ATM出金 ± 送金`。計算式は `LedgerMath.Close`（実行時と移行で共有）。
- ドリフト補正は残高の手上書きではなく、収入/支出に調整行を足す運用に統一。
- ATM・臨時収入も実際の口座残高を増減させる（残高グラフ②に反映）。**ただし ATM は統計の収入/支出集計からは除外**（専用フィールドのため Debits.Sum 集計に入らない）。

### カード明細 → 月次 支出(Debit) 反映
- 各カードの「その月の CardDetails 合計（＝利用額）」を、紐づく口座の `Debit`（`CardId` 付き）に展開（`ExpandCards`）。
- **リボ/分割対応**: `CardBilled[cardId]`（実請求額）が設定された月は、引き落とし額にそれを使う（未設定は利用額＝一括払い）。**利用額＝統計用**は CardDetails に残し、**請求額＝口座引落**だけを補正。利息/手数料は請求額に含めるか手数料明細で。翌月以降のリボ継続分は明細なしでも請求額を入力可。
- **CSV取込の重複除外**: リボ/分割は完済まで毎月CSVに同じ明細が再掲されるため、取込時に同一カードで**より早い月に既出**（利用日・請求先(正規化:全角ASCII/空白を半角化)・金額が一致）の行を除外（`DedupAgainstEarlierMonths`）。除外件数を取込メッセージに表示。時系列順の取込が前提。
- 月次管理タブでは 💳 付きの読み取り専用行として表示し、クリックでカードタブの該当カードへ展開＋スクロール遷移。
- 取込/手入力時は `CategoryRules`（店名→カテゴリ）で未分類を自動分類（完全一致）。
- **カード削除はソフト削除**（`IsDeleted`）。当月以降の明細・Debit・CardBilled のみ除去し過去は凍結。レコードは残すため統計で削除済みカード名を保持。

---

## 15日サイクル仕様

- 給料日サイクル: 15日〜翌月14日（例: 6/15〜7/14 = 6月）
- 初期表示月: `CurrentCycleStartYm()` (15日以降→当月、14日以前→先月)
- 固定費変更時: 当月サイクル以降の作成済み月次を自動再展開
- 新規ユーザー: 当月サイクルより前の月への「‹」ボタン非活性
- 月次/カードタブの月ナビ横に `CycleInfo` の ⓘ ツールチップ。表示中の月の実期間を明記

---

## 実装済み機能

| 機能 | 状態 |
|------|------|
| 月次管理タブ（収入[給料/ボーナス/ATM入金/臨時収入]・支出[カード/ATM出金/手入力]・送金） | ✅ 完了 |
| 固定費設定タブ（口座フィルター・D&D並び替え） | ✅ 完了 |
| カードタブ（明細手入力＋カードCSV取込[JCB/三井住友/PayPay/au PAY/楽天]・カードごと折りたたみ） | ✅ 完了 |
| マイページタブ（プロフィールヒーロー＋設定カードのグリッド[PC]/折りたたみ[スマホ]・アクセス管理2列色分け・#50リデザイン） | ✅ 完了 |
| グラフページ (7種・期間指定・sticky ヘッダー・内訳ドリルダウン) | ✅ 完了 |
| カテゴリ管理（12色・D&D並び替え） | ✅ 完了 |
| カード管理（口座紐づけ・D&D並び替え・ソフト削除＋確認ダイアログ） | ✅ 完了 |
| カード明細→月次 支出反映（ExpandCards） | ✅ 完了 |
| 月初残高の自動連鎖（前月末→翌月・手入力廃止・起点月のみ開始残高） | ✅ 完了 |
| カード請求額の補正（リボ/分割で利用額≠引落額・CardBilled） | ✅ 完了 |
| カードCSV取込の重複除外（過去月の再掲を除外・除外件数表示） | ✅ 完了 |
| 一括カテゴリ割当（複数選択＋カテゴリ絞り込み＋破棄警告）＋店名ルール自動分類 | ✅ 完了 |
| 統計の内訳ドリルダウン（カテゴリ別/カード別=明細、収入/支出/固定費=項目別合計） | ✅ 完了 |
| 削除確認ダイアログ（口座/固定費/カード） | ✅ 完了 |
| Cosmos DB 移行 / ドキュメント分割（settings/month） | ✅ 完了 |
| データ保全・楽観的並行制御（ETag）・差分保存 | ✅ 完了 |
| Azure SWA デプロイ / CI/CD / Application Insights | ✅ 完了 |
| PWA対応 (favicon・タイトル) | ✅ 完了 |
| フォント統一（Noto Sans JP・Google Fonts） | ✅ 完了 |
| 起動・統計リロード時のローディング制御（スピナー＋操作不可） | ✅ 完了 |
| Firebase認証（Googleログイン）・uid別パーティションでマルチユーザー化 | ✅ 完了（本番反映済み・v1.2.0） |
| アクセス承認制（未承認は承認待ち／オーナーがマイページで承認・拒否・解除） | ✅ 完了（本番反映済み・v1.2.0） |
| Phase 3 証券ポートフォリオ（`/portfolio`・`/api/portfolio`・価格自動取得） | ✅ 完了（本番反映済み・v1.2.0） |
| スマホUI全面最適化（下部タブナビ・カード＋ボトムシート編集・タッチ並べ替え） | ✅ 完了（本番反映済み・v1.3.0） |
| スマホ実機フィードバック修正（スクロールリセット・日付欄・下部バー隠れ・ポートフォリオ最上部固定・ログアウトのマイページ集約） | ✅ 完了（本番反映済み・v1.3.1） |
| ポートフォリオ表示改善（米国株の円/ドル評価切替・円拠出の元本=取得金額・前日比列・現在価格列・口座をバッジ化）＋深いURLの404修正（staticwebapp.config.json）＋認証永続化明示 | ✅ 完了（本番反映済み・v1.3.2） |
| Step4 前クリーンアップ（テスト基盤63→102・CI自動実行・純粋ロジック抽出 StatsMath/FixedCostPeriod/PortfolioMath・巨大razor4枚を code-behind 分離・楽天カード対応・表記統一） | ✅ 完了（本番反映済み・v1.3.3） |
| Phase 4 土台＝カード明細スクショの AI 読み取り（Claude Vision/Haiku 4.5・🤖AIで読取・複数枚＋PC Ctrl+V貼付・X風ステージング・当月へ増分追加） | ✅ 完了（本番反映済み・v1.4.0） |
| 市場指標バー（/portfolio 上部・固定5本のチップ列・前日比%・既存 `/api/quote` 再利用・AI不要） | ✅ 完了（dev・#26） |

---

## UI仕様メモ

### 共通ヘッダー（ブランドタイトル）
- ブランドタイトル「MoneyBoard vX.Y.Z」は共有コンポーネント `MoneyBoard/Components/AppTitle.razor` に集約。版はアセンブリの `InformationalVersion` を読む（全画面で同一供給源）。
- `Subtitle` パラメータに画面名を渡すと「v1.5.0 · ポートフォリオ」のようにドット区切りで併記（`.app-subtitle`）。Home は Subtitle なし。
- **PC**：ブランドは左サイドナビ最上部（`.sidenav-brand`）に出すため、各ページ頭のヘッダー（`.home-head`/`.pf-head`/`.graph-header`）は CSS で非表示（`.app-shell.has-sidenav` 配下）。
- **スマホ**：サイドナビが無いため、各ページ頭の AppTitle を従来どおり表示（#39/#40）。

### ナビゲーション（PC=サイドナビ / スマホ=下部バー）
- **行き先は5系統**：月次管理 / カード / 統計 / 資産 / マイページ。月次・カード・マイページは Home の `/?tab=` 経由、統計=`/graph`・資産=`/portfolio` は専用ルート。
- **PC**：`MoneyBoard/Components/SideNav.razor`（左固定の縦ナビ）。`MainLayout` が PC 時のみレンダーし、`.app-shell.has-sidenav` で「サイドナビ＋本文」の横並び。本文 `.wrap` は PC で `max-width:1200px`。
- **スマホ**：`MoneyBoard/Components/BottomNav.razor`（下部バー）。SideNav と**行き先・ハイライト規則は対**（現在地判定 `IsHomeTab`/`IsRoute` を両者で同形に持つ）。
- 統計への遷移時は保留中のデバウンス保存をフラッシュしてから遷移（`await Svc.SaveAsync()`）。
- 旧導線（Home 上部タブ・月次タブ内の 📊💹 入口ボタン・統計/資産の「← 戻る」）は撤去（PC=サイドナビ／スマホ=下部バーが代替）。

### 月次管理タブ（リデザイン済み・#43）
- **サマリ＝ヘッダー帯**：月末残高合計を**ダークのヒーローカード**（赤字時はピル）で主役化＋ `不足`/`口座数`/`当月の支出` を従える。`当月の支出`＝全口座の `Debits` 合計（ATM・送金は資産移動のため除外＝統計と同流儀）。
- **口座カードは開閉可能**（ヘッダークリック）。**既定はスマホ＝折りたたみ／PC＝展開**（`IsMobile` カスケード値で判定。`_toggled` は「既定からの反転」を保持）。畳むとヘッダー右に月末残高を表示。
- **支出セクションの並び＝固定費 → カード → ATM出金 → 手入力**。**固定費・カード由来はどちらも折りたたみ表示専用ブロック**（`.fold` 共用）。
  - **固定費はインライン編集を廃止＝表示専用**（金額変更は固定費設定タブから）。カード由来も表示専用で、展開した行クリックでカードタブの該当カードへ遷移。
- 月初残高は淡色の表示専用ボックス（起点月のみ「開始残高」を入力）。セクション見出しの右端に小計（収入/支出）。
- 月ナビ横に ⓘ（`CycleInfo`）で当月サイクルの実期間を表示。**スマホは月切り替えを全幅バー＋ⓘ独立ボタン**（CSS のみ・マークアップ共有）。

### 固定費設定タブ
- D&D で並び替え（`⠿`）→ `SortOrder` を連番更新。
- 口座列ヘッダーに **Excel風フィルター**（じょうごSVG＋チェックボックス複数選択）。フィルター中は D&D 無効・全選択で自動解除。表示のみ（非永続）。
- `＋ 追加` → ダイアログ。口座未登録時は警告ダイアログ。金額0でも追加可。

### マイページタブ（レイアウト/質感リデザイン済み・#50）
- 月次（#43）・カード（#49）と同じデザイン言語。上部に**プロフィールヒーロー**（ダーク地・アバター頭文字＋名前/メール＋ローカル開発ピル or ログアウト＋口座/カード/固定費月の指標）。
- **PC＝設定カードの3列グリッド**（`.mypage-grid`／各カードはチップ付き `.set-head`＋枠付き行 `.set-row`・`.set-input`）：1段目＝**アクセス管理を全幅**、2段目＝**口座（1列）｜固定費（2列ぶん）**、3段目＝**カード｜カード明細カテゴリ**。1400px以下で2列・1100px以下で1列に自動縮退。
- 各 `.set-head` の「追加」は `margin-left:auto` でタイトルと左右に振り分け（固定費は spacer 済みで無効化＝従来どおり右側に件数/月合計/フィルタ/追加）。
- 列見出しは**各行の列構造に合わせて整列**（口座＝名前は入力欄の上で中央／「ボーナス受取」はラジオ列72pxの上だけ・×に掛けない、カード＝「引き落とし口座」は select 列の上）。ドラッグハンドルは幅20px固定で見出しスペーサと一致。
- **固定費はカード内で常に2列**（`.fc-list` を `repeat(2,1fr)`・900px以下で1列）。各項目は枠付きカード＋`drag_indicator`／`close`／「期間・ボーナス設定」展開トグルを線アイコン化。
- **アクセス管理（Owner）＝承認待ち（左・オレンジ系）｜承認済み（右・グリーン系）の2列**。各グループは**個別に折りたたみ**（既定は承認待ちが居る時だけ開く）。1100px以下・スマホは縦積み。
- **スマホ＝従来の折りたたみセクション**（list-card＋`BottomSheet` 編集）をマークアップ共有で維持（`IsMobile`＋CSSで出し分け）。
- 口座設定：口座名のみ（口座番号は廃止）・ボーナス受取は1つ・削除時は使用中チェックで警告。カード設定：削除は確認ダイアログ（消える明細件数・合計を明示）。**ロジック・配線（D&D・色パレット・期間/ボーナス・フィルタ・検証ダイアログ）は不変**。

### カードタブ（レイアウト/質感リデザイン済み・#49）
- 月次タブ（#43）と同じデザイン言語。サマリは「当月のカード請求 合計」を**ヒーロー化**（ダーク地）し、ラベル右に**先月比ピル**（前月の全カード利用額合計と比較。増＝`trending_up`＋赤系／減＝`trending_down`＋緑系。前月データは `State.Months` から read-only に参照）。従えて「登録カード」「未分類の明細」（0件=緑チェック／残あり=橙 `sell`）。
- カードは全幅の縦スタック（`.ccard`）。PC は本文全幅で**月次と同じ auto-fill グリッド**（最小640px＝全幅時は2列）。**既定は折りたたみ（PC・スマホとも）**＝畳んだカードが均一に並んで幅を埋め、開いたカードだけ明細テーブル分広がる。
- 明細（PC）は枠＋薄背景で「編集可」と分かる質感（日付/利用先/カテゴリ/金額）。カテゴリは `appearance:none`＋自前 `expand_more`＋色ドット。削除は `close`。0件は `receipt_long` の空状態。スマホは明細をタップカード（`list-card`）化→`BottomSheet` 編集。
- アクション（明細を追加 `add`／取込 `upload_file`／AIで読取 `auto_awesome`）と3ダイアログ（一括カテゴリ／CSV種別／AI読取）は絵文字廃止で Material Symbols ＋ navy プライマリ。
- 一括カテゴリ：利用先グループにチェック→一括設定、カテゴリ絞り込み（未分類抽出）、未適用キャンセル時は破棄確認（**ロジック不変**）。

### 統計ページ（GraphPage）
- タイトル＋期間セレクタを sticky 固定。`/graph` 直接リロード時は読込完了まで操作不可（戻るも無効）。
- 期間：3/6/12ヶ月・全期間＋**期間指定**（月単位）。「対象期間：yyyy年M月 〜 …（Nヶ月）」を明記。
- 7グラフ：①月別支出合計推移 ②口座別月末残高推移 ③収入の内訳推移 ④収入vs支出 ⑤月別固定費合計推移 ⑥カテゴリ別支出 ⑦カード別利用額。
- ドリルダウン（モーダル）：⑥カテゴリ別/⑦カード別＝個別明細（行クリック or ドーナツのスライス選択）、④収入/支出の棒＝項目別期間合計、⑤固定費の棒＝固定費マスタ別合計。

---

## 保留中・未実装

> 認証は **Firebase Authentication で実装済み**（下記「認証（Firebase）」参照）。SWA Standard を使わず SWA Free のまま実現。

| 機能 | issue | 備考 |
|------|------|------|
| カテゴリ自動推定（C案） | #27 | Phase 4 土台再利用（Milestone: Phase 5） |
| 自然言語入力解析 | #28 | Phase 5 |
| 月次コメント生成 | #29 | Phase 5 |
| FABチャット（月次データ更新） | #30 | Phase 5・設計メモは「チャット設計」節 |
| README スクリーンショット追加 | #31 | 残高マスク前提 |

> タスクの最新状況は **GitHub Issues** を参照。本表は索引。

### 設計判断の記録（ADR）
- **全画面のページヘッダーを共通化（#55）** … 資産（#45）で導入した「線アイコン＋画面名＋淡色サブ説明（＋右スロット）」を共通コンポーネント **`PageHeader`** に切り出し、月次/カード/統計/資産/マイページへ展開。サイドナビと同じ字形・画面名で統一。**PC のみ表示**（スマホは従来のブランド `AppTitle`＝`PageHeader` が `IsMobile` で自己非表示）。CSS は `.page-topbar*`（base.css）へ一般化し、資産の `.pf-topbar*` を置換（資産は右スロットに価格更新メタ＋ボタン）。**上端・左端・帯高さを全ページで一致**させるため、包み要素（`.pf-sticky`/`.graph-sticky`/`.wrap` 直下）ごとのマージン相殺差を避けて上マージン0＋`min-height` で固定。版/ブランドの常時表示（#39/#40）は本対応の対象外（別途整理）。
- **統計画面のレイアウト/質感リデザイン＋グラフ明細ダイアログ刷新（#44）** … 外部 Claude Design の設計（`stats-spec.md`＝採用「案A 指標バンド型」／`dialog-spec.md`＝PC「案A モーダル」・スマホ「案C ボトムシート」）を採用し、月次（#43）と同じデザイン言語へ。**集計・期間処理・ドリルダウンのロジックは不変**。
  - **統計本体**：期間選択直下に要約バンド（ヒーロー＝期間収支＋指標4枚＝収入/支出/固定費/貯蓄率）を新設。チャートを **7→5枚** に統合（①月別支出＋④収入vs支出 → 収支コンボ1枚＝収入棒・支出棒＋収支折れ線／⑤固定費推移 → 指標カード化）。続けて〔収入内訳｜口座残高〕〔カテゴリ｜カード〕の2列ブロック。カードは白地＋枠＋極小シャドウ・見出しを `--ink` 太字・Y軸は万単位表示。本文幅は #43 で広げた `.wrap`（1880px）をそのまま活用。
  - **支出の定義差（意図的）**：メインの「支出」棒は従来④と同じ **Debits のみ**（棒タップ→支出内訳ドリルダウンと整合・ロジック不変）。一方、要約の「支出合計」と貯蓄率は **固定費込み** で定義（stats-spec §2 が明示。両者の差は spec の前提）。収支折れ線は可視の棒2本（収入−Debits）と整合させる。
  - **明細ダイアログのフォールド**：当初 #44 の想定変更は GraphPage.razor / graph.css のみだったが、ダイアログも同ページ・同ファイル群のため #44 に取り込み（ユーザー判断）。`DetailDialog` を合計の主役化＋カードバッジ（ドーナツと同色）＋金額バー（そのダイアログ内の最大行で正規化）＋突出タグ（最大行かつ合計の25%以上）＋並べ替え（金額/日付）に刷新。色情報は `DetailRow.SubColor`（カテゴリ明細→カード色／カード明細→カテゴリ色）で供給。`BreakdownDialog`（収支棒・指標カードの内訳）も同トーン化（合計主役・金額バー＝accent）。dashed罫線は全廃。
  - **スマホ＝ボトムシート（案C）**：`.app-shell.is-mobile .dd-overlay/.dd-box` で下から立ち上げ（グラバー＋78vh＋下部固定の全幅 navy ボタン「カテゴリを設定」、ミニ集計チップは高さ優先で省略）。スマホは `.app-scroll` が内部スクローラのため、一覧 `.dd-list` に **`overscroll-behavior: contain`** を付与し、ダイアログ末尾でのスクロールが背後へ連鎖しないようにした。
  - **スコープ外（別issue）**：「選択してカテゴリ設定」は **UI の枠まで**（複数選択→一括カテゴリ更新の実処理は未実装＝現状はプレースホルダ）。突出判定アルゴリズムの精緻化・CSV書き出しも別issue（dialog-spec §8）。
- **マイページのレイアウト/質感リデザイン（#50）** … Claude Design の PC モック（`MoneyBoard MyPage.dc.html` 相当）を起点に、月次（#43）・カード（#49）と同じデザイン言語へ。(1) プロフィールヒーロー、(2) 設定カードのグリッド化、(3) 編集可の質感（チップ付きヘッダー＋枠付き行）、(4) 絵文字を Material Symbols へ。**配線・ロジックは不変**（サブコンポーネントの PC ブランチと見出しのみ刷新し、D&D・色パレット・期間/ボーナス・口座フィルタ・各検証ダイアログは温存）。スマホは共有マークアップ＋CSS出し分けで自動反映（PC設計のみ提供の方針）。
  - **グリッド構成は反復で確定**：当初「アクセス管理を独立カード・固定費を全幅」→ユーザー要望で **3列（アクセス全幅／口座1列＋固定費2列ぶん／カード・カテゴリ各1列）** に。固定費の内部一覧も `auto-fill`→**2列固定**へ。中幅/狭幅は2列→1列に縮退。
  - **「追加」ボタンは右端へ／列見出しは列の上に整列**：ユーザー指摘で、各設定ヘッダーの追加は左右振り分け（`margin-left:auto`、固定費は spacer 既存で無効化）。列見出しはラジオ/ select 列の上に正確に重ねるため、ラジオ列を72pxに拡張（「ボーナス受取」が×に掛からない）・ドラッグハンドルを幅20px固定にして見出しスペーサと一致させた。
  - **アクセス管理は2列＋色分け**：承認待ち（左・オレンジ #fbeede/#b86a18）と承認済み（右・グリーン #e6f2ea/#2c7a52）を横並びにし、各々を個別に折りたたみ可能化（既定は承認待ちが居る時だけ開く）。スマホ・狭幅は縦積み。
- **カードタブのレイアウト/質感リデザイン（#49）** … Claude Design の PC モック（`MoneyBoard Card.dc.html`／`docs/card-spec.md` 相当）を採用し、月次（#43）と同じデザイン言語へ。(1) サマリのヒーロー化（当月のカード請求合計）＋**先月比ピル**（前月比較・read-only `State.Months` 参照・新規ロジック）、(2) 全幅で月次と同じ auto-fill グリッド＋**既定折りたたみ**（PC もスマホも）、(3) 編集可/表示専用の質感分離、(4) アクション・3ダイアログの絵文字を Material Symbols へ。**CSV/AI読取・一括カテゴリ・集計（請求額の口座反映）は不変**。
  - **モックの「行ごとの一括カテゴリ選択ドット」は不採用**：現行の一括カテゴリは*ダイアログ側で利用先ごとに集約*して設定する方式で、行選択は新規の状態・挙動追加（spec のスコープ外＝ロジック変更）になるため。カテゴリ色ドットは従来どおり残す。
  - **幅の方針の変遷**：初版は全幅1列で引き伸ばし過ぎ→Design モックの 1180px 単一カラムへ→「右側が空く・月次と幅を揃えたい」を受け、**最終は月次と同じ全幅 auto-fill グリッド**（既定折りたたみで均一に並ぶため成立）。スマホは共有マークアップ＋CSS出し分け（PC設計のみで反映する方針）。
- **資産（ポートフォリオ）タブのレイアウト/質感リデザイン（#45）** … Claude Design の PC モック（`MoneyBoard Assets.dc.html`／`docs/assets-spec.md` 相当）を採用し、月次（#43）・統計（#44）と同じデザイン言語へ。(1) **総資産をダークのヒーロー化**（含み益/含み損ピル＋**元本/評価損益(率)/当日損益をヒーローに集約**。`.summary/.stat/.hero` を monthly.css から流用）。**当日損益**＝全銘柄前日比の円合計は新規集計、(2) **資産クラスごとにグループ化＋小計**（評価額＋損益小計・米国株のみ円/ドルトグル）、(3) 市場指数（PC＝ヒーロー右に2段＝3+2／スマホ＝ヒーロー下に横スクロール小カード）、(4) 構成ドーナツは**中央に総資産 total（万単位）**＋一覧集約・凡例OFF、(5) 絵文字を Material Symbols（savings/refresh/add/tune/close/drag_indicator/trending_up・down/keyboard_arrow_up・down）へ。**集計・価格取得・USD/JPY換算・ドリルダウン・推移ロジックは不変**。
  - **指標カードは置かずヒーローに集約（反復で確定）**：当初は評価損益／当日損益を独立カードにしたが、動作確認で「評価損益はヒーローに既出・カードが冗長」「固定ヘッダーが縦に長すぎる」を受け、**評価損益(率)・当日損益(率)をヒーロー下部に併記**してカードを撤去。空いた横を使い、**市場指数を1枚の白カード**（中に5本を3列＝3+2・縦罫区切り）にしてヒーロー右へ。スマホは指数をヒーロー下に横スクロール（USD/JPY を先頭チップに）＋#46 で改善予定。
  - **スマホのヒーローは視認性優先で簡素化（PCと表示差分を許容）**：当日損益(率)まで畳み、更新ボタンはラベル右上、**元本・更新時刻はスマホでは非表示**（PCは元本をヒーロー併記・更新メタはトップバー）。
  - **資産構成は PC でクラス別・銘柄別の2ドーナツを横並び（トグル廃止）**：本文が広く1つでは余るため、`BuildComposition` で両データ＋2つのドーナツオプションを同時生成し常時2枚表示。スマホは幅優先でトグル1つ（表示切替のみ・再ビルド不要）。
  - **保有銘柄はモックの「2段組み行（スプレッドシート廃止・spec §7 ★最重要）」を採らず、PC は従来の表形式を維持**：動作確認でユーザー判断（「表形式の方が一覧性・情報量で勝る・"いいとこどり"したい」）。PC は元本/平均取得単価/数量/現在価格/評価額・損益/前日比の表を残しつつ、口座バッジのピル化・ドラッグ/取引/削除を Material Symbols 化・行 hover・損益色規律で**質感だけ新デザインへ**。表ヘッダーの数値見出しは中央揃え（右寄せだと「前日比」が取引ボタン上に寄るため）。スマホは従来どおりタップ式カード（3行）＋▲▼。
  - **要約ヒーロー＋指数までをスクロール追従で固定**：当初トップバーのみ `.pf-sticky` だったが、ユーザー要望で総資産ヒーロー＋指数ごと `.pf-sticky` に内包（リデザイン前の「総資産＋指数が固定」挙動を踏襲）。固定ヘッダー上端にスクロール時の隙間が出ないよう `.pf-sticky` に `padding-top` ＋同量の負 `margin-top` で背景(paper)を被せる。
  - **ダイアログ表示中は背面スクロールをロック**：`.dialog-overlay` が画面に収まる時は CSS だけだとホイールが背面へ抜けるため、`viewport.js` の `setBodyScrollLock` で PC=`html`／スマホ=`.app-scroll` に `.dialog-lock{overflow:hidden}` を付与。Portfolio は「いずれかのダイアログ開（取引/新規/削除確認/破棄確認）」を `OnAfterRenderAsync` で監視してトグル・離脱時(`Dispose`)に解除。
  - **資産構成の一覧は全幅へ引き伸ばさない**：サイドバー化で本文が 1880px と広く、`230px 1fr` だと一覧が間延びするため `230px minmax(320px,760px)` で頭打ち＋左寄せ（スマホは1カラム）。
  - **グループ小計の評価損益＝新規の純粋ロジックを最小追加**：銘柄単位の取得原価（円）が必要なため `PortfolioMath.HoldingCostBasisJpyAsOf`（既存 `CostBasisJpyAsOf` を銘柄単位へ抽出。全体版＝各銘柄の総和で挙動不変）を追加し、ユニットテストで担保（テスト 102→103）。当日損益・含み益率・グループ小計の表示は code-behind のヘルパ（既存の純粋関数を合成）で、計算結線は変えていない。
  - **スマホ＝共有マークアップ＋CSS出し分け**：保有はタップ可能な3行カード＋▲▼並べ替え（`list-card`/`list-card-wrap` を流用）、ヒーロー内に価格更新メタを内包（PCはトップバー右に出す）。PC設計のみ提供→`IsMobile`＋mobile.css で差分のみ出し分けの方針を踏襲。
  - **スコープ外（別issue）**：新規登録/取引（買付・売却・配当）ダイアログの刷新・価格取得/換算ロジックの変更・銘柄並べ替えの永続化仕様（assets-spec §10）。推移チャートは構成（1トグルで2チャート同時制御）は既存のままで質感のみ整え、エリア塗り等の細部は据え置き。
- **月次タブのレイアウト/質感リデザイン＋アイコンを線アイコンに統一（#43）** … 外部の Claude Design に作らせた PC 設計（`docs/redesign-spec.md` 相当）を採用。(1) サマリの主従化（月末残高合計のヒーロー化）、(2) 本文幅を `.app-shell.has-sidenav .wrap` で 1200→**1880px**（ほぼ全幅。**graph/portfolio も同 `.wrap` で広がる**＝#44/#45 はこの幅前提で調整）、(3) 編集可/表示専用の質感分離、**固定費はインライン編集廃止＝表示専用**（変更は固定費設定タブ。挙動変更）、(4) 口座カード/カード/固定費の折りたたみ、(5) **絵文字を全画面で Material Symbols Outlined に置換**（`index.html` にフォント1行＋`base.css .msym`・新色は足さず既存トークン）。
  - **スマホへの適用方針**：マークアップを二重化せず共有し、**PC 設計を入れると mobile.css の調整なしでスマホにも反映される**（`IsMobile` ＋ CSS で差分のみ出し分け）。当初 #43/spec は「PCのみ」想定だったが、別ツリー化を避ける本方針を採用＝**以後 PC 設計の提供だけでスマホ版にも反映**（Design 依頼の手間・トークン削減）。口座カードの既定折りたたみだけは `IsMobile` で出し分け。
- **PC ナビは左サイドバーで統一（#41）** … PC は「Home 上部タブ＋月次タブ内の入口ボタン＋各ページの戻る」で遷移方法が混在していた。スマホの下部バー（5系統一貫）に倣い、**PC は左サイドナビ（`SideNav`）に統一**。`MainLayout` が PC=サイドナビ／スマホ=下部バーを出し分け、行き先・ハイライト規則は両者で同形。ブランドはサイドナビ上部に集約し、各ページ頭のヘッダー・「← 戻る」・📊💹 入口ボタンは撤去。代替案（トップバー／各ページにタブ複製）より、本文幅を確保しつつ全画面で一貫した導線になるため採用。**統計/資産の URL 一貫性（`/graph`・`/portfolio` を他と揃える）は別 issue で継続検討**。
- **日報のアプリ化は見送り（#32・closed）** … 投資SNSの日次日報を MoneyBoard で生成/編集/X投稿/記録簿化する構想は見送り。価値の大半が当日ニュースの web 検索＋分析＝既存 Claude 会話との差分が薄く、X自動投稿も外部・有料・OAuth でリスク過大なため。代替として **市場指標バー（#26）** のみ実装。詳細は issue #32。

> **Claude API 連携（土台）／カード画像（スクショ）読み取り** は **v1.4.0 で本番リリース済み**（下記「実装済み機能」表・「Phase 4」節を参照）。

※ CSVエクスポートは **廃止**（IndexedDB 時代のバックアップ用途。Cosmos DB 常用で不要のため機能削除済み）。

### 解決済みの旧・微細タスク
インラインstyleのCSS化／統計の重複ロード（IsLoadedガード）／AmountInput入力UX（フォーカス中カンマ無し）／CSSクラス命名整理（cat-spend→spend, cat-dot→color-dot）／カード削除時の明細掃除（ソフト削除化）／カード口座変更の反映範囲（当月以降のみ）— いずれも対応済み。

---

## Phase 2 (実装済み)

カード明細管理・カテゴリ管理・カテゴリ別支出グラフ。すべて完了。詳細は「実装済み機能」表を参照。

---

## Phase 3：証券ポートフォリオ（家計簿とは完全独立・`/portfolio`・`/api/portfolio`）

> v1.2.0 で本番リリース済み（main マージ済み・2026-06-17）。家計簿機能とはデータ・画面とも独立。

### 構成・データ
- Cosmos の同一 `/userId` パーティションに `type=portfolio` ドキュメントを追加（ETag 楽観ロック）。
- API は `/api/portfolio`（GET/POST・承認ゲート内）。家計簿の `/api/data` とは別系統。
- 主要ファイル：`MoneyBoardApi/DataApi.Portfolio.cs`（CRUD）、`MoneyBoardApi/DataApi.Quote.cs`（価格プロキシ `/api/quote`）、`MoneyBoardShared/Portfolio.cs`（モデル：`Holding`/`BuyLot`/`Dividend`/`AccountKind` ほか）、`MoneyBoardShared/PortfolioMath.cs`（評価・集計）、`MoneyBoard/Services/FundMaster.cs`（投信マスタ）、`MoneyBoard/Pages/Portfolio.razor`（UI）。

### スライス（実装順）
- **①** 保有銘柄の手動 CRUD（独立 `portfolio` ドキュメント＋ ETag 楽観ロック）。
- **②** 買付/売却/配当の入力と集計（`PortfolioMath`）。一覧を日本株/米国株/投資信託でカテゴリ分け。投信は基準価額÷10,000。
- **③** 価格自動取得＋評価額/評価損益＋投信マスタ。
- **④⑤** 価格スナップショット＋資産構成ドーナツ＋総資産/評価損益/配当の推移。

### 価格取得プロキシ `/api/quote`（CORS 回避でサーバー経由・承認ゲート内）
- **株＋為替＝Yahoo Finance v8**（`query1.finance.yahoo.com/v8/finance/chart/{sym}` の `meta.regularMarketPrice`、為替は `JPY=X`）。
- **投信＝投信協会ライブラリ CSV**（`toushin-lib.fwg.ne.jp/FdsWeb/FDST030000/csv-file-download`）。**基準価額は「協会コード」で一意**（ISIN は有効値必須だが値は不問 →`FallbackIsin` で代替）。Shift-JIS は `Encoding.Latin1` でデコードしてカンマ分割（基準価額列は ASCII 数字）。
- 旧 Stooq は JS ボット検証で死亡 → Yahoo へ移行。
- **市場指標バー（#26）**：固定5本（`^DJI`/`^IXIC`/`^GSPC`/`^N225`/`^KS11`）を保有銘柄の価格取得に相乗りで取得し `/portfolio` 上部にチップ表示（非永続・前日比%）。先頭 `^` は `Uri.EscapeDataString` で URL エンコード済み。⚠️ **TOPIX は対象外**：Yahoo v8 は TOPIX 指数を配信していない（`^TOPX`・`998405.T` は空、`^TPX` は別物＝米国 OPRA オプション指数/USD・105pt 台）。ETF（`1306.T` 等）は前日比%は連動するが絶対値が指数値と桁違いになり日報スクショで誤解を招くため除外。日本株は日経平均で代表。
- ⚠️ **リスク**：投信を協会コードのみで取得しているため、投信協会が将来 ISIN↔協会コードの相互検証を入れると全投信が落ちる（その時は `FundMaster`／`Holding.Isin` に実 ISIN を持たせて復旧）。

### 入力簡略化
- 日本株＝証券コード4桁のみ（取得時 `.T` 自動付与）／米国株＝ティッカー／投信＝標準 `<select>`「投信を選択」（`FundMaster` の銘柄名→協会コード自動入力、無ければ「その他」で協会コード直接入力）。
- 「新規銘柄登録」ダイアログ（外側クリックで閉じない）。

### 評価ロジック（`PortfolioMath`）
- `Valuation`（建て通貨）/ `ValuationJpy`（円換算）。米国株の現在価格は USD 前提。
- **円建て米国株**は USD価格×USD/JPY で円換算 →評価損益(円)に為替損益を内包。**ドル建て**は USD のまま。総資産のみ全部円換算。価格未取得は「—」。
- **価格の自動更新タイミング**：①画面表示時 ②新規登録時 ③取引・設定ダイアログを変更ありで閉じた時（`_txDirty` 集約）。自動呼び出しは失敗/一部未取得メッセージを出さない。

### リリース後の追加改善（v1.2.0 に含む）
- **NISA 口座区分を分割**：`AccountKind` に NisaGrowth/NisaTsumitate を末尾追加（整数保存で既存値不変）。旧 Nisa はレガシー残置。投信マスタに野村世界半導体（協会コード 01313098）追加。
- **D&D 並べ替え**：同一クラス内のみ・`SortOrder` をクラス内スロットで再割当。並べ替えでグラフ再描画しない。
- **取引・設定ダイアログを編集バッファ化**：クローンに編集 →「保存」で初めて反映・外側クリックで閉じない・未保存は破棄確認・ダイアログ内スクロール。
- **配当再投資**：`Dividend.Quantity`（取得コスト $0 で数量加算 →平均取得単価が下がる）。
- **元本推移を取引履歴から全期間化**＋総資産/元本チャートを**日時軸（横軸 yy/MM）**・**期間切替 1W/1M/3M/6M/1Y/ALL**（元本は期間開始日にアンカー）。推移の再描画キーは `_trendRev` に分離。
- **約定為替レート**（`BuyLot.FxRate`）：ドル建て元本(円)=Σ数量×単価×係数×約定レート（未設定は現在レート）。一覧に銘柄別「元本」列、評価損益に損益率(%)。
- **投信元本=受渡金額**（`BuyLot.Amount`）：入力時はその額を取得原価に（口数丸めズレ解消）。`Summarize`/`CostBasisJpyAsOf` は「ロット別実取得原価の合計 →平均取得単価法で按分」（Amount 未設定・ESPP 無しなら従来と同値）。
- **ESPP（従業員株式購入制度）**：`BuyLot.IsEspp`＋`EsppDiscount=0.15`。買付ロット単位で会社補助15%を差し引く。社員フラグ＝`AccessDoc.TsmcEmployees`（Owner マイページでチェック）。`GET /api/portfolio` は**本人ぶんの `IsTsmcEmployee` のみ**返す（Owner 常に true・非社員に UI を出さない）。ESPP 列は TSM ティッカー×社員のみ表示。

### v1.3.2 の追加（本番反映済み・2026-06-20）
- **米国株の円/ドル評価切替**：米国株グループ見出しの円/ドルトグルで、その**グループの評価額・評価損益・前日比の表示通貨**を一括切替（既定=円）。**元本・平均取得単価は建て通貨のまま**。換算は現在レート（`CcyFactor`／`ValDisp`／`UpnlDisp`）、為替未取得は「—」。損益率(%)は通貨非依存（建て通貨ベース）。日本株・投信は常に円。
- **円拠出の米国株＝取得金額(円)を元本に**：円で拠出してドル転約定する積立/ESPP は、後から円換算するとレートでズレる。買付フォームの**「取得金額(¥)」**（`BuyLot.Amount`・従来は投信のみ）を**円建て株でも入力可**にし、入れた円をそのまま元本＝**為替換算なし＝ドリフトなし**。`PortfolioMath.LotCost` が Amount 優先なので計算側は既存のまま。
- **価格通貨とお金通貨の分離**：**米国株の単価・平均取得単価・現在価格は常にドル表記**（`PriceCcySym`＝US は $）。元本・評価額・評価損益は建て通貨（円拠出は円）。買付の単価ラベルは「単価($)」。
- **前日比**：`/api/quote` が前日終値も返す（株＝Yahoo `chartPreviousClose`／投信＝協会CSVの前営業日の基準価額）。`QuoteResponse.PrevClose`/`FundPrevClose`→`PortfolioData.PrevPrices` に保存。一覧は**専用「前日比」列**（金額メイン＋%・%は値動きそのもので通貨非依存）。
- **一覧レイアウト**：**現在価格列を追加**／**口座は列を廃し銘柄名下のバッジ**（`pf-badge-sub`・名前は列幅で省略・バッジは `align-self:flex-start` で文字幅）／**評価額＋評価損益を1列に統合**（評価額の下に損益・金額メイン）／**取引列を固定幅(56px)化**してヘッダーと行のグリッドを一致（以前のヘッダーずれを解消）。列順＝銘柄名・元本・平均取得単価・数量・現在価格・評価額／損益・前日比。スマホのカードは**現在価格のみ**追加表示。

### 残タスク（→ GitHub Issues）
- AI 機能（C案 #27 / 自然言語入力 #28 / 月次コメント #29 / FABチャット #30）は **Phase 4 の土台を再利用**。Milestone「Phase 5: AI機能拡張」。
- 市場指標バー（`/portfolio` 上部）＝ **#26**（設計詳細は issue 本文）。

---

## Phase 4：Claude API 連携（v1.4.0 本番リリース済み・2026-06-21）

> 「キーはサーバー側・WASM に置かない」プロキシ土台を作り、各 AI 機能で再利用する方針。最初の機能＝**カード明細スクショの AI 読み取り**を実装し、v1.4.0（PR #22）で本番リリース。本番 SWA に `Anthropic__ApiKey` 設定済み。

### 方針・構成
- **公式 `Anthropic` C# SDK** を `MoneyBoardApi` に導入（`<PackageReference Include="Anthropic" .../>`）。価格パーサ同様「**取得（API呼び出し）と解析を分離**」し、解析部は `internal static` でユニットテスト可能にする。
- **モデル＝Claude Haiku 4.5**（`Model.ClaudeHaiku4_5`・最安・Vision 対応）。構造化出力（`OutputConfig.Format = JsonOutputFormat{ Schema }`）で `{"items":[{date,name,amount}]}` を強制。
- キーは **`Anthropic__ApiKey` 環境変数**（ローカルは `local.settings.json`・本番は SWA アプリ設定）。未設定時は `CreateAnthropic()` が null を返し、エンドポイントは **503** を返す（キー無しで落とさない）。

### カード明細スクショ読み取り（最初の AI 機能）
- **バックエンド**：`DataApi.CardImage.cs`（partial）。`POST /api/extract-card`（`AuthorizeAsync` ゲート内）。本文 `{cardId, image(base64), mediaType}` を受け、`ExtractCardAsync` が画像＋プロンプトを Haiku に渡し、`ParseCardImageResponse` が応答 JSON を `List<CardDetail>` へ。日付は ISO 正規化（`yyyy-MM-dd`/`yyyy/M/d` 等を許容）、金額はカンマ除去、不正行・合計行はスキップ。本文上限 1.9MB。
- **フロント**：`CardTab` の「🤖 AIで読取」ボタン → ダイアログ。`cardimage.js` が画像を canvas で**長辺1600pxに縮小→JPEG(base64・品質0.85)** 化（本文上限内＋トークン削減）。
  - **複数枚選択**＋**PC は Ctrl+V 貼り付け**（複数回可・**最大10枚**）。**X風ステージング**＝選択/貼付した画像をサムネイルで溜め置き→個別×削除→「読み取り開始」でまとめて取込。
  - 取込は **当月へ増分追加**（CSV と違い全置換しない）。カテゴリ自動分類（`CategoryRules`）→過去月の再掲除外（`DedupAgainstEarlierMonths`）→当月の完全一致除外（再実行・複数枚の二重追加防止）→追加。**AI 結果は一覧で必ず確認・修正する前提**（ダイアログに明記）。
  - スマホは貼り付け非対応のため Ctrl+V ヒントを出さない（ファイル選択は可）。
- **テスト**：`MoneyBoardApi.Tests/CardImageParserTests.cs`（6件）＝行抽出/返金マイナス保持/不正行スキップ/items欠落→空/不正JSON→空/スキーマがvalid JSON。
- **検証状況**：ローカルで合成スクショ→実 API 呼び出しの end-to-end OK（合計行除外・日付/金額正規化を確認）。実カード明細でも日付・金額は全件一致、店名は OCR の表記ゆれが軽微に出る（家計簿用途では実用十分・要確認運用）。本番（v1.4.0）でも `Anthropic__ApiKey` 経由で稼働。

### 今後（この土台を再利用）
- **カテゴリ自動推定（C案）**：カード明細の利用先を一括分類→レビュー後に適用し `CategoryRules` にキャッシュ。
- **月次コメント生成 / 自然言語入力解析 / FABチャット** も同じプロキシ土台（サーバー側キー・取得と解析の分離）の上に追加する。
- 改善余地：店名 OCR の表記ゆれをプロンプトで詰める（ハイフン/長音・英数字を原文どおり等）。トークン増と効果のトレードオフ。

---

## スマホUI最適化（v1.3.0・2026-06-18）

PC幅は従来どおり・スマホ幅のみ最適化する**ハイブリッド方針**（画面ツリーは二重化せず、`IsMobile` とCSSで表示層だけ出し分け）。

- **判定の土台**：`viewport.js`（`matchMedia(640px)`）→ `ViewportService`（`IsMobile`＋変化通知）→ `MainLayout` で `CascadingValue Name="IsMobile"` を全ページへ配布。各コンポーネントは `[CascadingParameter(Name="IsMobile")]` で受ける。ブレークポイント 640px はここが単一定義。
- **アプリシェル**：スマホは縦flex（`.app-shell.is-mobile`）で中身 `.app-scroll` だけ内部スクロール、**下部バーを最下段に在席**（モバイルの `position:fixed` 不具合を回避）。横断スタイルは `wwwroot/css/mobile.css`（最後に読込）。
- **下部5タブナビ**（`BottomNav.razor`）：月次/カード/統計/資産/マイページ。月次/カード/マイページは `"/?tab="`、統計/資産は専用ルート。タブ切替は `Home` の `SetTab` を含め **URL(`?tab=`)経由に統一**（下部バーのハイライト追従のため）。
- **編集パターン（全一覧編集で統一）**：一覧は `.list-card` のサマリーカード → タップで `BottomSheet.razor`（下からせり上がる全幅シート・`sheet-field` でラベル付き縦編集・footに削除/完了・外側クリックでは閉じない）。「＋追加」は新規作成→そのままシート、**未入力（名前空）で閉じたら破棄**。対象：マイページ（口座/カテゴリ/カード設定）・固定費・カード明細。
- **固定費設定**：上部タブから外し**マイページ内の折りたたみに内包**（上部タブは 月次/カード/マイページ の3つ）。
- **資産**：保有をサマリーカード→取引・設定ダイアログ（**削除を追加**）。取引（買付/売却/配当）はラベル付き縦積みカード＋**1行サマリーに折りたたみ**（＋追加で自動展開）。総資産バー縦積み。
- **統計**：内訳一覧の割合(%)列を省略・戻る非表示（グラフは既存レスポンシブ）。
- **月次**：構造的に既対応。行はみ出しは `min-width:0` で修正、収支入力はインライン維持。
- **一括カテゴリ**：スマホは2行レイアウト・ヘッダ圧縮・適用/キャンセル最下部固定・ほぼ全幅。
- **タッチ並べ替え**：HTML5 D&D はタッチ非発火のため、スマホは各カードに **▲▼ 移動ボタン**（固定費=フィルター無し時のみ/カテゴリ/カード/保有=同一クラス内）。`SortOrder` 振り直しは既存D&Dロジックと整合。PCは従来ドラッグ。
- **タッチ基盤**：入力 `font-size:16px`（iOS自動ズーム防止）、ボタン `min-height:40px`。
- 補足：スマホの `<select>` はOSネイティブピッカーで開くため長い選択肢の横溢れ問題なし（PCエミュレータのみ溢れて見える）。CSV取込もスマホ可（OSファイルピッカー→iPhoneは「ファイル」App。CSVを端末に用意できるかがハードル）。

### v1.3.1 実機フィードバック修正（2026-06-19）

Chrome の mobile emulator と **iOS Safari 実機で挙動差**があると判明（特に `position:fixed`／オーバーレイ周り）。実機検証は `http://localhost:5000/?auth=real`（`AuthBypass=false` 時。localhost は既定で無トークン送信＝検証必須だと全API 401 になるため `?auth=real` で実Firebaseログインを強制）。

- **ヘッダーのアカウント表示を撤去 → マイページへ集約**：ヘッダー右の「メール＋ログアウト」常時表示をやめ、`Home.razor` はタイトルのみ。`MyPageTab.razor` の「アカウント」欄にログアウトボタン（**本番＝非バイパス時のみ表示**）。押下時は `ConfirmDialog`（「ログアウトしますか？」）を挟む（誤タップ防止）。※ローカルは `AuthBypass=true` でアカウント欄＝バイパス表示・ログアウトボタン非表示。
- **タブ/ページ遷移でスクロール位置が持ち越される**：スマホは `.app-scroll` だけが内部スクロールするため遷移後も位置維持されていた。`MainLayout` が `NavigationManager.LocationChanged` で `viewport.js` の `scrollAppTop()`（`.app-scroll.scrollTop=0`）を呼ぶ。ただし**月次→カードの「該当カードへスクロール」遷移中（`LedgerService.ScrollToCardId` 有）は抑止**（その後の `scrollIntoView` を上書きしないため）。
- **カード明細の日付入力欄がはみ出る**：iOS の `input[type=date]` はネイティブ装飾の最小幅で枠外へ膨らむ。mobile.css で `-webkit-appearance:none; appearance:none; min-width:0; width:100%`。
- **シート/ダイアログの下部ボタンが下部バーに隠れる**：下部バー（`.botnav`）は in-flow で最下段在席のため、ボトムシートの削除/完了・取引ダイアログの下部ボタンと重なっていた。mobile.css で `.app-shell.is-mobile:has(.sheet-backdrop) .botnav` / `:has(.dialog-overlay) .botnav { display:none }`（**オーバーレイ表示中だけ下部バーを退避**。`:has()` は iOS Safari 15.4+）。
- **ポートフォリオ最上部を固定**：タイトル＋総資産バーを `.pf-sticky`（`position:sticky; top:0`・統計の `graph-sticky` と同流儀）でまとめ、一覧/グラフをスクロールしても総資産が見える（PC/スマホ共通）。`Portfolio.razor` の summary バーを sticky ラッパへ移設。

---

## チャット設計 (未実装)

```
FAB（右下💬・全画面常時表示）
  └─ タップでチャットダイアログ出現

ダイアログ
  ├─ 最小化（—）: セッション継続・FABにドット
  └─ 閉じる（×）: セッション終了

タブ構成
  ├─ 新規（デフォルト）
  └─ 月別（降順・保持期間内）
       └─ 月内は日付区切り線

保持期間: 15日サイクルで2ヶ月保持・自動消去

選択肢
  月次データ更新 / 固定費の変更
  口座の追加 / カード明細の読み込み
  その他（自由入力）
```

---

## 認証プロバイダ設定

### Firebase（現行）
- Firebase プロジェクト: `money-board-jp`（authDomain `money-board-jp.firebaseapp.com`）
- Authentication → Google サインイン有効、Authorized domains に本番 `purple-stone-08eacab00.7.azurestaticapps.net` 追加済み
- Web アプリの firebaseConfig は `wwwroot/js/auth.js` に記載（apiKey は公開値）
- バックエンド検証は `Firebase__ProjectId` のみで成立（サービスアカウント鍵は不要）

### 旧 Google Cloud OAuth（未使用）
- moneyboardjp プロジェクトの OAuth クライアント（SWA-Google 認証用）は**現在未使用**。
  `GOOGLE_CLIENT_ID/SECRET` は SWA に残置（削除可）。

---

## 重要な実装メモ

### 認証（Firebase）・マルチユーザー化
- **方式**: Firebase Authentication(Google)。SWA Free のまま、WASM でログイン→ID トークン(JWT/RS256)取得→
  **独自ヘッダー `X-Firebase-Token` で API に添付**（⚠️ SWA マネージド関数は `Authorization` をプラットフォーム自前トークンで上書きするため使えない。`Authorization: Bearer` はローカル等 SWA 非経由のフォールバック）→Functions(`FirebaseAuth`)で検証（issuer=`https://securetoken.google.com/money-board-jp`、
  audience=`money-board-jp`、署名鍵は securetoken の OIDC 構成を `ConfigurationManager` でキャッシュ）→ `uid` を Cosmos `/userId` に使用。
- **承認制**: `DataApi.AuthorizeAsync`（`DataApi.Access.cs`）で、オーナー(`OwnerEmail`一致＋`email_verified`)／承認済み uid は許可。
  未承認は `access-control` ドキュメントの `pending[]` に記録して **403 + `{status:"pending"}`** を返す。フロントは 403 を
  `AccessPendingException` 化し、Home に「アクセス承認待ち」画面を表示。オーナーはマイページ「アクセス管理」(`GET/POST /api/access`)で承認/拒否/解除。
- **ローカルバイパス**: `AuthBypass=true`(local.settings.json)でバックエンドは JWT 検証せず固定 `default` userId・オーナー扱い。
  フロントは `AuthService` が localhost を検出して Firebase を介さず固定ユーザー（`?auth=real` で実 Firebase 強制）。
  → **ローカル開発はログイン不要・既存 `default` データで作業できる**。
- **本番**: SWA に `Firebase__ProjectId` と `OwnerEmail` を設定済み、`AuthBypass` は未設定（検証必須）。
- **uid パーティション注意**: 認証前は全データが `"default"` パーティション。認証後は各 uid のパーティションになるため、
  本番のオーナーは自分の uid で**1からデータ作成**（旧 `default` は未使用で残置）。
- **Firebase コンソール**: projectId=`money-board-jp`、Google サインイン有効、Authorized domains に本番ドメイン追加済み。
  apiKey はクライアント公開値（auth.js / repo に置いて可）。

### CORS (ローカル開発)
`host.json` の CORS は Functions Isolated では効かない。
`local.settings.json` の `Host.CORS` セクションで設定する。

### SWA ルーティング（深いURLの404対策・v1.3.2）
- `MoneyBoard/wwwroot/staticwebapp.config.json` を追加。`navigationFallback` で `/portfolio` 等の**直接リロード・放置後復帰**時に `index.html` を返す（無いと Azure SWA が自前の白い「404: Not Found」を返す）。`/_framework/*`・`/api/*`・各種静的ファイルは除外。
- `index.html` と `_framework/blazor.boot.json` は `cache-control: no-cache`（デプロイ後に古いブートファイルを掴んで404になるのも抑止）。
- `wwwroot` 直下なので publish 出力のルートに出る。これが無いと「Google認証のせいに見える404」が起きる（認証は無関係）。

### Firebase セッション永続化（v1.3.2）
- `wwwroot/js/auth.js` で `setPersistence(LOCAL)` を明示（既定でも LOCAL）。**会社PC等でブラウザがサイトデータ（localStorage/IndexedDB）を消す設定だと保持できず毎回ログインになる**（Google 側 Cookie のみ残るのはそのため。アプリ側では上書き不可）。ID トークンの1時間期限は `getIdToken()` が自動更新するので全ログアウトの原因ではない。

### JSON シリアライズ
Blazor は camelCase 送信、C# は PascalCase。
`SaveData` では `PropertyNameCaseInsensitive = true` が必須。

### 環境変数の読み方
Functions Isolated では `IConfiguration` ではなく
`Environment.GetEnvironmentVariable()` を使う。
(`__` は Linux 環境での階層区切り)

### 固定費展開タイミング
- `EnsureMonth()` で未登録口座に固定費を自動展開
- `OnFixedCostChanged()` で当月サイクル以降を再展開

### disabled 属性 / ボタン無効表示 (Blazor)
現行 Blazor では `disabled="@boolValue"` で正しく付与/省略される（Home のタブ、MonthlyTab の月ナビ ‹、GraphPage の戻るで使用）。
見た目は CSS の `:disabled`（半透明＋`not-allowed`）で明示：`.btn:disabled`＋`.btn:not(:disabled):hover`、月ナビは `.monthnav button:disabled`（新規ユーザーが過去月へ行けないことを明示）。

### ストレージ構造（ドキュメント分割）
- 1ユーザー = `settings` ＋ `month:yyyyMM`（同一 `/userId` パーティション）。
- `AppStateStore` がメモリ上に AppState 全体を保持し、保存時は前回保存分と比較して
  **変更されたドキュメントだけ**を送る（snapshot-diff）。
- `DataApi` の POST は TransactionalBatch（per-item If-Match）で原子的に保存。

### 保存の信頼性
- **デバウンス＋直列化**: 連続入力は `RequestSave()` で1回に集約、`SaveAsync()` は
  `SemaphoreSlim` で直列化（更新ロスト防止）。
- **楽観的並行制御**: 各ドキュメントの ETag を保持し If-Match 送信。競合（412）時は
  ローカルを上書きせず最新を再読込し、`StateReloadedExternally` で UI に通知。
- **読込失敗時**: State を変更せず保存もしない（空での上書き防止）。UI は再読み込みを促す。

### API ガード（DataApi.SaveData）
- 本文サイズ上限（約1.9MB）＋構造バリデーション（コレクション数の健全性チェック）。

### スキーマ移行
- `AppState.SchemaVersion` と `SchemaMigration.Apply()` が将来の段階移行の足場。**現状 CurrentVersion=3**。
- Phase 2 のカテゴリ/カード/明細、`Ledger.Incomes`/`AtmDeposit`/`AtmWithdraw`・`Card.IsDeleted`・
  `MonthData.CardBilled` はすべて**加算的追加**（旧データはデフォルト値で読める）。
- **v3**: 月初残高を「作成時スナップショット」から「前月末からの自動連鎖」へ変更。非起点月の `Confirmed` が
  参照されなくなるだけで構造的な移行処理は不要（旧 `Ledger.OpeningPinned` 案は採用せず撤去）。

### 月初残高の自動連鎖（OpeningOf）
- `OpeningOf(ym, acct)` ＝ 前月の同口座台帳があれば `CloseOf(前月)`、無ければ（起点月）`Confirmed`。
- `CloseOf` は `OpeningOf` 経由で再帰的に前月へ遡る（個人規模では十分高速・メモ化なし）。
- UI（MonthlyTab）: 起点月のみ開始残高を入力可、それ以外は「前月末より自動」を読み取り専用表示（ピン/トグルなし）。
- `IsOpeningAnchor(ym, acct)` で起点月を判定（前月の同口座台帳の有無）。

### カード請求額（リボ/分割）
- `MonthData.CardBilled[cardId]` に実請求額を保持。`ExpandCards` は「請求額があればそれを、無ければ明細合計＝利用額」を Debit に反映。
- `CardBilledOf`/`SetCardBilled`（null で解除＝一括に復帰）。永続化は `Ledger` 同様 `MonthPart`→`MonthDoc` 全経路に追加済み。
- CardTab に「リボ・分割」トグル→請求額入力。表示は「今月の請求額（口座引落）／利用額（統計）」を併記（繰越の引き算はしない＝マイナス表示を避ける）。

### カード明細の Shift-JIS デコード
- カード明細CSVの文字コードは **PayPay と楽天が UTF-8(BOM可)、その他(JCB/三井住友/au PAY)が Shift-JIS**。Shift-JIS は WASM で .NET の CodePages 依存を避けるため、
  ブラウザの `window.decodeShiftJis`（`TextDecoder('shift_jis')`, storage.js）でデコードしてから `CardCsvParser.Parse` に渡す（UTF-8 は BOM を除去して `Encoding.UTF8`）。

### ApexCharts の options インスタンス共有に注意
- `ApexChartOptions` は描画時にチャート固有の状態を書き込むため、**1インスタンスを複数の
  `<ApexChart>` で共有すると最初の1つしか描画されず、2つ目以降が空になる**。
  GraphPage では各チャートに専用インスタンス（`DebitLineOptions`/`BalanceLineOptions`/… ）を割り当てている。
- 薄色スライスのホバー白飛び対策として、ドーナツの `States.Hover/Active.Filter.Type = darken`（列挙値は小文字）。
- ドリルダウンは `OnDataPointSelection`（`SelectedData<T>` の `SeriesIndex`/`DataPointIndex`）でスライス/棒の選択を拾う。

### ATM・臨時収入の扱い
- ATM入出金は**資産移動**として残高（CloseOf）にのみ反映し、統計の収入/支出集計からは除外（`Debits.Sum` 等に入らない専用フィールド）。
- 臨時収入は給料/ボーナスとは別系列。統計③（収入の内訳）と④（収入vs支出の収入側）に合算/内訳表示する。

### フォント
- 全体を **Noto Sans JP** に統一（`index.html` で Google Fonts 読込、`body` の `font-family`、他は `font: inherit`）。

### 統計ページの sticky / ローディング
- タイトル＋期間セレクタは `.graph-sticky`（`position: sticky; top:0`）で追従。
- `/graph` 直接リロード時は `Loaded`/`LoadFailed` で読込完了までスピナー＋操作不可（戻るも無効）。

