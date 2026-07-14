# MoneyBoard 基本・概要設計（Architecture）

> 本ドキュメントは MoneyBoard の基本設計・概要設計をまとめた技術資料です。
> リポジトリは技術ポートフォリオとして **公開** しています。
> シークレット（接続文字列・APIキー等）は一切記載せず、**Azure アプリ設定／`local.settings.json`（gitignore 済）でのみ管理**します。
> タスク管理は **GitHub Issues**、恒久的な設計判断は本ドキュメントの **ADR 節** で行います。

## プロジェクト概要

**MoneyBoard** - 口座別の収支管理・家計簿Webアプリ（給料日15日サイクル・カード明細・統計）

- GitHub: https://github.com/MyNameIsToshi/moneyboardjp
- 本番URL: https://purple-stone-08eacab00.7.azurestaticapps.net
- API ドキュメント（Swagger UI）: https://mynameistoshi.github.io/moneyboardjp/swagger/
- ローカルパス: `C:\Development\moneyboard\`
- **現行バージョン: `2.15.0`**（2026-07-10 本番リリース。お知らせ一覧/What's NewをタイムラインUIに刷新#130・CUD系ダイアログの背景クリックによる誤クローズを防止#114・全ダイアログ表示中の背景スクロールをロック#115）。`2.14.0`（2026-07-10 本番リリース。固定費（支出・収入）の期間・ボーナス設定をPC追加ダイアログにも対応#128・収入の固定費設定（金額固定/未固定）を追加#95・財布有効時は支出/収入口座の選択肢から財布を除外#124・固定費設定タブの月合計から期限切れ項目を除外#126・マイページの固定費カードを支出/収入に分割し表示順を整理#125・マイページ折りたたみセクションのアイコン/タイトル重複表示を解消#127・portfolio-snapshot-current に銘柄別の前日比を追加#109）。`2.13.0`（2026-07-09 本番リリース。投資信託マスタに「eMAXIS 日経半導体株インデックス」を追加#121・マイページのカテゴリ削除時に使用中チェック＋警告を追加#113・金額入力ボックス（AmountInput）にマイナスが入力できる不具合を修正#112・グラフで参照切れ／未設定CategoryIdの明細を1つの「未分類」に集約#117・口座削除警告ダイアログの対象月を時系列順にソート）。`2.12.0`（2026-07-09 本番リリース。リリースノート・お知らせ通知を追加#38＝ヘッダーのベル一覧＋アプリ更新後の初回モーダル（未読分を全件表示）・未読判定はMoneyBoardShared.AnnouncementMathに切り出しテスト追加（168→178））。`2.11.0`（2026-07-09 本番リリース。固定費に期限切れ折りたたみグループを追加#100（EndBoundが基準月より前を判定・通常一覧から分離・編集不可化・延長で自動復帰）・カード一括カテゴリ設定ダイアログの絞り込み中に手動設定が別項目へ誤反映される不具合を修正#101）。`2.10.0`（2026-07-08 本番リリース。月次管理に「財布（現金）」機能を追加#77＝現金の手元残高・使い道を口座と対称に追跡・ATM入出金の実体化・現金支出のカテゴリ別集計）。`2.9.0`（2026-07-08 本番リリース。毎月金額が変わる「変動費」（水道・電気等）の登録#87・リファクタ（前方一致ルール完了メッセージ生成の共通化#93・RecordSnapshotsのPortfolioData集約#92・AI 2エンドポイントextract-card/classify-categoriesの重複共通化#91）)。`2.8.0`（2026-07-07 本番リリース・PR #94。統計グラフの内訳表示改善（期間選択「当月」追加#88・収入内訳を給料/ボーナス/臨時収入の3系列化#89）とバグ修正（期間指定での再描画不具合#90・棒タップ内訳の集計不具合#85）)。`2.7.0`（2026-07-06 本番リリース・PR #82。月次管理タブの口座カードをヒーロー＋収入/支出/振込ゾーン構成に刷新#81）。`2.6.0`（2026-07-05 本番リリース。PWA化＝manifest・service worker・アイコン一式・ホーム画面追加・アプリ更新検知#76）。`2.5.0`（2026-07-05 本番リリース。ポートフォリオ推移スナップショットのサーバー側自動記録#37（平日クローズ後にcronからHTTP呼び出しで記録・画面を開かない日の欠測防止）・portfolio-snapshot-current に NISA 枠区分を追加#69・推移スナップショットの日付キーをUTCに統一#73）。`2.4.0`（2026-07-04 本番リリース。カード明細カテゴリのAI一括推定#27・利用先前方一致による一括分類#70・一括カテゴリ設定ダイアログのタブ型リデザイン#71）。`2.3.0`（2026-06-30 本番リリース。市場サマリ API #54・ポートフォリオ現況 API #48・Swagger UI 公開 #66）。`2.2.0`（2026-06-27 本番リリース・PR #62。金額マスク機能＝アプリ全体トグル・リロード後も反映・入力欄/ダイアログ/ドーナツにも適用）。`2.1.0`（2026-06-27 本番リリース・PR #60。マイページ口座並べ替え▲▼/D&D・設定行アイコン統一・起点月より過去へ戻れる不具合修正・追加時空データ出現不具合修正）。`2.0.0`（2026-06-26・PR #58。全画面リデザイン＆PCサイドバーナビ＝ヒーロー集約＋線アイコン統一、月次/カード/統計/資産/マイページ全画面刷新、PC左サイドバー、ポートフォリオ市場指数3列グリッド。メジャー番号＝UIの大節目・API非互換なし）。`1.5.0`（2026-06-21・PR #35。市場指標バー=NYダウ/ナスダック/S&P500/日経/KOSPI 5本・AI読取エラー可視化）。`1.4.0`（2026-06-21・PR #22。**Phase 4 土台＝Claude Vision でカード明細スクショをAI読み取り→当月へ取込**。詳細は「Phase 4」節）。`1.3.4`（2026-06-20・PR #21。CIカバレッジをPRコメント＋Job Summaryに出力）／`1.3.3`（PR #20。Step4前クリーンアップ＝テスト基盤整備・純粋ロジック抽出・巨大razor code-behind分離）／`1.3.2`=証券ポートフォリオ表示改善＋深いURL404修正／`1.3.1`=スマホ実機修正／`1.3.0`=スマホUI全面最適化／`1.2.0`=Phase 3 証券ポートフォリオ。
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
- `OwnerUserId` = （オーナーの Firebase uid・**実値は SWA アプリ設定で管理**。`/api/portfolio-snapshot-current` がオーナーのポートフォリオを特定するために使用 #48）
- `InternalApi__SharedSecret` = （内部 API 用共有シークレット・`/api/market-summary` / `/api/portfolio-snapshot-current` / `/api/record-snapshots` 共用。**実値は SWA アプリ設定 ＋ GitHub Secrets（`INTERNAL_API_SHARED_SECRET`）で管理** #48/#54/#37）
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
      FixedCostTab.razor      固定費設定タブ（固定費（支出）＝口座フィルター[Excel風複数選択]・D&D並び替え・期限切れ折りたたみ・ボーナス払い。固定費（収入）（#95）＝同タブ下部に併設・「金額固定」/「金額未固定」を選択可・口座フィルター/期限切れ折りたたみは支出と同挙動を#95フォローアップで踏襲、ボーナス払いのみ対象外）
      MonthlyTab.razor        月次管理タブ（収入[給料/ボーナス/ATM入金/臨時収入]・支出[固定費/カード/ATM出金/手入力]・送金。サマリ=ヒーロー、口座カードは折りたたみ＋ヒーロー/収入・支出・振込ゾーン構成・#43/#81 リデザイン）
      CardTab.razor           カードタブ（明細の手入力/カードCSV取込[JCB/三井住友/PayPay/au PAY/楽天]/AIで読取[スクショ]/一括カテゴリ・カードごと折りたたみ・#49でリデザイン）
      MyPageTab.razor         マイページタブ（プロフィールヒーロー＋設定カードのグリッド[PC]/折りたたみ[スマホ]・アクセス管理は2列色分け・#50でリデザイン）
      CategorySettings.razor  カテゴリ管理（12色・追加/編集/削除[確認ダイアログ・使用中は対象月/件数を明示し「未分類」化を警告した上で削除可・#113]・D&D並び替え）
      CardSettings.razor      カード管理（名前＋口座・追加/削除[確認ダイアログ]・D&D並び替え）
      CycleInfo.razor         月ナビ横の ⓘ ツールチップ（当月=15日サイクルの実期間を表示）
      AmountInput.razor       金額入力共通（フォーカス中はカンマ無し・空欄許可・blurで確定・`AllowNegative`既定false=マイナス禁止／`CardDetail.Amount`のみtrueで返金の手入力を許可・#112）
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
    DataApi.Anthropic.cs      extract-card / classify-categories 共通の Anthropic 基盤（partial・クライアント生成・AnthropicError・SummarizeAnthropicError・構造化出力の定型呼出 CreateStructuredMessageAsync・502/503エラーハンドラ。issue #91 で両エンドポイントの重複を集約）
    DataApi.CardImage.cs      POST /api/extract-card（partial・Claude Vision でカード明細スクショ→CardDetail[]。解析部 ParseCardImageResponse は internal でテスト可）
    DataApi.CategoryClassify.cs POST /api/classify-categories（partial・Claude Haiku(テキストのみ)で利用先一覧→カテゴリID一括分類。解析部 ParseCategoryClassifyResponse は internal でテスト可。カテゴリ一覧はリクエストボディで受け取りCosmosは叩かない）
    FirebaseAuth.cs           Firebase IDトークン(JWT/RS256)検証→uid抽出（OIDC構成キャッシュ・AuthBypass対応）
    Program.cs                DI登録 (CosmosClient・AppInsights・FirebaseAuth)
    host.json
    local.settings.json       ※.gitignore対象（`Anthropic__ApiKey` もここに置く）
  MoneyBoardShared\           ※ 役割は下記「MoneyBoardShared の憲章」を参照
    Models.cs                 AppState・Account・FixedCost・FixedIncome（収入固定費・#95）等
    Ym.cs                     年月(yyyyMM)の値型（パース/整形/比較）
    LedgerMath.cs             月末残高の計算式（実行時 CloseOf と移行で共有しドリフト防止）
    LedgerEngine.cs           残高の前月末連鎖(OpeningOf/CloseOf)・カード明細の月次反映(ExpandCards)・取込重複除外(DedupAgainstEarlierMonths)・支出/収入固定費計算（純粋ロジック・LedgerService が委譲。収入固定費(ExpandFixedIncomes/ReconcileFixedIncomes)は#95）
    CardCsvParser.cs          カード明細CSVを種別ごとの列マッピングでパース（JCB/三井住友/PayPay/au PAY/楽天）
    SchemaMigration.cs        スキーマ移行の足場（SchemaVersion管理）
    StorageContracts.cs       GET/POST DTO（DataEnvelope/SettingsPart/MonthPart）
    StatsMath.cs              統計（グラフ）の純粋ロジック（SelectPeriodYms=期間選択。GraphPage が委譲・v1.3.3。NormalizeCategoryKey=カテゴリ別集計のグルーピングキー正規化・#117で追加）
    FixedCostPeriod.cs        固定費の有効期間 StartYm/EndYm の解析・組み立て・表示整形＋期限切れ判定（YearPart/MonthPart/ComposeYm/FmtBound/Summary/IsExpired/ParseBound。FixedCostTab が委譲・v1.3.3・IsExpiredは#100。ParseBound・Summary(FixedIncome)は#95で FixedCost/FixedIncome 共用に抽出）
    Portfolio.cs / PortfolioMath.cs  証券ポートフォリオのモデルと集計計算（Phase 3）。PortfolioMath に CostBasisJpyAsOf（指定日元本・円換算）/ YahooSymbol（日本株 .T 付与）を v1.3.3 で抽出。v2.1.0（issue #57）で PnlPct・DayChangePct・GroupValuationJpy を追加（テスト 118件）。issue #36 で BuildSnapshot（スナップショット構築）を追加（テスト 125件）
  MoneyBoardShared.Tests\     ※ xUnit(net8.0)。LedgerMath / LedgerEngine / PortfolioMath / StatsMath / FixedCostPeriod / CardCsvParser / Ym / SchemaMigration / FixedCost / FixedIncome / AnnouncementMath のユニットテスト（計206・`dotnet test`／カバレッジは `--collect:"XPlat Code Coverage"`）
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
  - `MoneyBoardShared.Tests`：`LedgerMath` / `LedgerEngine`（残高連鎖・ExpandCards・重複除外・支出/収入固定費）/ `PortfolioMath`（集計・Valuation・CostBasisJpyAsOf・YahooSymbol・PnlPct・DayChangePct・GroupValuationJpy・BuildSnapshot）/ `StatsMath`（期間選択）/ `FixedCostPeriod`（年月の解析・整形。IsExpired/Summaryは FixedCost/FixedIncome 両対応） / `CardCsvParser` / `Ym` / `SchemaMigration`（v4＝CategoryRules正規化統合含む） / `FixedCost` / `FixedIncome`（収入固定費・#95） / `AnnouncementMath`（未読判定・#38）（計**206**・v1.3.3 で 63→102・v2.1.0 で 102→118・issue #36 で 118→125・#27 で 125→130・#38 で 168→178・（間の #100 等の増分を経て）183・#95 で 183→206）。
  - `MoneyBoardApi.Tests`：API の**純粋ロジックのみ**（計36・#54 で 20→26・#27 で 26→35・#95 で 35→36）。`DataApi.IsStructurallyValid`（保存前データ健全性ガード）／価格パーサ `ParseYahooQuote`・`ParseFundCsv`（取得=HTTPと分離した解析部）／`ParseCardImageResponse`（スクショAI応答JSON→CardDetail[]・日付正規化/金額/不正行スキップ）／`ParseCategoryClassifyResponse`（利用先一括分類AI応答JSON→Dictionary<store,categoryId>・null/存在しないID/でっち上げ店名除外・要求店名へ NormalizeStore で突き合わせ表記ゆれ吸収）／`IsAuthorizedSharedSecret`（共有シークレット照合・定数時間比較）。テストのため対象は `internal static`＋`InternalsVisibleTo("MoneyBoardApi.Tests")`。
- **カバレッジ**：`--collect:"XPlat Code Coverage"`（coverlet）。ロジック層は行/分岐とも高水準（LedgerMath/SchemaMigration=100% など）。DTO/モデルやCRUD/HTTP部は対象外のため class 全体の数値は薄く出る点に注意（=想定どおり）。**カバレッジ100%でもバグ不在の証明ではない**点は前提として共有。
- **CI**：`.github/workflows/dotnet-test.yml` が dev push / main への PR で**両テストプロジェクト**を `dotnet test`（カバレッジ収集）。main への PR で「必須チェック」に設定すればマージゲートになる（要：Settings→Branches の保護ルール）。

---

## データモデル (MoneyBoardShared/Models.cs)

```csharp
AppState
  ├─ SchemaVersion              // スキーマ版数（移行判定用・現状 5）
  ├─ List<Account> Accounts
  ├─ List<FixedCost> FixedCosts
  ├─ List<Category> Categories
  ├─ List<Card> Cards
  ├─ Dictionary<string,string> CategoryRules        // 店名 → categoryId（完全一致の自動分類ルール）
  ├─ Dictionary<string,string> CategoryPrefixRules  // 店名の前方一致(prefix) → categoryId（#70）
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
- 取込/手入力時は `CategoryRules`（店名→カテゴリ・完全一致）で未分類を自動分類し、該当しなければ `CategoryPrefixRules`（前方一致・最長優先・大小無視）で分類する（`LedgerEngine.ResolveCategory`・#70）。
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
| 期限切れ固定費の折りたたみグループ化（既定は閉・終了年月の降順・並べ替え不可・`FixedCostPeriod.IsExpired`） | ✅ 完了（dev・リリース待ち・#100） |
| カードタブ（明細手入力＋カードCSV取込[JCB/三井住友/PayPay/au PAY/楽天]・カードごと折りたたみ） | ✅ 完了 |
| マイページタブ（プロフィールヒーロー＋設定カードのグリッド[PC]/折りたたみ[スマホ]・アクセス管理2列色分け・#50リデザイン） | ✅ 完了 |
| グラフページ (7種・期間指定・sticky ヘッダー・内訳ドリルダウン) | ✅ 完了 |
| カテゴリ管理（12色・D&D並び替え・削除は確認ダイアログ＋使用中の場合は対象月/件数と「未分類」化を明示・#113） | ✅ 完了 |
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
| PWA化（manifest・service worker・ホーム画面追加・アプリ更新検知） | ✅ 完了（#76・iOS向けバージョン比較方式#86） |
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
| 市場指標バー（/portfolio 上部・固定5本のチップ列・前日比%・既存 `/api/quote` 再利用・AI不要） | ✅ 完了（本番反映済み・v1.5.0・#26） |
| カテゴリ自動推定（C案・`POST /api/classify-categories`。未分類の利用先を Claude Haiku 4.5 で一括分類→一括カテゴリ画面でレビュー→適用時に `CategoryRules` へキャッシュ。CSV取込・AIスクショ読取後に未分類が残っていれば一括カテゴリ画面を自動オープン＋AI分類まで自動実行、適用はユーザー操作。CategoryRules は `NormalizeStore` 正規化キーで統合し表記ゆれによる分裂を解消、SchemaMigration v3→v4 で既存データも統合） | ✅ 完了（dev・リリース待ち・#27） |
| カテゴリ前方一致ルール（`CategoryPrefixRules`。ETC通行料金など区間ごとに店名が変わる明細を共通の接頭辞でまとめて分類。完全一致優先→前方一致は最長プレフィックス優先・大小無視。一括カテゴリ画面で一覧編集＋プレビュー件数＋最低2文字。登録時に同カテゴリの完全一致ルールを整理／完全一致保存時は前方一致で解決済みなら重複保存しない。SchemaMigration v4→v5） | ✅ 完了（dev・リリース待ち・#70） |
| 推移スナップショットのサーバー側自動記録（`POST /api/record-snapshots`。全ユーザー横断でクロスパーティションクエリ→価格重複排除取得→`PortfolioMath.BuildSnapshot`/`UpsertSnapshot`再利用で1点ずつ記録。GitHub Actions cron `record-snapshots.yml` から平日1回呼び出し。SWA Free の Timer トリガー非対応を cron→HTTP で代替） | ✅ 完了（dev・リリース待ち・#37。要 GitHub Secrets `INTERNAL_API_SHARED_SECRET` 設定） |
| 変動費（`FixedCost.IsVariable`。水道・電気など毎月自動計上だが金額が変わる支出。固定費設定タブでフラグON→月次管理タブでその月の金額を編集可、マスタ額は既定値/初期値。当月に限り手動編集値（`Debit.AmountOverridden`）を保持しマスタ変更で上書きしない、翌月以降は非変動の固定費と同様マスタへ一律追随。あわせて `EnsureMonth` の固定費展開を新規作成月/当月以降に限定するバグ修正（既存の過去月を開いただけで新規固定費が遡って混入しないように）。SchemaMigration v5→v6） | ✅ 完了（dev・リリース待ち・#87） |
| リリースノート・お知らせ通知（`wwwroot/announcements.json` を repo 同梱・デプロイ配信。ヘッダーの🔔ベルに未読バッジ→押下で一覧。案A・タイムライン改良型（バージョン単位のレール＋ノード、本文の【新機能】/【改善】/【修正】を色バッジ分離、種別フィルタ、未読件数ピル・未読ドット）。初回表示は未読分を全件 What's New モーダルで自動提示（1件なら「最新のお知らせ」・複数なら「新着のお知らせ（N件）」）。既読は localStorage の最終既読idで判定・端末ごと独立。未読判定は `AnnouncementMath`（Shared）で純粋ロジック化） | ✅ 完了（dev・リリース待ち・#38→#130） |
| 収入の固定費（`FixedIncome`。支出の `FixedCost` の収入版。「金額固定」＝毎月 `Amount` を自動計上、「金額未固定」（`IsVariable`。例：売電収入）＝項目のみ自動展開し `Amount` は使わず毎月0から月次管理タブで手入力して確定（`IncomeItem.AmountOverridden` で当月分のみ保護、翌月以降は毎月あらためて0からの手入力を求める＝変動費 `FixedCost.IsVariable` とは既定値の扱いが異なる）。固定費設定タブに併設し、口座フィルター（Excel風）・期限切れ折りたたみグループ化・D&D/▲▼並び替え・追加ダイアログは支出の固定費と同挙動を踏襲（ボーナス払いのみ収入側に対応概念が無く対象外）。SchemaMigration v7→v8） | ✅ 完了（dev・リリース待ち・#95） |
| マイページの固定費カード分割（`FixedIncomeTab.razor` を `FixedCostTab.razor` から分離。#95 で同居していた固定費（支出）・（収入）を独立カード化し、PC グリッドの1段目＝口座・カード・カードカテゴリ（3列）、以降＝固定費（支出）・固定費（収入）を各 `.mypage-grid-full` で全幅・各1行に配置（カード内一覧は3列へ拡張）。スマホはマイページ折りたたみを「固定費（支出）」「固定費（収入）」の2セクションに分割） | ✅ 完了（dev・リリース待ち・#125） |
| チュートリアル基盤（コーチマーク基盤＝対象DOM要素のスポットライト＋吹き出し、モーダル図解基盤＝タブ切替＋ステップ送り。`AppState.TutorialSeenVersion` で既読をサーバー保存し初回のみ強制表示）＋PWA追加方法チュートリアル（iOS/Android/PCの3パターンをタブで切替。基盤の初回PoC） | ✅ 完了（dev・リリース待ち・#111） |

---

## UI仕様メモ

### 共通ヘッダー（ブランドタイトル）
- ブランドタイトル「MoneyBoard vX.Y.Z」は共有コンポーネント `MoneyBoard/Components/AppTitle.razor` に集約。版はアセンブリの `InformationalVersion` を読む（全画面で同一供給源）。
- `Subtitle` パラメータに画面名を渡すと「v1.5.0 · ポートフォリオ」のようにドット区切りで併記（`.app-subtitle`）。Home は Subtitle なし。
- **PC**：ブランドは左サイドナビ最上部（`.sidenav-brand`）に出すため、各ページ頭のヘッダー（`.home-head`/`.pf-head`/`.graph-header`）は CSS で非表示（`.app-shell.has-sidenav` 配下）。
- **スマホ**：サイドナビが無いため、各ページ頭の AppTitle を従来どおり表示（#39/#40）。
- **お知らせベル（#38）**：`AppTitle` 右端に🔔＋未読バッジを内蔵。PC/スマホいずれも同一コンポーネントのため二重実装なしで両対応。押下で `AnnouncementListDialog`（新しい順・全件・Markdown本文）を開き既読化。更新後の初回表示（未読あり）は `AnnouncementWhatsNewDialog` を自動表示。データは `MoneyBoard/wwwroot/announcements.json`（repo同梱・デプロイ配信）を `AnnouncementService` が読込・localStorage の最終既読idで未読管理。

### ナビゲーション（PC=サイドナビ / スマホ=下部バー）
- **行き先は5系統**：月次管理 / カード / 統計 / 資産 / マイページ。月次・カード・マイページは Home の `/?tab=` 経由、統計=`/graph`・資産=`/portfolio` は専用ルート。
- **PC**：`MoneyBoard/Components/SideNav.razor`（左固定の縦ナビ）。`MainLayout` が PC 時のみレンダーし、`.app-shell.has-sidenav` で「サイドナビ＋本文」の横並び。本文 `.wrap` は PC で `max-width:1200px`。
- **スマホ**：`MoneyBoard/Components/BottomNav.razor`（下部バー）。SideNav と**行き先・ハイライト規則は対**（現在地判定 `IsHomeTab`/`IsRoute` を両者で同形に持つ）。
- 統計への遷移時は保留中のデバウンス保存をフラッシュしてから遷移（`await Svc.SaveAsync()`）。
- 旧導線（Home 上部タブ・月次タブ内の 📊💹 入口ボタン・統計/資産の「← 戻る」）は撤去（PC=サイドナビ／スマホ=下部バーが代替）。

### 月次管理タブ（リデザイン済み・#43／口座カードは#81でさらに刷新）
- **サマリ＝ヘッダー帯**：月末残高合計を**ダークのヒーローカード**（赤字時はピル）で主役化＋ `不足`/`口座数`/`当月の支出` を従える。`当月の支出`＝全口座の `Debits` 合計（ATM・送金は資産移動のため除外＝統計と同流儀）。
- **口座カードは開閉可能**（ヘッダークリック）。**既定はスマホ＝折りたたみ／PC＝展開**（`IsMobile` カスケード値で判定。`_toggled` は「既定からの反転」を保持）。畳むとヘッダー右に月末残高を表示。
- **口座カード本文は「ヒーロー＋収入/支出/振込ゾーン」構成（#81）**：
  - **ヒーロー**（`.acct-hero`）＝月末残高（見込み）を主役化。当月収支ピル（`MaskedSignedYen` で符号つき表示・黒字=`--ok`緑／赤字=`--bad`赤）＋収支バー（収入:支出の flex 比・`barIn`/`barOut` は文化依存の桁区切りが CSS に混ざらないよう invariant 文字列化）＋補助行（月初/収入/支出）。
  - **収入ゾーン**（緑）／**支出ゾーン**（赤。固定費・カード由来は表示専用の折りたたみ`.fold`、手入力のみ input）／**振込ゾーン**（グレー。送金は編集可、受取は表示専用）。**3ゾーンとも既定=閉**。各ゾーンの開閉は `_incomeToggled`/`_expenseToggled`/`_transferToggled`（展開中の口座を保持）。
  - **固定費の折りたたみ内、変動費（`Debit.IsVariable`・#87）のみ金額を編集可**（🔄アイコン＋ `AmountInput`）。非変動の固定費は従来どおり🔒＋表示専用。
  - **月初残高は起点月のみ手入力ボックス**（navy枠＋「起点」バッジ）を表示。**通常月は自動計算のためヒーロー補助行に畳み**、独立ボックスは出さない（`OpeningOf` は起点月でも `l.Confirmed` を返すため計算はそのまま流用）。
- 月ナビ横に ⓘ（`CycleInfo`）で当月サイクルの実期間を表示。**スマホは月切り替えを全幅バー＋ⓘ独立ボタン**（CSS のみ・マークアップ共有）。

### 固定費設定タブ
- **`FixedCostTab.razor`（支出）／`FixedIncomeTab.razor`（収入）の2コンポーネント**（#125。元は#95で1コンポーネントに同居していたが、マイページのグリッド上で独立カード化するため分割）。年月ヘルパー（`YearRange`/`StartYear`/`EndYear` 等）・`CurrentCycleStart`・`AccountName`・口座未登録警告は両者に少量重複させ、それ以外の期間解析・整形は引き続き `FixedCostPeriod`（Shared・テスト対象）へ委譲する共用のまま。
- D&D で並び替え（`⠿`）→ `SortOrder` を連番更新。
- 口座列ヘッダーに **Excel風フィルター**（じょうごSVG＋チェックボックス複数選択）。フィルター中は D&D 無効・全選択で自動解除。表示のみ（非永続）。
- `＋ 追加` → ダイアログ。口座未登録時は警告ダイアログ。金額0でも追加可。
- **変動費（`IsVariable`・#87）**：期間・ボーナス設定の展開部にチェックボックス「毎月金額が変わる」。ON にすると一覧に🔄バッジを表示し、月次管理タブでその月の金額を編集できるようになる（マスタの金額は既定値/初期値扱い）。
- **期限切れの折りたたみグループ化（#100）**：`EndBound()` が当月サイクル開始（`CurrentCycleStartYm()`）より前の固定費は、通常の一覧から分離し「期限切れ（N件）」の折りたたみグループにまとめる（既定は閉・非永続・`FixedCostPeriod.IsExpired`）。グループ内は終了年月の降順（新しく切れたものが先頭）で固定表示し、D&D／▲▼ の並べ替え対象外。背景色（`--paper`・破線ボーダー）で通常項目と区別する。口座フィルターと併用可（両条件を満たすものだけ表示）。PC/スマホ共通。
- **固定費（収入）（#95）**：同タブ下部に別セクションとして併設。項目名・口座・「金額固定」（`Amount`を毎月自動計上）/「金額未固定」（例：売電収入。項目のみ自動展開し`Amount`は使わず毎月0から月次管理タブで手入力）・開始終了年月・口座フィルター（Excel風）・期限切れ折りたたみグループ化・D&D/▲▼並び替えは固定費（支出）と同じ挙動を踏襲（#95フォローアップ）。**ボーナス払い（`BonusSettings`）のみ対象外**：カード等のボーナス月一括払いを想定した機能で、収入側に自然に対応する概念が無く要望にも含まれないため未実装（必要になれば別途 issue 化）。追加はPC/スマホともダイアログ形式で支出固定費と統一。
  - **編集ロック**：期限切れ中は終了年月以外（名前・口座・金額・変動費チェック・開始年月・ボーナス払い）を disabled にして編集不可。終了年月のみ延長できる。延長して期限切れでなくなると、その場で（シートを閉じずに）編集可能へ戻り、一覧上も通常グループへ自動的に移動する。

### マイページタブ（レイアウト/質感リデザイン済み・#50）
- 月次（#43）・カード（#49）と同じデザイン言語。上部に**プロフィールヒーロー**（ダーク地・アバター頭文字＋名前/メール＋ローカル開発ピル or ログアウト＋口座/カード/固定費月の指標）。
- **PC＝設定カードの3列グリッド**（`.mypage-grid`／各カードはチップ付き `.set-head`＋枠付き行 `.set-row`・`.set-input`）：1段目＝**アクセス管理を全幅**、2段目＝**口座｜カード｜カード明細カテゴリ**（各1列）、3・4段目＝**固定費（支出）・固定費（収入）を各 `.mypage-grid-full` で全幅・各1行**（#125。横並びではなく縦積みにして各カード内の一覧を広く取れるようにした）。1400px以下で `.mypage-grid` は2列・1100px以下で1列に縮退（固定費の2カードはどちらの幅でも常に全幅のまま）。
- 各 `.set-head` の「追加」は `margin-left:auto` でタイトルと左右に振り分け（固定費は spacer 済みで無効化＝従来どおり右側に件数/月合計/フィルタ/追加）。
- 列見出しは**各行の列構造に合わせて整列**（口座＝名前は入力欄の上で中央／「ボーナス受取」はラジオ列72pxの上だけ・×に掛けない、カード＝「引き落とし口座」は select 列の上）。ドラッグハンドルは幅20px固定で見出しスペーサと一致。
- **固定費（支出・収入とも）はカード全幅化（#125）に伴いカード内一覧を3列に拡張**（`.fc-list`。1300px以下で2列・900px以下で1列に段階的縮退）。各項目は枠付きカード＋`drag_indicator`／`close`／「期間・ボーナス設定」展開トグルを線アイコン化。
- **アクセス管理（Owner）＝承認待ち（左・オレンジ系）｜承認済み（右・グリーン系）の2列**。各グループは**個別に折りたたみ**（既定は承認待ちが居る時だけ開く）。1100px以下・スマホは縦積み。
- **スマホ＝従来の折りたたみセクション**（list-card＋`BottomSheet` 編集）をマークアップ共有で維持（`IsMobile`＋CSSで出し分け）。
- 口座設定：口座名のみ（口座番号は廃止）・ボーナス受取は1つ・削除時は使用中チェックで警告。カード設定：削除は確認ダイアログ（消える明細件数・合計を明示）。**ロジック・配線（D&D・色パレット・期間/ボーナス・フィルタ・検証ダイアログ）は不変**。

### カードタブ（レイアウト/質感リデザイン済み・#49）
- 月次タブ（#43）と同じデザイン言語。サマリは「当月のカード請求 合計」を**ヒーロー化**（ダーク地）し、ラベル右に**先月比ピル**（前月の全カード利用額合計と比較。増＝`trending_up`＋赤系／減＝`trending_down`＋緑系。前月データは `State.Months` から read-only に参照）。従えて「登録カード」「未分類の明細」（0件=緑チェック／残あり=橙 `sell`）。
- カードは全幅の縦スタック（`.ccard`）。PC は本文全幅で**月次と同じ auto-fill グリッド**（最小640px＝全幅時は2列）。**既定は折りたたみ（PC・スマホとも）**＝畳んだカードが均一に並んで幅を埋め、開いたカードだけ明細テーブル分広がる。
- 明細（PC）は枠＋薄背景で「編集可」と分かる質感（日付/利用先/カテゴリ/金額）。カテゴリは `appearance:none`＋自前 `expand_more`＋色ドット。削除は `close`。0件は `receipt_long` の空状態。スマホは明細をタップカード（`list-card`）化→`BottomSheet` 編集。
- アクション（明細を追加 `add`／取込 `upload_file`／AIで読取 `auto_awesome`）と3ダイアログ（一括カテゴリ／CSV種別／AI読取）は絵文字廃止で Material Symbols ＋ navy プライマリ。
- 一括カテゴリ：利用先グループにチェック→一括設定、カテゴリ絞り込み（未分類抽出）、未適用キャンセル時は破棄確認（**ロジック不変**）。
- **一括カテゴリダイアログのタブ型リデザイン（#71）**：#70（前方一致ルール）追加で縦に間延びした課題を解消するため、外部 Claude Design（案A折りたたみ／案B2カラム／案C タブ型を比較・採用は案C）を踏まえ、「利用先で設定」「前方一致ルール」の2タブへ分離（既定＝利用先タブ・両タブに件数バッジ常時表示）。一括設定バー＝ベージュ＋「当月のみ」ピル（一時操作）、前方一致ルール＝navy淡バナー（恒久ルール）で地色を分け役割を視覚的に区別。前方一致ルールの状態メッセージは追加フォーム直下に**固定高さ(26px)のスロット**を確保し、プレビュー件数／バリデーションエラー／完了メッセージの切替でレイアウトが動かないようにした。未分類が残る場合は前方一致ルールタブ内に「利用先で設定へ」の戻り導線を表示。**集計・保存タイミング・バリデーション文言・API・利用先ごとに集約する一括適用方式は不変**（構造・質感・情報の優先度のみ変更）。
- **#71 のスマホ追補修正**：#71 は当初「共有マークアップ＋CSSで自動反映」としたが、PC用グリッド（列固定幅・横並びの一括設定バー等）をスマホへそのまま流用すると崩れる（列見出し潰れ・入力欄圧迫・AIで分類ボタンの位置崩壊・前方一致ルールタブが `max-height` を持たずダイアログごと伸びて破綻）ことが判明。Claude Design から追加でスマホ専用モック（`2a`/`2b`）を受け、**方針を変更**して `mobile.css` 側に別レイアウトを追加（**設計判断としてissueの当初想定と乖離**）。ダイアログ自体は下からのボトムシート化（角丸上20px・グラバー・`:has()`でこのダイアログのみ overlay を下寄せ）。利用先一覧はスマホで列見出しを非表示にし、代わりに `IsMobile` 分岐で「全選択＋金額が大きい順」の1行（`CardTab.razor`）に置き換え。前方一致ルールタブは `.prefix-rules` を独立スクロール領域化し、ルール行・追加フォームをそれぞれ2段のカード/縦積みへ（grid-template-areas の再配置）。
- **#71 の追加UX改善（レビュー指摘対応）**：①前方一致文字列を chip 見た目の `<input>` にして**その場で改名可能**に（`RenamePrefixRule`。`CategoryPrefixRules` は prefix文字列を他のどこからも参照しないキー→カテゴリIdの辞書のため、改名しても既に分類済みの明細には影響しない＝削除→再作成と等価だが手間を削減）。追加(`AddPrefixRule`)と改名で共通処理を `CommitPrefixRule`（完全一致ルールのクリーンアップ＋当月未分類の即時分類）に抽出。②「ルールを追加」フォームをルール一覧より**上**に配置（ルールが増えるほど最下部までスクロールが必要だった問題を解消。PC/スマホ共通、マークアップ順序の変更のみ）。③スマホの利用先一覧に**並び替え**（利用先/件数/金額/カテゴリのセレクト＋昇順降順トグル。既存 `SetBulkSort` を再利用しロジック追加なし）。④スマホの「利用先で設定」タブ下部の注意書きは**非表示**（`IsMobile` で画面縦幅を圧迫するため削除、PCでは従来どおり表示）。
- **#71 の code-review 指摘対応**（commit前）：①`OpenBulk` が `_prefixRenameError`/`_prefixMessage` をリセットしておらず、ダイアログ再オープン時に前回の改名エラー/追加完了メッセージが亡霊のように残る不具合を修正（あわせて別行の編集・削除・追加操作でも古い改名エラーをクリア）。②`UnclassifiedStoreCount`（戻り導線の件数）が `BulkSelection`（ダイアログの未適用選択）基準だったため、一部だけ分類済み・一部未分類な「混在」利用先（`KeepSentinel`）を見落とし、未分類が残っていても戻り導線が消えることがあった → 実データ（`Mo.CardDetails`）基準に修正。③`RenamePrefixRule` が当月明細を無言で再分類していた（`CommitPrefixRule` の戻り値を捨てていた）ため、`AddPrefixRule` と同様の完了メッセージを表示するよう追加。④改名で prefix の対象範囲が狭まる/変わる場合、旧prefixが分類していた明細のカテゴリは残るのに、それを再現するルールがもう存在せず「次回取込では未分類に戻る」という永続状態と表示の乖離があったため、対象を再解決してから新ルールを適用するよう修正（ロジックが少し複雑になったが、データ整合性を優先）。⑤スマホの一括設定バーは `display:contents`＋`order` でPC側マークアップをCSSだけで並べ替えていたが、PC側の構造変更に追従できず壊れやすいため、`RenderFragment`（見出し／カテゴリ選択／適用ボタン／AIで分類／絞り込み）を1か所だけ定義し `@if (IsMobile)` でDOM順そのものを組み替える構成に変更（内容の二重管理を避けつつ、CSSでのDOM順操作をやめてフォーカス順と表示順を一致させた）。

### 統計ページ（GraphPage）
- タイトル＋期間セレクタを sticky 固定。`/graph` 直接リロード時は読込完了まで操作不可（戻るも無効）。
- 期間：当月/3/6/12ヶ月・全期間＋**期間指定**（月単位）。「対象期間：yyyy年M月 〜 …（Nヶ月）」を明記。**「当月」は数値期間(count=1・TakeLast)を使わず専用の `"current"` 期間**（`StatsMath.SelectPeriodYms` が `LedgerService.CurrentCycleStartYm()` と一致する月だけをピンポイントで返す）（#88）。理由：月次管理で先の月を先行作成済みだと `TakeLast(1)` は「データがある最後の月」を拾い、実際の給料サイクル（15日〜14日起点）とズレるため（動作確認で発覚し設計変更）。**数値期間（3/6/12ヶ月）も #88 と同じ理由で未来月を除外**：`currentYm` を上限に `Where` で絞り込んでから `TakeLast` することで、固定費先行展開等で作成済みの未来月を直近N件から除外する（`currentYm` 未指定時は従来どおり全件対象。全期間/カスタムは変更なし＝未来月を確認したいときの受け皿として残す）（#119）。
- 7グラフ：①月別支出合計推移 ②口座別月末残高推移 ③収入の内訳推移 ④収入vs支出 ⑤月別固定費合計推移 ⑥カテゴリ別支出 ⑦カード別利用額。
- ドリルダウン（モーダル）：⑥カテゴリ別/⑦カード別＝個別明細（行クリック or ドーナツのスライス選択）、④収入/支出の棒＝項目別期間合計、⑤固定費の棒＝固定費マスタ別合計、③収入の内訳推移の棒＝タップした月の収入内訳（④と同じダイアログ、#89）。
- ⑥カテゴリ別支出の集計キーは `StatsMath.NormalizeCategoryKey` で正規化する：`CategoryId` が未設定、または参照先カテゴリが存在しない（削除済み等の参照切れ）場合は両方とも空文字列キーに統一し、「未分類」が複数行に分裂しないよう1グループへ集約する（#117）。

---

## 保留中・未実装

> 認証は **Firebase Authentication で実装済み**（下記「認証（Firebase）」参照）。SWA Standard を使わず SWA Free のまま実現。

| 機能 | issue | 備考 |
|------|------|------|
| README スクリーンショット追加 | #31 | 残高マスク前提 |

> 自然言語入力解析（#28）／月次コメント生成（#29）／FABチャット（#30）は **見送り・クローズ済み**。理由は下記 ADR 参照。

> タスクの最新状況は **GitHub Issues** を参照。本表は索引。

### 設計判断の記録（ADR）
- **全画面のページヘッダーを共通化（#55）** … 資産（#45）で導入した「線アイコン＋画面名＋淡色サブ説明（＋右スロット）」を共通コンポーネント **`PageHeader`** に切り出し、月次/カード/統計/資産/マイページへ展開。サイドナビと同じ字形・画面名で統一。**PC のみ表示**（スマホは従来のブランド `AppTitle`＝`PageHeader` が `IsMobile` で自己非表示）。CSS は `.page-topbar*`（base.css）へ一般化し、資産の `.pf-topbar*` を置換（資産は右スロットに価格更新メタ＋ボタン）。**上端・左端・帯高さを全ページで一致**させるため、包み要素（`.pf-sticky`/`.graph-sticky`/`.wrap` 直下）ごとのマージン相殺差を避けて上マージン0＋`min-height` で固定。版/ブランドの常時表示（#39/#40）は本対応の対象外（別途整理）。
- **統計画面のレイアウト/質感リデザイン＋グラフ明細ダイアログ刷新（#44）** … 外部 Claude Design の設計（`stats-spec.md`＝採用「案A 指標バンド型」／`dialog-spec.md`＝PC「案A モーダル」・スマホ「案C ボトムシート」）を採用し、月次（#43）と同じデザイン言語へ。**集計・期間処理・ドリルダウンのロジックは不変**。
  - **統計本体**：期間選択直下に要約バンド（ヒーロー＝期間収支＋指標4枚＝収入/支出/固定費/貯蓄率）を新設。チャートを **7→5枚** に統合（①月別支出＋④収入vs支出 → 収支コンボ1枚＝収入棒・支出棒＋収支折れ線／⑤固定費推移 → 指標カード化）。続けて〔収入内訳｜口座残高〕〔カテゴリ｜カード〕の2列ブロック。カードは白地＋枠＋極小シャドウ・見出しを `--ink` 太字・Y軸は万単位表示。本文幅は #43 で広げた `.wrap`（1880px）をそのまま活用。
  - **支出の定義（#84 で二重計上・マスタ再計算ズレを修正）**：メインの「支出」棒・要約の「支出合計」・貯蓄率、および指標カードの「固定費」欄（`FixedTotal`）・固定費内訳ダイアログは、いずれも各月の **`Debits` の実記帳額**から集計する（棒タップ→支出内訳ドリルダウンと整合）。固定費は `LedgerService.ExpandFixedCosts()` が月初生成時に `IsFixed=true` の `Debit` として `Debits` へ記帳済みのため、支出合計（Debits 全件）は既に固定費込みで、`FixedTotal`（`IsFixed` 分の合計）はその部分集合＝必ず支出合計以下。**固定費マスタから再計算しない**のが要点：マスタ変更後は過去月の `Debits` は据え置き（`OnFixedCostChanged` は現在/将来サイクルのみ再展開）のため、マスタ再計算すると過去月で実績（月次管理の表示）と乖離し二重計上・100%超が生じる。したがって支出合計に `FixedTotal` を加算しない（旧実装は `ExpenseTotal = Debits合計 + FixedTotal` で二重計上、かつ固定費系列をマスタ再計算していた）。収支折れ線は可視の棒2本（収入−Debits）と整合。
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
- **会話型 AI 機能（自然言語入力解析・月次コメント生成・FABチャット）は見送り（#28/#29/#30・closed）** … #27（カテゴリAI一括推定）・AI読取スクショ・#70（前方一致ルール）の実績から、このアプリで価値が出るのは「機能に埋め込まれた自動化（ボタン一発でAI処理）」であり、会話型UI（自由入力解析・チャット常駐）は既存のUXパターンと方向性が合わないと判断。特に FABチャット（#30）は新規UI要素（常駐FAB・セッション管理・保持期間）の実装コストが大きい割に、既存の「機能組み込み型」体験と比べて得られる価値が見えにくい。今後 AI 機能を追加する際は会話型ではなく機能組み込み型を前提とする。チャットUIの設計メモ（旧「チャット設計」節）は本方針転換に伴い削除。
- **日報のアプリ化は見送り（#32・closed）** … 投資SNSの日次日報を MoneyBoard で生成/編集/X投稿/記録簿化する構想は見送り。価値の大半が当日ニュースの web 検索＋分析＝既存 Claude 会話との差分が薄く、X自動投稿も外部・有料・OAuth でリスク過大なため。代替として **市場指標バー（#26）** のみ実装。詳細は issue #32。
- **ポートフォリオ現況 API はオーナー固定・共有シークレット認証（#48）** … 日報生成向けの `/api/portfolio-snapshot-current` は日報対象がオーナー1名に固定のため、ユーザー JWT ではなく共有シークレット（`InternalApi__SharedSecret` / `X-Internal-Secret`）を採用。オーナーの userId は `OwnerUserId` 環境変数で直接指定（`OwnerEmail` からの検索は Cosmos クロスパーティションクエリが必要になり不要な複雑性を招くため採らない）。`/api/market-summary` と同じ認証パターンを踏襲しシークレットを共有。
  - **#37 を待たずに #48 を実装**：issue #48 は「#37 の記録処理を呼ぶ」と記述していたが、#37 は全ユーザーバッチ処理 × GitHub Actions cron の文脈。#48 は単一ユーザー × 日報スキル呼び出しであり、共通の内部ロジック（`FetchPriceAsync` + `BuildSnapshot`）を直接呼べば #37 の HTTP エンドポイントは不要と判断。issue にコメント済み。
- **推移スナップショットの自動記録は SWA Free 据え置き＋ GitHub Actions cron→HTTP（#37）** … SWA Free の managed Functions は HTTP トリガーのみ対応（Timer トリガーは SWA Standard=有料）。Timer 相当を実現するため、Azure Functions 側に Timer を持たせず、**GitHub Actions の `schedule(cron)` から `POST /api/record-snapshots` を叩く**構成を採用（追加コスト無し）。認証は `/api/market-summary`/`/api/portfolio-snapshot-current` と同じ共有シークレットを再利用し、専用の認証系統を増やさない。全ユーザー分の価格取得は銘柄単位で重複排除してから1回だけ行い（ユーザーごとの個別取得にしない）、日々の cron 実行が外部API（Yahoo/投信協会）へ与える負荷を抑える。
- **OpenAPI 仕様を手書き YAML + Swagger UI（GitHub Pages）で公開（#66）** … Azure Functions Isolated は ASP.NET Core と異なり Swashbuckle が直接使えない（Isolated は HTTP middleware を持たずビルド時のリフレクションが複雑）。NSwag や Microsoft.Azure.Functions.Worker.Extensions.OpenApi も追加パッケージ・スタートアップ変更を要し、ポートフォリオ用途（実際にトライアウトするわけではない）に対してコストが大きい。そのため **`docs/swagger/openapi.yaml` を手書き**してリポジトリに置き、GitHub Pages（`docs/` フォルダ）で Swagger UI（CDN）を介して公開する方式を採用。openapi.yaml はコードとともにメンテ・CI やパッケージの追加なし。GitHub Pages は repo 設定で `main` ブランチの `docs/` フォルダを Source に設定する（一度限りの手作業）。公開 URL: https://mynameistoshi.github.io/moneyboardjp/swagger/
- **iOS/Android 展開は PWA 化のみを段階的に採用、.NET MAUI Hybrid は見送り（#67・技術スパイク）** … MoneyBoard は不特定多数への配布を想定しない**承認制の個人利用アプリ**（オーナーが承認した少数ユーザーのみ）であり、ストア掲載による発見性（レビュー・検索・カテゴリランキング）には価値がない。この前提で2経路を比較した。
  - **PWA化（manifest + service worker）**：`wwwroot/manifest.json`＋`service-worker.js`＋`apple-touch-icon`を追加し、iOS Safari／Android Chrome の「ホーム画面に追加」でアイコン起動・スタンドアロン表示（ブラウザUIなし）を実現できる。**コスト0・ストアアカウント不要**。iOS は自動インストールプロンプト非対応（共有→「ホーム画面に追加」の手動操作が必要）、Background Sync 非対応、Web Push は iOS 16.4+ でホーム画面追加後のみ対応（VAPID等サーバー実装が別途必要・今回は対象外）。いずれも MoneyBoard の使い方（手動更新・サーバー側 Cosmos DB が正のデータソースでオフラインキャッシュはアプリシェルのみ）と衝突しない。SWA の静的ホスティングのまま追加ファイルを配信するだけで、CI/CD・デプロイ構成の変更は不要。
  - **.NET MAUI Hybrid（BlazorWebView）**：既存の `MoneyBoard.csproj`（単一 Blazor WASM App）を Razor コンポーネントライブラリ＋MAUI ホストへ分割する構造変更が必要。iOS ビルドには**ネットワーク接続された Mac＋Xcode**が必須（GitHub Actions の macOS runner で代替可、public repo のため Actions 分数は無料）。ただし**コード署名に Apple Developer Program（$99/年）の証明書・プロビジョニングプロファイルを CI シークレットとして管理**する必要があり、現行の `dotnet-test.yml` に対して構成・秘密情報管理が大幅に増える。App Store 配布には審査（提出後 1〜3 日程度）が挟まり、**本リポジトリの「small diff→即 SWA 自動デプロイ」という速いケイデンスと相性が悪い**。Google Play は $25 の一度払いで Apple より軽いが、いずれにせよ得られる利点（プッシュ通知・生体認証・ストア発見性）は上記の通り本アプリの利用形態では価値が薄く、$99/年の継続コストと CI 複雑化に見合わない。
  - **結論**：PWA化（manifest・service worker・アイコン一式）のみを別 issue で段階実装する。Web Push・MAUI Hybrid は不採用。将来ストア配布や生体認証など明確なネイティブ要件が生じた場合のみ MAUI Hybrid を再検討する。
- **PWA化の実装方式（#76・#67の実装）** … Blazor WASM SDK 標準の `ServiceWorker` MSBuild 連携（`MoneyBoard.csproj` に `<ServiceWorkerAssetsManifest>` プロパティ＋ `<ServiceWorker Include="wwwroot\service-worker.js" PublishedContent="wwwroot\service-worker.published.js" />`）を採用。追加 NuGet パッケージ・ビルドスクリプト変更は不要（`dotnet publish` 時に `service-worker.js` が published 版へ差し替わり、`service-worker-assets.js`＝静的アセットのハッシュ一覧が自動生成される）。
  - **キャッシュ対象は静的アセットのみ・`/api/*` は明示的に除外**：`service-worker.published.js` の `onFetch` で `url.pathname.startsWith('/api/')` を最優先チェックし常にネットワークへ素通し。Cosmos DB が正のデータソースであるという既存方針（申し送り事項）と矛盾しないための必須ガード。
  - **アイコンは絵文字 💰（既存 favicon と統一）＋既存アクセント色 `--accent`（`#1f3a5f`）**：新規ブランド色を増やさず、`base.css` の既存トークンをそのまま icons 生成に流用。実ファイルは PowerShell + `System.Drawing` でオンザフライ生成（`icon-192/512.png`・`icon-maskable-192/512.png`・`apple-touch-icon.png`）。追加の画像編集ツールやライセンス済みアセットへの依存なし。
  - **アプリ更新検知→再読み込み導線**：`js/pwa.js` が新 service worker の `installed`（かつ既存 controller あり＝初回インストールでなく更新）を検知して `#pwa-update-toast`（`base.css`。既存 `#blazor-error-ui` と同じ「画面下部固定バー」パターンを踏襲）を表示。クリックで `postMessage({type:'SKIP_WAITING'})` → 新 SW が `self.skipWaiting()` → `controllerchange` を検知して自動リロード。ユーザー操作なしの強制リロードは避け、明示的なクリックを起点にした（作業中データを失わせないため）。
    - **他タブを巻き込まない**：`skipWaiting()` は同一オリジンの全タブに反映され `controllerchange` も全タブで発火するため、`updateRequested` フラグで「このタブでクリックしたか」を管理し、クリックしていない裏タブは無操作リロードしない（未保存のデバウンス編集を保護）。
    - **前回セッションから waiting のままの SW も検知**：`updatefound` は今回のロード後に新規インストールが始まった時のみ発火するため、`register()` 直後に `registration.waiting` を確認し、既にインストール済みで待機中の SW があればその場でトーストを出す（クリック前にタブを閉じた場合に更新が握り潰されないように）。
  - **iOS実機ではSWイベント方式が機能せず、バージョン比較による事後通知を追加（#86）**：iOS実機でv2.6.0起動中にv2.7.0をデプロイし(1)完全終了→再起動、(2)終了せずホーム画面往復の両方で検証したが、いずれも上記の`installed`/`waiting`検知が発火せずサイレントに更新されていた。iOSのスタンドアロンPWAはバックグラウンド復帰時にWKWebViewが再読込されることが多く、`visibilitychange`起点の`registration.update()`も実質効いていないと推測される。SWのライフサイクルイベントに一切依存しない方式として、**起動時にlocalStorageへ保存した前回バージョンと現在のAppVersionを比較し、差異があれば「vX.Y.Zに更新されました」と事後報告する**方式を追加した（`MoneyBoardShared.AppVersionMath.ShouldNotifyUpdate`＝純粋ロジック、`MoneyBoard.Services.AppUpdateService`が`localStorage`比較を担当、`MainLayout`が起動時に`AppUpdateService.InitAsync(AppVersionProvider.Current)`を呼び`#app-update-toast`を表示）。初回起動（前回バージョン未記録）は通知しない。強制リロードは行わず、表示のみ（他タブへの影響なし・未保存データを壊さない、という#76の既存方針を踏襲）。既存のSWイベント方式（`pwa.js`のトースト）はイベントが発火する環境（Desktop等）ではそのまま活かすため撤去せず、pwa.jsの「更新する」クリック時に`mb_pwa_manual_reload`フラグ（クリック時刻つき）を立てて直後の自タブリロードで新方式の通知が二重に出ないよう抑止している。フラグに時刻を持たせているのは、reloadが完了しなかった場合（タブを閉じた・iOSでcontrollerchangeが発火しない等）にフラグが残り続け、無関係な将来の更新まで誤って抑止してしまうのを防ぐため（`AppVersionMath.IsWithinManualReloadWindow`＝30秒の時間枠内のみ抑止・純粋ロジック、コードレビューで発覚し修正）。両トーストが同時に表示されるケースに備え、`#app-update-toast`は`body:has(#pwa-update-toast)`（既存の`mobile.css`と同じ`:has()`パターン）で1段上へ積むCSSも追加した。`AppVersionMath.FormatDisplayVersion`（"vX.Y.Z"整形）は元々`AppTitle.razor`にインライン実装されていたロジックを、両者で共有するため純粋関数として切り出したもの（挙動不変）。localStorageの読み書き（例外握りつぶし）は`AnnouncementService`と重複していたため`MoneyBoard.Services.LocalStorage`として共通化した。
  - **iOS 16.4 未満のスタンドアロン対応**：manifest の `display:standalone` は iOS 16.4+ でのみ有効なため、`index.html` に旧来の `apple-mobile-web-app-capable`（`content="yes"`）を追加。`apple-mobile-web-app-status-bar-style` は `black-translucent`（透過）だと `viewport-fit=cover` とヘッダー側の `env(safe-area-inset-top)` 対応が別途必要になり、本アプリは bottom 側の safe-area（botnav）のみ対応済みで top 側は未対応のため、既存レイアウトと衝突しないよう `default`（不透明）を採用。
- **収入の内訳推移＝給料/ボーナス/臨時収入の3系列固定＋棒タップで内訳ダイアログ（#89）** … 従来は臨時収入を入力名ごとに個別系列化しており、月ごとに顔ぶれが変わる臨時収入（家賃収入・還元・単発イベント等）が増えるほど凡例・積み上げ棒とも同系色（`IncomeGold` 一色）で判別不能になっていた。検討した3案（A: 臨時収入に `DonutPalette` をローテ割当／B: 主収入と臨時収入を分離／C: 臨時収入をドーナツ等の別表現）のうち、**Bを採用**：臨時収入は月内合算の1系列（「臨時収入」・ゴールド）にまとめ、給料（navy）・ボーナス（緑）と合わせて常に3系列固定（色ローテ不要）。内訳（入力名ごとの金額）は積み上げ棒からは読み取れなくなる代わりに、棒タップで既存の `OpenIncomeBreakdown`（④収入内訳ダイアログと同一処理・対象月1ヶ月に絞って呼び出し）を開いて確認する導線（`OnIncomeBreakdownSelected`）を追加。新規ダイアログ・新規集計ロジックを増やさず、既存の「棒タップ→内訳」パターン（④収入vs支出のコンボ棒）を踏襲した。
- **変動費のモデリング＝固定費マスタへのフラグ追加方式、再展開は非上書き（#87）** … 水道・電気のように「毎月必ず発生するが額が変わる」支出を、独立した新エンティティにせず既存 `FixedCost` へ `IsVariable` フラグを追加する方式を採用（自動展開・期間/ボーナス設定・口座紐付けなど固定費の周辺機能をそのまま再利用できるため）。展開先の `Debit` にも `IsVariable` を複製し、月次管理タブは `FixedCostId` を辿らずこのフラグだけで編集可否を判定できるようにした（固定費マスタが削除されても過去月の表示挙動が変わらない）。
  - **再展開時に月ごとの編集値を失わないための非上書き規則**：`OnFixedCostChanged()`（`LedgerEngine.ReconcileFixedCosts`）は当月以降の `IsFixed` な `Debit` を一旦全削除して再構築する既存方式のままだが、削除前に `IsVariable` な `Debit` の金額を `FixedCostId` 別に退避し、再構築後に復元する。これにより「マスタの `Amount` は既定値/初期値」「月ごとに編集した額は再展開（口座変更・期間変更・他の固定費追加削除を含む）をまたいで保持される」を、既存の全削除→再構築パターンを崩さず実現した。非変動の固定費は従来どおりマスタへ都度追随する（1つの再展開ロジックで両方を扱う）。
  - **統計への反映は既存ロジックのまま**：`GraphPage` の固定費集計（`FixedTotal`／固定費内訳ダイアログ）はマスタを再計算せず「各月の `Debits` に実際に記帳された `IsFixed` 分」を合算する既存方針（#84）のため、変動費も同じ `IsFixed=true` の `Debit` として記帳される以上、追加改修なしで二重計上なく反映される。
- **財布（現金）＝既存 `Account` を拡張した特殊口座＋ATM対称実体化（materialize）（#77）** … 現金の手元残高・使い道を追跡するため、独立エンティティではなく既存 `Account` に `IsWallet` フラグを追加する方式を採用（残高連鎖・台帳構造・収入/支出ゾーンをそのまま再利用でき、固定費#87と同じ「既存モデルへのフラグ追加」路線を踏襲）。アクティブな財布（`IsWallet && !IsDeleted`）は同時に1個のみ・任意作成。
  - **ATM入出金はカード/固定費と同じ実体化（materialize）方式で対称反映**：`LedgerEngine.ExpandWallet` が (1) 各非財布口座の既存 `AtmWithdraw`（手入力・UI不変）を合算して財布の `AtmDeposit` へ、(2) 財布の新規リスト `Ledger.WalletAtmDeposits`（{AccountId, Amount}・財布台帳でのみ使用）を全件合算して財布自身の `AtmWithdraw` へ、(3) 対象口座ごとに合算して各口座の `AtmDeposit` へ書き込む。財布自身は合算対象から除外して自己ループを防止。派生値を保存するため `LedgerMath.Close` は無改修で乗り、財布削除後も過去月に凍結保存される。
  - **再計算スコープは「編集された月そのもの」**：固定費/カードの「当月以降のみ」再展開とは異なり、過去月編集でもその月を再計算する（過去月も自由に編集できるアプリの特性上、materialize が古くなるのを防ぐ）。`ExpandWallet` は state と当月の `MonthData` だけに依存する純粋関数のため、`EnsureMonth` から**毎回無条件**に呼んで（`ExpandCards` と同じパターン）常に最新化し、加えて `LedgerService.RecalcWallet(ym)` を UI 側（口座のATM出金／財布のATM入金明細の編集ハンドラ）から明示的に呼んで、同一月内の他口座への即時反映を担保する。
  - **財布OFF＝口座と同じソフト削除＋当月以降の掃除**：`LedgerService.DeleteAccount` が `IsWallet` を検知した場合のみ、当月以降（`IsCurrentOrFutureCycle`）の月について財布自身の実体化値・`WalletAtmDeposits` をクリアし、財布由来で口座に実体化されていた `AtmDeposit` も 0 に戻す（カード削除#49の「当月以降を掃除・過去は凍結」パターンを踏襲）。口座側の「ATM入金（手入力）」欄は財布が非アクティブになった時点で `MonthlyTab` が自動的に手入力表示へ切り替わる（`Svc.HasActiveWallet` を見て出し分けているため、削除後は追加のクリーンアップなしで復活する）。
  - **現金支出のカテゴリ集計は `Debit.CategoryId` を新設して対応（issue想定からの拡張）**：issue は「財布の台帳は Debits・カテゴリ分類を無改修で再利用」としていたが、既存のカテゴリ別統計（`GraphPage.BuildCategorySpend`）は `CardDetail.CategoryId` のみを集計対象にしており、`Debit` にはカテゴリの概念が無かった。現金支出をカテゴリ別支出グラフへ反映する（issue の「本機能の主眼」）には集計ロジックの拡張が不可避なため、`Debit` に `CategoryId`（nullable・既定 null）を追加。他の `Debit`（固定費・カード由来・通常口座の手入力支出）はカテゴリを設定しない＝ null のまま集計対象外を維持するため、既存ユーザーの統計値には影響しない。カテゴリ選択 UI は `MonthlyTab` の財布カード「現金支出」行にのみ表示し、通常口座の手入力支出には出さない（並行導線を増やさずスコープを財布に限定）。
  - **口座間振込ピッカーからの除外は `others` フィルタの1点変更**：`MonthlyTab` の振込先候補生成 (`others = accts.Where(...)`) に `&& !x.IsWallet` を加えるだけで、全口座の振込ゾーンから財布が一律除外される。財布カード自身の「ATM入金 → 口座」は、既存の振込ゾーン（灰色・資産移動）の見た目を共用する形で表示し、同じ `others` リストを対象口座候補に転用している。
  - **実機確認後のフォローアップ（初回リリース直後の反復）**：①財布は他の口座と異なる特殊枠のため、一覧では常に先頭固定表示・並び替え対象外・名前変更不可にした（`LedgerService.ActiveAccounts` を `IsWallet` 降順→`SortOrder` の順でソートし、`AccountsTab` の D&D／▲▼は非財布口座のみで完結する部分集合 `MovableAccounts` で処理）。②財布は他の口座と異なり「作成した月」を恒久的な起点（開始残高の入力月）に固定する（`Account.WalletStartYm` を作成時に記録し、`LedgerEngine.ShouldCreateLedgerFor` で作成月より前の月へは `EnsureMonth` が台帳を遡って作らないようにした。他の口座は従来どおり最初に開いた月が起点になる仕様のまま）。③財布の「ATM入金（自動受取）」は固定費/カードと同様、口座別の内訳を折りたたみ表示にした（`Ledger.AtmDeposit` は合算値のみ保持するため、内訳は `MonthlyTab` が表示時に非財布口座の `AtmWithdraw` を都度集計するだけで、新規の永続フィールドは増やしていない）。④財布の「ATM入金 → 口座」（財布→口座・要口座選択）は支出ではなく資産移動のため、赤い支出ゾーンから振込ゾーン（灰色）へ移設した。⑤実機確認で発見した既存バグ：`MonthlyTab.ActiveAccountsForMonth` の「過去月」判定が暦月（`LedgerService.NowYm()`）基準になっており、給料日サイクル（15日起点）の関係で暦月とサイクル起点月がずれる期間（毎月1日〜14日）は当月サイクルなのに「過去月」の分岐（`Mo.Ledgers` に残っているソフト削除済み口座も表示する分岐）に入ってしまい、口座削除（財布OFF含む）が当月から即座に反映されない不具合があったため、判定基準を `LedgerService.IsCurrentOrFutureCycle`（サイクル起点基準）に修正した（財布固有ではなく既存の全口座削除に影響する不具合）。
  - **コミット前レビューでの修正（受入条件13との矛盾を解消）**：⑥財布の削除（OFF）が現金支出を1件でも記帳していると`GetFutureMonthsUsingAccount`の「使用中口座」ガードに阻まれ、確認ダイアログにすら進めなかった不具合を修正。このガードは通常口座（固定費/カードと同様、削除前にデータ整理を促す）を想定したもので、財布は`CleanupWalletOff`が当月以降を掃除し過去を凍結する設計上、現金支出があっても削除できて当然のため、財布(`IsWallet`)はこのガード自体の対象外にした。⑦非財布口座を削除した際、その口座の`AtmWithdraw`が財布の`AtmDeposit`へ実体化されたまま残ってしまう（次回`EnsureMonth`実行まで財布残高がズレる）問題を修正し、`DeleteAccount`が非財布口座削除時にも当月以降の`ExpandWallet`を再実行するようにした。⑧財布追加時の`WalletStartYm`が「月次タブで表示中だった月」（`Svc.CurrentMonth`）になっており、過去/未来月を表示したまま追加すると起点がずれる不具合を修正し、常に実際の当月サイクル（`LedgerService.CurrentCycleStartYm()`）を使うようにした。⑨現金支出のカテゴリ未選択（`""`）と未設定（`null`）とで⑥カテゴリ別支出グラフへの反映有無が食い違っていた不整合を修正し、両方とも集計から除外（`!string.IsNullOrEmpty`）に統一した。
  - **スマホの現金支出UIをカード＋ボトムシート方式に変更（実機確認後の追加フォローアップ）**：現金支出にカテゴリ選択が加わったことで、スマホでは「項目・カテゴリ・金額・削除」を1行（`.row`）に収めきれず窮屈になっていたため、`CardTab`のカード明細編集と同じパターン（`list-card`をタップ→`BottomSheet`「現金支出を編集」で項目名・カテゴリ・金額・削除を編集）へ変更。PC・非財布の手入力支出（カテゴリなし）は従来の1行編集のまま。あわせて「＋現金支出を追加」もスマホでは`CardTab.AddDetail`と同様、追加直後に編集シートを自動的に開く（先にカードを表示してから内容を入力する導線）方式にし、項目名・金額とも未入力のまま閉じた場合は自動破棄する（`_isNewCashDebit`）ようにして、空のカードが一覧に残らないようにした。
  - **固定費（支出/収入）・カードの引き落とし口座選択からも財布を除外（#124・口座間振込ピッカー除外#77のフォローアップ）**：`others` フィルタで口座間振込ピッカーからは既に除外していたが、固定費・カードの「口座を選択」`<select>` は `Svc.ActiveAccounts`（財布を含む）をそのまま使っており、財布を支出/収入の計上先として選べてしまっていた。財布は現金の出納枠であり、固定費・カードの引き落とし口座として選ぶのは業務上想定外（財布への/からの出入りは振替ゾーンに一本化＝#77）のため、`LedgerService` に `NonWalletAccounts`（`ActiveAccounts.Where(a => !a.IsWallet)`）を追加し、`FixedCostTab`・`CardSettings` の口座選択 `<select>`（新規追加ダイアログ／編集シート／インライン行・PC/スマホ両方）と、口座未登録警告・新規追加時の既定口座をこちらに差し替えた。口座フィルター（表示絞り込み用の`<select>`ではないチェックボックスメニュー）と `AccountName` によるラベル表示は従来どおり `ActiveAccounts` のまま据え置き、**既に財布が計上先として設定されている既存データは自動解除せず**、選択肢から消えるだけで名前表示（`AccountName`）は引き続き正しく解決される（ソフト削除済み口座を参照する既存ケースと同じ「表示は残るが選び直しはできない」という既存の許容パターンに倣った）。
- **アプリ内お知らせは repo 同梱 JSON（デプロイ配信）で管理する（#38）** … リリース内容・告知をユーザーに気づかせる手段として、サーバー/DB を追加せず `MoneyBoard/wwwroot/announcements.json`（フィールド: id/date/version/type/title/body）を静的配信する方式を採用（SWA Free 据え置き・追加コスト無し）。リリース作業（`/release`）の一環で1件追記するだけで告知できる。
  - **本文の箇条書きは種別タグを先頭に付ける運用**：`body` の各行（Markdown箇条書き）は `【新機能】`/`【改善】`/`【修正】` のいずれかを先頭に付け、その変更が新規追加・既存機能の改善・不具合修正のどれかを一目で判別できるようにする。1エントリ内に複数種別が混在してもよい（例: 新機能追加のリリースで併せて直したバグ修正がある場合、その行だけ `【修正】` にする）。リリース作業（`/release` §2.5）でエントリを追記する際は必ずこの形式に従う。
  - **表示先はスマホ＝共通ブランドコンポーネント `AppTitle`、PC＝サイドバーの独立ナビ項目（リリース後の実機確認で変更）**：当初は PC/スマホとも `AppTitle`（PCは `.sidenav-brand`、スマホは各ページ頭 `.home-head`/`.pf-head`/`.graph-header`。いずれか一方だけが常に1つレンダリングされる #55 の既存構造）に🔔ベルを集約する実装だったが、PC実機確認で「タイトル文字にベルがくっつく」「バッジ位置が行全体基準でずれる」不具合が見つかったため、PCはサイドバー内に「お知らせ」ラベル付きの独立したナビ項目（`SideNav.razor`。「金額を隠す」の直上）として分離した。`AppTitle` に `ShowBell` パラメータ（既定 true）を追加し、`SideNav` からは `ShowBell="false"` を渡してベル自体を非表示にしている。スマホは従来どおり `AppTitle` 内のベルのまま（ただし `.app-title` が親の flex コンテナ内で内容幅に縮んでベルがタイトル文字にくっつく不具合があったため `width:100%` を追加）。
  - **未読判定は「最後に見た id との比較」のみの純粋ロジック**：id は追記のたび単調増加させる運用とし、`MoneyBoardShared.AnnouncementMath`（`CountUnread`/`LatestId`）としてテスト可能な形で切り出した（`AnnouncementService` から委譲）。既読は localStorage の最終既読id（端末ごと独立）。ベル押下での一覧閲覧・What's New モーダルの閉じるは、いずれも「見た」= 最新idを既読として保存する統一動作にした。
  - **Markdown レンダリングは Markdig を採用し `DisableHtml()` で生HTMLを無効化**：body は repo 管理者（自分）が書く信頼コンテンツ前提だが、XSS 留意（issue 受入条件）のため防御的に生HTMLパススルーを止めている。
  - **静的JSON取得は API 用 HttpClient と別系統**：既存の DI 登録済み `HttpClient`（`Program.cs`）は `BaseAddress` がバックエンド API（ローカルは `http://localhost:7071`）向けのため、`wwwroot/announcements.json` の取得には使えない。`AnnouncementService` 内で `NavigationManager.BaseUri` を `BaseAddress` にした専用 `HttpClient` を都度生成して対応した。
  - **ダイアログは `AppTitle` ではなく `MainLayout` でレンダー（実機確認で発覚した不具合の修正）**：当初はベル＋ダイアログとも `AppTitle.razor` に実装したが、PC 実機確認で「マイページの口座/カテゴリ設定の `<select>` がお知らせダイアログより手前に描画される」不具合が発覚。原因は PC の `AppTitle` が `.sidenav-brand`→`.sidenav`（`position: sticky`）配下にあり、`position: sticky`/`fixed` は z-index の値に関わらず**必ず新しい重ね合わせコンテキストを生成する**ため、ダイアログの `position:fixed; z-index:300` が `.sidenav` ローカルのコンテキスト内に閉じ込められ、DOM順で後にある兄弟の `.app-scroll`（本文側）に描画順で負けていた。対策として `AnnouncementService` に `ShowList` フラグと `OpenListAsync`/`CloseList` を持たせ、ダイアログの実体は `MainLayout.razor` 側で `.app-shell` 直下（sidenav の外）にレンダーするよう変更。`AppTitle` はベルボタン（アイコン＋未読バッジ＋クリックで `OpenListAsync` 呼び出し）のみを残した。
  - **背面スクロールロック**：お知らせダイアログ表示中は Portfolio（#45）と同じ `viewport.js` の `setBodyScrollLock` パターンを採用。`MainLayout.OnAfterRenderAsync` で `AnyAnnounceDialogOpen`（`ShowList || UnreadItems.Count > 0`）の変化を監視してトグルし、`Dispose` 時にも保険で解除する。
  - **What's New は未読分を全件表示（実機フィードバックでの修正）**：当初は「未読件数に関わらず最新1件だけ自動モーダル表示、閉じると未読は全部まとめて既読化」という実装だったが、これだと複数件たまっていた未読のうち2件目以降が一度もモーダルに出ないまま既読になってしまう（ベル一覧を自発的に開かないと気づけない）。ユーザー指摘を受け、`AnnouncementService.UnreadItems`（未読分を新しい順で保持。旧 `PendingWhatsNew`単一項目から変更）を導入し、`AnnouncementWhatsNewDialog` が未読を全件ループ表示するよう修正（1件なら「最新のお知らせ」、複数なら「新着のお知らせ（N件）」とタイトルを出し分け）。既読化のタイミング（モーダルを閉じた時点でまとめて既読）自体は変更していない。
  - **同時配信件数が多い時のスクロール／横はみ出し対策**：`.ann-dialog-box`（`max-height:80vh; overflow:hidden`）配下の `.ann-list` は当初 `overflow-y:auto` だけでは効かなかった（flex子要素の既定 `min-height:auto` により、内容が親の `max-height` を超えても縮まずそのまま box 全体がはみ出す典型的な flexbox の罠）。`.ann-list` に `flex:1 1 auto; min-height:0` を追加してはじめて内側だけがスクロールし、下部の「閉じる」ボタン行（`.ann-dialog-box .dialog-btns{flex:none}`）は常に見える位置に固定される。あわせて `.ann-body`/`.ann-title` に `overflow-wrap:break-word` を追加し、本文中の長いURL等の未分割トークンで横にはみ出さないようにした。Playwright で15件のダミーお知らせ（うち長いURL入り）を使い、スマホ幅(390px)・PC幅(1280px)とも横スクロール無し・縦は内側のみスクロールすることを実測確認済み。
  - **`announcements.json` の id は新しいエントリほど大きい値にする（v2.12.0 リリース作業中に発覚した逆転バグの修正）**：`AnnouncementService.InitAsync` は `OrderByDescending(a => a.Id)` で新しい順を作る前提のため、id は「追記のたび単調増加」でなければならない。過去バージョン（2.9.0〜2.11.0）分を後から追記した際に誤って新しい版ほど小さい id を振ってしまい、一覧・What's New が古い順に表示される不具合が発生した（#38 issueコメント参照）。id1=最古〜id4=最新の向きに修正し、以後の追記でも「常に配列末尾に、直前の最大id+1で追記する」運用を徹底する。
  - **UI/UX刷新＝案A・タイムライン改良型（#130・Design提供）**：バージョン単位のタイムライン表示に変更し、本文各行の先頭 `【新機能】`/`【改善】`/`【修正】` を色バッジとして分離表示するようにした（`AnnouncementMath.ParseChangeItems` で `- 【種別】本文` 行を `(TypeLabel, Text)` に分解。テスト可能な純粋ロジックとして切り出し）。データ構造（`announcements.json` のフィールド・既読判定ロジック `IsUnread`/`CountUnread`/`LatestId`）は不変。
  - **「未読 N」ピル・未読ドットは開いた瞬間のスナップショットで描画（#130）**：`AnnouncementService.OpenListAsync` は開くと同時に実既読化（`WriteLastSeenAsync` で最終既読idを更新）するため、素の `UnreadCount`/`_lastSeenId` はダイアログが描画される時点で既に0・最新に更新済みになっている。新デザインはヘッダーに「未読 N」ピル、各バージョンに未読ドットを出す必要があるため、`OpenListAsync` の実既読化直前に `UnreadCountAtOpen`/`LastSeenIdAtOpen` としてスナップショットを保存し、ダイアログはこちらを参照して描画する（既読の判定ロジック自体・実際のlocalStorage更新タイミングは変更していない）。
  - **ダイアログ内「すべて既読」ボタンは実装後に削除（動作確認でのフィードバック）**：当初デザイン仕様どおり実装したが、既読化はダイアログを開いた時点で既に自動で完了する既存仕様のため、ボタンを押しても表示上のスナップショットが早めに消えるだけで実質的な効果がなく、ユーザーを誤解させると判断して削除した。既読管理ロジック自体を変更する場合（開いた時点の自動既読化をやめ、ボタン押下や閉じる操作を既読化のトリガーにする等）は挙動の変更を伴うため別issueで検討する。
  - **種別フィルタ（すべて/新機能/改善/修正）はバージョンの枠を保持**：フィルタは各バージョン内の該当行だけを絞り込み、該当0件のバージョンごと非表示にする（バージョン跨ぎのフラット表示にはしない＝「どのバージョンで入ったか」を残す設計）。フィルタ中の状態バー（「『◯◯』のみ表示中」）は動作確認フィードバックで削除し、セグメントの選択状態（アクティブタブの色）のみで絞り込み中であることを示す。
  - **What's New モーダルは未読ドット・フィルタを出さない（実装時の判断）**：仕様書はベルアイコンの一覧ダイアログを主対象にしており、What's New（更新後の初回自動表示）に未読ドットを出す仕様までは明記されていなかった。What's New に載る項目は定義上すべて未読（未読であることを示すために表示される画面）のため、ドットを全件に付けるのは冗長と判断し省略。最新版の NEW ピルのみ付与し、タイムライン表示・バッジは一覧ダイアログと共通にした。

- **チュートリアル基盤＝コーチマーク（スポットライト）＋モーダル図解の2種、初回PoCはPWA追加方法（月次管理タブから差し替え・#111）** … 各機能の使い方を初見のユーザーに伝える仕組みとして、(1) 実画面の対象要素を暗転背景＋ハイライト＋吹き出しで示す**コーチマーク**、(2) 概念・手順を中央ダイアログの図解で示す**モーダル図解**の2基盤を用意し、初回は `AppState.TutorialSeenVersion`（既定0）が `TutorialMath.CurrentVersion` 未満なら `Home` の `IsLoaded` 完了後に強制表示、以降はマイページの「？」ボタンから任意に再表示できるようにした。**PoC対象は当初issue本文の「月次管理タブ」から、実装着手時のユーザー指示で「PWAをホーム画面に追加する方法」へ差し替えた**（月次管理タブは別issueで後日対応）。
  - **既読管理はお知らせ（#38）と異なりサーバー保存（`AppState.TutorialSeenVersion`）**：お知らせの既読はlocalStorage（端末ごと独立）だが、チュートリアルは「ユーザー単位で初回に必ず1回見せる」という issue の要求（複数端末でログインしても既読を引き継ぐ）を満たすため、既存の口座/固定費と同じ「設定ドキュメント」経路（`SettingsPart`/`SettingsDoc`・`SchemaMigration` v8→v9）に乗せた。判定は `TutorialMath.ShouldForceShow`（純粋ロジック・テスト対象）で `seenVersion < CurrentVersion` の単純比較のみ。
  - **PWA追加方法の内容はUA判定でなく手動タブ切替（iOS/Android/PC）**：ホーム画面への追加はOSブラウザの操作であり実UI要素を指し示せないため、実スクショではなく手順アイコン＋番号のみで説明する（ADR「陳腐化回避」方針＝ピクセルスクショの多用を避ける）。UA自動判定は誤判定リスクとロジック追加コストに見合わないと判断し、3パターンを `TutorialModal` のタブで利用者自身に選んでもらう方式にした（`PwaTutorialModal.razor`）。
  - **コーチマークの対象は「マイページ」ナビ項目1点のみ（PoC範囲）**：？ボタンの置き場所をマイページタブに決めたため（月次管理タブに紐づかない内容のため）、初回はまず「マイページ」ナビ（`BottomNav`/`SideNav`。両者に共通 `id="tutorial-target-mypage"` を付与）をコーチマークで示し、「次へ」でPWA追加方法のモーダルへ進む2段階フローにした。対象要素の位置取得は JS interop（`wwwroot/js/tutorial.js` の `getBoundingClientRect`）をフロント側に配置し、`CoachMark.razor` は複数対象のステップ送りにも拡張できる形（`Selector` 差し替えのみ）にしてある。
  - **コーチマーク／モーダルは `MainLayout` の app-shell 直下でレンダー**：お知らせダイアログ（#38）と同じ理由で、`SideNav` の `position:sticky` が作る独立した重ね合わせコンテキストの影響を受けないようにするため。あわせてモーダル表示中のみ（コーチマークは対象のナビ項目を見せる必要があるため対象外）`viewport.js` の `setBodyScrollLock` で背面スクロールをロックする。
  - **モーダル図解基盤は「タブ切替」と「戻る/次へ」の両方でステップ間を移動できる汎用エンジンに設計**：`TutorialModal.razor` はタイトル・`TutorialStepMeta`（タブ見出し）のリストと `RenderFragment<int> ChildContent` だけを受け取り、内容は呼び出し側（`PwaTutorialModal.razor`）が組み立てる。今後の画面別チュートリアル（月次管理タブ等・別issue）もこの基盤をそのまま再利用する想定。

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

### 市場サマリ API `GET /api/market-summary`（#54）
- **目的**：公的な市場データ（指数＋USD/JPY）のみを返す読み出し専用エンドポイント。個人ポートフォリオは含まない（=**#48 で分離**）。日報スキル・市場指標バーなど複数の外部連携から再利用できる共通部品。
- **認証**：ユーザー JWT ゲートとは別の**共有シークレット**（環境変数 `InternalApi__SharedSecret`・リクエストヘッダー `X-Internal-Secret`）。定数時間比較（`CryptographicOperations.FixedTimeEquals`）でタイミング攻撃を回避。シークレット値はコードに書かず GitHub Secrets ＋ SWA アプリ設定のみ。
- **固定シンボルセット**（市場指標バー #26 の確定5本に準拠。⚠️ TOPIX は Yahoo v8 非配信のため除外）：`^DJI`（NYダウ）/ `^IXIC`（ナスダック）/ `^GSPC`（S&P500）/ `^N225`（日経平均）/ `^KS11`（KOSPI）＋`JPY=X`（USD/JPY）
- **取得ロジック**：既存の `FetchPriceAsync`（Yahoo v8 chart API）を再利用し並行取得。1銘柄失敗しても残りを返す（落とさない既存方針を踏襲）。全銘柄失敗時は 502。
- **レスポンス**（`MarketSummaryResponse`）：`At`（取得時刻 UTC・`yyyy-MM-dd HH:mm`）/ `UsdJpyRate`（小数値 0 なら未取得）/ `Indices`（取得できた指数のみ）。各 `MarketIndexInfo` は `Symbol`・`Label`・`Value`・`PrevClose`（null 可）。

### ポートフォリオ現況 API `GET /api/portfolio-snapshot-current`（#48）
- **目的**：オーナーのポートフォリオ現況（保有銘柄・評価額・含み損益）と過去の資産推移（スナップショット時系列＋各時点の評価損益）を返す読み出し専用エンドポイント。日報スキルがスクショ手貼りなしでポートフォリオデータを取得するために使用。
- **認証**：ユーザー JWT ゲートとは別の**共有シークレット**（環境変数 `InternalApi__SharedSecret`・リクエストヘッダー `X-Internal-Secret`）。`/api/market-summary` と同じ仕組みを再利用。
- **オーナー特定**：環境変数 `OwnerUserId`（Firebase uid）でオーナーの Cosmos パーティションを直接読む。マルチユーザーでも日報対象はオーナー1名で固定。
- **処理フロー**：ポートフォリオドキュメントを読む → 全保有銘柄の価格を並行取得（Yahoo v8 / 投信協会 CSV）→ USD/JPY レート取得 → 当日スナップショットを記録（同日上書き・`PortfolioMath.BuildSnapshot` を再利用）→ Cosmos に保存 → レスポンス構築
- **レスポンス**（`PortfolioCurrentResponse`）：`PricedAt`・`UsdJpyRate`・`TotalValuationJpy`・`CostBasisJpy`・`UnrealizedPnlJpy` / `Holdings`（銘柄ごと：名前・口座区分`AccountKind`・数量・現在価格・評価額・取得原価・含み損益・**前日終値/前日基準価額`PrevPriceNative`・前日比%`DayChangePct`・前日比評価額(円)`DayChangeValuationJpy`**（休場・データなし等は null #109）。同一銘柄が成長/つみたて両枠にある場合の判別に使用 #69）/ `History`（スナップショット時系列：日時・UsdJpyRate・総資産・取得原価・評価損益）
- **取得原価の算出**：`PortfolioMath.CostBasisJpyAsOf`（指定日元本・円換算）を再利用。現況・各スナップショット点ともに同方式。
- **前日比の算出（#109）**：価格取得に使う `FetchPriceAsync`/`FetchFundPriceAsync` が返す前日終値（`.Prev`）を `PortfolioData.PrevPrices`（非永続・Web UI と同じフィールド）に保存し、Web UI の `PortfolioMath.DayChangePct`／`ValuationJpy` をそのまま再利用して算出（二重実装なし）。
- **ETag 競合の扱い**：フロント（Portfolio 画面）と同時操作で 412 が発生した場合はスキップして記録なしでも応答は返す（読み取った価格データは正しいため）。

### 推移スナップショットのサーバー側自動記録 `POST /api/record-snapshots`（#37）
- **目的**：アプリ（ポートフォリオ画面）を開かなくても、毎営業日 推移スナップショットが自動で1点記録されるようにする（開かない日は従来欠測だった）。
- **認証**：`/api/market-summary` / `/api/portfolio-snapshot-current` と同じ共有シークレット（`InternalApi__SharedSecret` / `X-Internal-Secret`）。ユーザー JWT ゲートとは別系統。
- **全ユーザー対応**（#48 のオーナー固定とは異なる）：`type = 'portfolio'` でクロスパーティションクエリし、全ユーザーの portfolio ドキュメントを列挙。価格は全ユーザー分の銘柄をまとめて重複排除してから取得し（Yahoo/投信協会への呼び出し回数を抑制）、ユーザーごとに `PortfolioMath.BuildSnapshot`→`UpsertSnapshot`→保存。
- **記録しない条件**：評価額を1件も算出できないユーザー（保有0件・全銘柄価格未取得等）は `BuildSnapshot` が null を返し、そのユーザーはスキップ（既存 `GetPortfolioSnapshotCurrent` と同じ規則）。
- **二重記録の防止**：`UpsertSnapshot` の同日上書きにより、手動で画面を開いた時の記録（既存の価格更新フロー）と cron 記録は同じ日なら1点に吸収される。同日判定の基準はフロント・サーバーとも UTC に統一（フロントの記録時刻は `RecordSnapshot` 内でのみ `DateTime.UtcNow` を使用。画面表示用の `PricedAt`「価格更新 HH:mm」はユーザー向けの現地時刻表示なので `DateTime.Now` のまま・記録の同日判定には使わない）#73。
- **ETag 競合の扱い**：フロントと同時操作で 412 が発生したユーザーはそのユーザーだけスキップし、他ユーザーの記録は継続する。
- **呼び出し元**：GitHub Actions の `record-snapshots.yml`（`schedule: cron` 平日 07:15 UTC＝16:15 JST・東証クローズ後／米国市場オープン前 #96）が本番 URL に POST。SWA Free の managed Functions は HTTP トリガーのみ対応（Timer トリガーは SWA Standard=有料が必要）なため、Timer ではなく「cron→HTTP」で実現（下記 ADR）。
- **必要な設定（手動）**：GitHub Actions が呼ぶための **GitHub Secrets `INTERNAL_API_SHARED_SECRET`**（SWA アプリ設定の `InternalApi__SharedSecret` と同じ値）を追加すること。

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
- **ESPP（従業員株式購入制度）**：`BuyLot.IsEspp`＋`EsppDiscount=0.15`。買付ロット単位で会社補助15%を差し引く。対象社員フラグ＝`AccessDoc.EsppEligibleEmployees`（Owner マイページでチェック）。`GET /api/portfolio` は**本人ぶんの `IsEsppEligible` のみ**返す（Owner 常に true・対象外に UI を出さない）。ESPP 列は対象ティッカー（`PortfolioMath.EsppEligibleTicker`）×対象社員のみ表示。

### v1.3.2 の追加（本番反映済み・2026-06-20）
- **米国株の円/ドル評価切替**：米国株グループ見出しの円/ドルトグルで、その**グループの評価額・評価損益・前日比の表示通貨**を一括切替（既定=円）。**元本・平均取得単価は建て通貨のまま**。換算は現在レート（`CcyFactor`／`ValDisp`／`UpnlDisp`）、為替未取得は「—」。損益率(%)は通貨非依存（建て通貨ベース）。日本株・投信は常に円。
- **円拠出の米国株＝取得金額(円)を元本に**：円で拠出してドル転約定する積立/ESPP は、後から円換算するとレートでズレる。買付フォームの**「取得金額(¥)」**（`BuyLot.Amount`・従来は投信のみ）を**円建て株でも入力可**にし、入れた円をそのまま元本＝**為替換算なし＝ドリフトなし**。`PortfolioMath.LotCost` が Amount 優先なので計算側は既存のまま。
- **価格通貨とお金通貨の分離**：**米国株の単価・平均取得単価・現在価格は常にドル表記**（`PriceCcySym`＝US は $）。元本・評価額・評価損益は建て通貨（円拠出は円）。買付の単価ラベルは「単価($)」。
- **前日比**：`/api/quote` が前日終値も返す（株＝Yahoo `chartPreviousClose`／投信＝協会CSVの前営業日の基準価額）。`QuoteResponse.PrevClose`/`FundPrevClose`→`PortfolioData.PrevPrices` に保存。一覧は**専用「前日比」列**（金額メイン＋%・%は値動きそのもので通貨非依存）。
- **一覧レイアウト**：**現在価格列を追加**／**口座は列を廃し銘柄名下のバッジ**（`pf-badge-sub`・名前は列幅で省略・バッジは `align-self:flex-start` で文字幅）／**評価額＋評価損益を1列に統合**（評価額の下に損益・金額メイン）／**取引列を固定幅(56px)化**してヘッダーと行のグリッドを一致（以前のヘッダーずれを解消）。列順＝銘柄名・元本・平均取得単価・数量・現在価格・評価額／損益・前日比。スマホのカードは**現在価格のみ**追加表示。

### 残タスク（→ GitHub Issues）
- AI 機能（C案 #27）は **✅ 実装済み**（Phase 4 の土台を再利用・v2.4.0 で本番リリース）。自然言語入力解析（#28）／月次コメント生成（#29）／FABチャット（#30）は会話型UIが方向性に合わないため見送り・クローズ済み（「保留中・未実装」節の ADR 参照）。
- 市場指標バー（`/portfolio` 上部）＝ **✅ v1.5.0 で完了**（#26 closed）。

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

### カテゴリ自動推定（C案・issue #27・dev・リリース待ち）
- **バックエンド**：`DataApi.CategoryClassify.cs`（partial）。`POST /api/classify-categories`（`AuthorizeAsync` ゲート内）。本文 `{stores(string[]), categories([{id,name}])}` を受け、`ClassifyCategoriesAsync` が店名一覧＋カテゴリ一覧をテキストで Haiku に渡し（Vision 不要）、`ParseCategoryClassifyResponse` が応答 JSON を `Dictionary<store, categoryId>` へ。カテゴリ一覧はユーザーごとに異なりサーバー側で保持していないため、**リクエストボディで受け取る**（Cosmos を叩かず完結）。存在しない categoryId・確信が持てない（null）行は結果から除外。AI が返す店名は `LedgerEngine.NormalizeStore`（全角半角/空白）で**要求した原文の店名へ突き合わせ**て表記ゆれを吸収し、キーは必ず原文に揃える（フロントの完全一致採用のため）／要求していない店名（でっち上げ）は捨てる。stores は最大200件（超過は400）。`MaxTokens=32000`（200件でも構造化出力が途中で切れて JSON 不正→全件失敗にならないよう余裕。Haiku 4.5 出力上限 64K 内）。**このAPI自体は何も永続化しない**（キャッシュ書き込みはフロントの「適用」時のみ）。
- **フロント**：`CardTab` の一括カテゴリダイアログに「AIで分類（未分類のみ）」ボタンを追加。現在「未分類」の利用先だけを対象に呼び出し、返ってきた提案を一括カテゴリの選択欄（`BulkSelection`）へプリセットするだけで、**確定は既存の「適用」操作のまま**（レビュー必須はここで担保）。適用時の `CategoryRules` キャッシュ・カテゴリ反映ロジックは既存のまま変更なし。
- **テスト**：`MoneyBoardApi.Tests/CategoryClassifyParserTests.cs`（7件）＝店名→カテゴリ変換/null除外/存在しないID除外/店名空行除外/items欠落→空/不正JSON→空/スキーマがvalid JSON。

### カテゴリ前方一致ルール（issue #70・dev・リリース待ち）
- **背景**：ETC通行料金のように利用先表記が区間ごとに毎回変わる明細（例:「ETC 一宮IC入-鳥見町出口 普通車」）は完全一致ルールだと1件ずつ登録する必要があり非現実的。共通の接頭辞（例:「etc」）でまとめて分類したいという要望から派生（issue #27 の会話）。
- **データモデル**：`AppState.CategoryPrefixRules: Dictionary<string,string>`（prefix → categoryId）を `CategoryRules`（完全一致）と並列で加算的に追加。キーの正準形は `LedgerEngine.NormalizeStore` + `ToLowerInvariant`（大小文字を区別しない要件をキー側で構造的に満たす）。
- **解決ロジック（`LedgerEngine.ResolveCategory`・純粋関数）**：①完全一致（`CategoryRules`）を最優先、②該当しなければ前方一致（`CategoryPrefixRules`）を `StartsWith(OrdinalIgnoreCase)` で判定し**最長プレフィックス優先**。完全一致が優先されるため、広い prefix に対する個別上書きが可能（例: prefix `kabu`→公共料金、完全一致 `KABU&【プラス／プレミアム】`→サブスクを個別に維持）。`LedgerService.ApplyCategoryRules`（取込時の自動分類）と `CardTab.OnNameChanged`（手入力確定時。従来 `NormalizeStore` を通さず素の店名で照合していた不整合を本対応で解消）を同じ resolver に統一。
- **UI（`CardTab` の一括カテゴリダイアログ）**：利用先一覧の下に独立セクションとして前方一致ルールの一覧編集（prefix・該当件数プレビュー・カテゴリ select・削除）＋追加フォーム（前方一致文字列は最低2文字でバリデーション・入力中に現在月の該当件数をプレビュー）を追加。**追加/削除した時点で即座に保存**（完全一致側の「適用」ボタンとは独立）。
- **クリーンアップ・増加抑止**：前方一致ルールを追加すると、その prefix に包含され**同一カテゴリ**を指す既存の完全一致ルールを件数提示のうえ削除（`LedgerEngine.ExactRulesCoveredByPrefix`。別カテゴリを指すものは個別上書きとして残す）。また `ApplyBulk`（完全一致ルールの保存）時、選んだカテゴリが前方一致ルールで既に解決される場合は完全一致ルールとして重複保存しない（`LedgerEngine.ResolveCategoryByPrefix` で判定）。実データで試算すると現行約139ルール→約95件相当に整理される見込み（ETC 20→1・セブン系 13→1等）。
- **スキーマ**：SchemaVersion v4→v5（加算のみ・移行処理なし）。
- **テスト**：`LedgerEngineTests`（完全一致優先/前方一致フォールバック/大小無視/最長プレフィックス優先/該当なし→null/クリーンアップ対象判定の6件）・`SchemaMigrationTests`（v4→v5 が加算のみで既存ルールを保持することの1件）。

### 今後（この土台を再利用）
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
- `EnsureMonth()` で未登録口座に固定費を自動展開（`LedgerEngine.ExpandFixedCosts`：未展開分のみ追加し既存 `Debit` の金額は変更しない）。**ただし展開自体を実行するのは「その月を新規作成した場合（バックフィル）」または「当月以降のサイクル」に限る**（`isNewMonth || IsCurrentOrFutureCycle(ym)`）。既に存在する過去月をタブ操作で開き直しただけでは実行しない＝あとから追加/変更した固定費が過去の確定済み月へ遡って混入することはない（#87 の動作確認で発覚した既存バグを合わせて修正。以前は `EnsureMonth` にこのガードが無く、無期限＝開始年月未設定の固定費を追加すると過去月を開いた瞬間に新規Debitが混入し、月末残高の自動連鎖に影響していた）。
- `OnFixedCostChanged()` で当月サイクル以降を再展開（`LedgerEngine.ReconcileFixedCosts`：一旦 `IsFixed` の `Debit` を全削除して再構築）。こちらは元から `IsCurrentOrFutureCycle` でガード済み。
- **変動費（`FixedCost.IsVariable`・#87）は「当月に限り」手動編集値を保持**：マスタ変更時、**翌月以降は編集の有無に関わらず常にマスタへ一律追随**する（非変動の固定費と同じ挙動）。**当月のみ**、月次管理タブでユーザーが金額を手動編集した（`Debit.AmountOverridden=true`）分は `ReconcileFixedCosts` が保持し上書きしない。`ReconcileFixedCosts(state, ym, mo, isCurrentCycle)` の `isCurrentCycle` は `LedgerService.OnFixedCostChanged()` が `ym == CurrentCycleStartYm()` で判定して渡す（`LedgerEngine` はテスト容易性のため「今日」を直接参照しない）。翌月以降の再構築時は `AmountOverridden` を毎回 `false` にリセットする（その月が将来「当月」になった時点で改めて手動編集がなければマスタへ追随させるため）。

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
- `AppState.SchemaVersion` と `SchemaMigration.Apply()` が将来の段階移行の足場。**現状 CurrentVersion=5**。
- Phase 2 のカテゴリ/カード/明細、`Ledger.Incomes`/`AtmDeposit`/`AtmWithdraw`・`Card.IsDeleted`・
  `MonthData.CardBilled` はすべて**加算的追加**（旧データはデフォルト値で読める）。
- **v3**: 月初残高を「作成時スナップショット」から「前月末からの自動連鎖」へ変更。非起点月の `Confirmed` が
  参照されなくなるだけで構造的な移行処理は不要（旧 `Ledger.OpeningPinned` 案は採用せず撤去）。
- **v4**（#27）: `CategoryRules` のキーを `LedgerEngine.NormalizeStore`（全角半角/空白正規化）済みに統一。
  OCR・CSV発行元差の表記ゆれ（例：全角/半角の「Amazon Downloads」）で同一店名が別キーに分裂していた
  既存データを正規化キーへ統合（衝突時は後勝ち）。以降の書き込み（一括カテゴリ「適用」）・読み取り
  （`LedgerService.ApplyCategoryRules`）も正規化キーで統一し、再分裂を防ぐ。
- **v5**（#70）: `CategoryPrefixRules`（前方一致カテゴリルール）を追加。加算的なフィールド追加のみで
  移行処理は不要（旧データは空の辞書として読める）。
- **v6**（#87）: `FixedCost.IsVariable`（変動費フラグ）・`Debit.IsVariable`/`AmountOverridden` を追加。
  加算的なフィールド追加のみで移行処理は不要（旧データは `false`＝従来どおりの固定費として読める）。

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

