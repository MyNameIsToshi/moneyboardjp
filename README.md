# 生計 — 口座別 引き落とし管理（Blazor WebAssembly）

毎月、口座ごとに「確認時点の残高＋給料−引き落とし−送金＋受取」で月末残高を出し、
足りない口座を警告する家計簿アプリです。データはブラウザの IndexedDB に保存され、
維持費0円で動きます。

## 必要環境
- .NET 10 SDK

## ローカルで動かす
```bash
dotnet restore
dotnet run
```
表示された `http://localhost:xxxx` をブラウザで開いてください。

## 構成
- `Models/Models.cs` … データモデル（口座・月次台帳・引き落とし・振込）
- `Services/LedgerService.cs` … 計算ロジック（月末残高・前月末の初期値引き継ぎ・初期データ）
- `Services/StorageService.cs` … IndexedDB への保存（`wwwroot/js/storage.js` を呼ぶ）
- `Pages/Home.razor` … 画面

## Azure Static Web Apps（無料）へのデプロイ
1. このフォルダを GitHub リポジトリに push
2. Azure ポータルで Static Web App を新規作成し、リポジトリを連携
3. ビルド設定（Blazor プリセット）
   - App location: `/`
   - Api location: （空欄）
   - Output location: `wwwroot`
4. 以降は push のたびに GitHub Actions で自動デプロイ

## 今後の予定
- カード利用明細（引き落とし額の内訳）
- Claude API による入力補助・支出講評（C# のサーバー関数経由でキーを安全に保持）
- 資産管理（Phase 2）
