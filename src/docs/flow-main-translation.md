# 主翻译执行链路

## 模块职责
- 承接主窗口输入文本，驱动自动或手动翻译。
- 协调翻译插件与词典插件并发执行、缓存命中与历史写入。
- 处理自动回译、翻译后复制、历史前后导航等行为。

## 关键入口
- `STranslate/ViewModels/MainWindowViewModel.cs`
  - `OnInputTextChanged()`：自动翻译防抖入口。
  - `TranslateAsync()`：主翻译命令。
  - `SingleTranslateAsync()` / `SingleTransBackAsync()`：单服务执行。
  - `ExecuteTranslateAsync()`：缓存优先 + 实时翻译编排。
- `STranslate/Helpers/LanguageDetector.cs`
  - `GetLanguageAsync()`：源语种判定与目标语种推导。
- `STranslate/Core/SqlService.cs`
  - `GetDataAsync()` / `InsertOrUpdateDataAsync()`：历史缓存读取与落盘。

## 核心流程
### 从入口到结果：输入文本到多服务翻译完成
1. 输入变化触发 `OnInputTextChanged(value)`：
   - 若 `Settings.AutoTranslate == false` 直接返回。
   - 空文本时取消防抖任务。
   - 非空时通过 `DebounceExecutor` 按 `Settings.AutoTranslateDelayMs` 延迟执行 `TranslateCommand`。
2. `TranslateAsync()` 执行前先取消防抖队列，重置已启用服务的结果对象。
3. 进入 `ExecuteTranslateAsync(checkCacheFirst)`：
   - 获取已启用且 `ExecMode == Automatic` 的服务。
   - 若启用历史缓存：用 `(InputText, SourceLang, TargetLang)` 查询 `SqlService`。
   - 命中缓存后把结果注入各插件结果对象，仅保留未命中服务继续实时执行。
4. 对未命中服务调用 `LanguageDetector.GetLanguageAsync()` 获取最终 `source/target`。
   - 当用户在“识别为”标签上手动选择语种时，本次主翻译会跳过缓存并强制以所选语种作为 `source` 执行一次；执行结束后仍按原始 `(InputText, SourceLang, TargetLang)` 键回写最新结果。
5. 使用 `SemaphoreSlim` 并发执行服务：
   - `ITranslatePlugin` 走主翻译，按需执行自动回译。
   - `IDictionaryPlugin` 走词典查询路径。
6. 翻译完成后按设置执行复制逻辑，并将结果按服务顺序排序写回历史。

### 从入口到结果：缓存命中与增量补全
1. `PopulateResultsFromCacheAsync()` 遍历目标服务。
2. 命中缓存时：
   - 翻译服务更新 `TransResult` / `TransBackResult`。
   - 词典服务更新 `DictionaryResult`。
3. 若服务需要自动回译但缓存无回译结果，只补做回译，不重做主翻译。

### 从入口到结果：手动单服务执行（词典/翻译）
1. `SingleTranslateAsync(service)` 先查当前输入历史。
2. 若是 `IDictionaryPlugin`：执行 `ExecuteDictAsync()`，失败即返回。
3. 若是 `ITranslatePlugin`：识别语种后执行 `ExecuteAsync()`，按配置追加 `ExecuteBackAsync()`。
4. `Settings.CopyAfterTranslationNotAutomatic` 为真时，手动执行完成立即复制结果。

### 复制与历史策略
- 自动复制：`Settings.CopyAfterTranslation` 支持第 N 个自动服务或最后一个自动服务。
- 历史持久化：`Settings.HistoryLimit > 0` 时使用 SQLite；否则仅使用内存 `_recentTexts` 缓存最近输入。

## 关键数据结构/配置
- `Service.Options`
  - `ExecMode`：自动/手动执行。
  - `AutoBackTranslation`：自动回译开关。
- `HistoryModel` / `HistoryData`
  - `RawData` 序列化所有服务结果（翻译、回译、词典）。
- `TranslateResult` / `DictionaryResult`
  - 承载执行状态、耗时、文本与结构化词典结果。
- 输入区识别状态
  - `None`：当前不显示识别语种标签。
  - `Cache`：当前翻译结果来自缓存命中；若缓存里记录了 `EffectiveSourceLang`，语种下拉仍预选该语言。
  - `Detected`：显示最近一次主翻译实际使用的源语种，可能来自自动识别成功、识别失败后的回退语言，或用户手动选择的一次性强制执行。
- 关键设置项
  - `AutoTranslate`、`AutoTranslateDelayMs`
  - `CopyAfterTranslation`、`CopyAfterTranslationNotAutomatic`
  - `HistoryLimit`

## 关键文件
- `STranslate/ViewModels/MainWindowViewModel.cs`
- `STranslate/Helpers/LanguageDetector.cs`
- `STranslate/Core/SqlService.cs`
- `STranslate/Services/TranslateService.cs`
- `STranslate.Plugin/ITranslatePlugin.cs`

## 常见改动任务
- 新增翻译结果后处理（如术语替换）：优先在 `ExecuteAsync` 返回后、历史入库前处理。
- 调整自动翻译触发体验：修改 `OnInputTextChanged` 与 `Settings.AutoTranslateDelayMs`。
- 修改缓存命中规则：改 `HistoryModel.HasData()` 与 `PopulateResultsFromCacheAsync()`，避免只改 UI 层。
- 增加复制策略：改 `TranslateAsync()` 内 `CopyAfterTranslation` 分支，并同步枚举定义与设置页。
