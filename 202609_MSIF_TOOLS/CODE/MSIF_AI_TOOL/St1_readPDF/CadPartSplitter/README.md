# CadPartSplitter — CAD 图纸零件自动分割工具

把一张工程图纸（PDF 或扫描图）自动切分成一个个独立零件图（PNG），
**并记录每张零件图的坐标、以及该图上所有文字及其坐标**，
用于后续 CAD 识别 / 报价流程。基于 **.NET Framework 4.8 + WinForms + OpenCvSharp4 + Docnet.Core(PDFium) + PdfPig**。

## 运行

双击 `CadPartSplitter.exe` 启动图形界面；带参数启动则进入命令行模式（供批处理调用）。

### 图形界面

1. **输入文件**：点「浏览…」选择 `.pdf / .png / .jpg / .bmp / .tif`（也可直接把文件拖到窗口上）
2. **输出目录**：选择图纸后自动填为 `<图纸名>_parts`，可手工修改
3. **识别参数**：一般保持默认即可；识别效果不理想时按下表调整
4. 点「开始分割」，日志面板会实时显示每一步进度，完成后状态栏提示零件数量

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| 渲染 DPI | 300 | PDF 渲染分辨率。降低可省内存，但细节会变模糊 |
| 页码 | 0 | 多页 PDF 指定页（从 0 开始） |
| 右侧遮蔽 | 0.50 | 屏蔽右侧 BOM 表/说明区。**开启「自动遮蔽」时仅作兜底比例** |
| 自动遮蔽 | 开 | 自动定位表格/标题栏的起始列，按实际表格位置遮蔽。关掉则退回固定比例 |
| 最小面积 | 80000 | 轮廓面积阈值，滤掉孤立数字/噪线 |
| 扩展边距 | 30 | 裁切框四周留白，防止尺寸标注被切 |
| 核宽度 / 核高度 | 80 / 25 | 形态学膨胀核，用于把同一零件的碎散图元聚合在一起 |
| 保存调试图 | 关 | 额外输出二值图、遮蔽图、膨胀图，便于定位参数问题 |

### 命令行

```
CadPartSplitter.exe <输入文件> [输出目录] [选项]
```

| 选项 | 说明 |
| --- | --- |
| `--debug` | 保存中间处理图 |
| `--dpi <N>` | PDF 渲染 DPI（默认 300） |
| `--page <N>` | PDF 页码，从 0 开始（默认 0） |
| `--mask <N>` | 右侧遮蔽比例 0.0 ~ 1.0（默认 0.50，自动检测失败时的兜底） |
| `--fixed-mask` | 关闭自动遮蔽，强制按 `--mask` 的固定比例遮蔽（旧行为） |
| `--mask-ink <N>` | 自动遮蔽的列墨迹阈值（图高比例，默认 0.010） |
| `--area <N>` | 最小轮廓面积（默认 80000） |
| `--pad <N>` | 外接框扩展像素（默认 30） |
| `--kw <N>` / `--kh <N>` | 膨胀核宽度 / 高度（默认 80 / 25） |
| `-h` / `--help` | 打印用法 |

示例：

```
CadPartSplitter.exe drawing.pdf output --debug
CadPartSplitter.exe scan.png parts --mask 0.48 --area 50000 --fixed-mask
CadPartSplitter.exe plan.pdf result --dpi 200 --kw 60 --kh 20
```

退出码：`0` 成功（含「未检测到零件」）、`1` 输入文件不存在、`2` 处理异常。

## 处理流程

```
输入 PDF/图片
   ↓ 灰度化 + 自适应反向二值化（线条 → 白色前景）
   ↓ 自动定位右侧表格/标题栏起始列（列墨迹延伸法；失败则按「右侧遮蔽」比例）
   ↓ 遮蔽该区域
   ↓ 形态学闭运算 + 膨胀（聚合同一零件的碎散图元）
   ↓ 外层轮廓提取 + 面积/宽高/边界筛选 + 重叠矩形合并
   ↓ 按 Y 坐标排序裁切 → Part_01.png, Part_02.png, …
   ↓ （仅 PDF，只读）读取文字层：框内文字 + 坐标 → texts / parts_text.txt
   ↓ （仅 PDF，只读）框外文字（表格 / 标题栏 / 页边注记）+ 表格行列结构 → outside_info.json / outside_text.txt
```

每一步的原理、算法依据、参数影响与调参顺序，见 **[docs/分割原理.md](docs/分割原理.md)**。

## 输出

```
<输出目录>/
├─ Part_01.png          # 按从上到下顺序编号的零件图
├─ Part_02.png
├─ parts_info.json      # 每个零件的像素坐标 + PDF 毫米坐标 + 该图上的文字及坐标
├─ parts_text.txt       # 人眼可读的文字清单（每张图一节：图坐标 + 图上的文字行）
├─ outside_info.json    # 被分割部分以外：表格区「表→行→单元格」+ 其它未归属文字（含坐标）
├─ outside_text.txt     # 人眼可读的「未分割区域文字清单」（表格按行列、注记按行）
└─ debug_00_original.png   # 仅开启「保存调试图」时生成
   debug_01_binary.png
   debug_02_masked.png
   debug_03_dilated.png
```

### parts_info.json（机器可读）

每个零件一条，`texts` 为该图上所有文字（PDF 词级，未合并）。文字坐标同样给三套：
`pixel`（页面绝对像素）、`local`（相对该零件图左上角）、`pdf_mm`（页面毫米）。
非 PDF 输入时 `texts` 为空数组，其余字段与旧版本一致。

```json
[
  {
    "index": 1,
    "fileName": "Part_01.png",
    "pixel": { "x": 2663, "y": 539, "w": 2160, "h": 1055 },
    "pdf_mm": { "x": 225.47, "y": 45.64, "w": 182.88, "h": 89.32 },
    "texts": [
      {
        "text": "鴨居",
        "pixel":  { "x": 3619, "y": 1045, "w": 190, "h": 77 },
        "local":  { "x": 956,  "y": 506,  "w": 190, "h": 77 },
        "pdf_mm": { "x": 306.41, "y": 88.48, "w": 16.09, "h": 6.52 }
      }
    ]
  }
]
```

### parts_text.txt（人眼可读）

同一水平线上的词会合并成一句并给出并集坐标，便于直接核对「这张图上写了什么」：

```
# CadPartSplitter 文字清单（每张零件图的坐标 + 该图上的文字及坐标）
# 生成时间   : 2026-10-30 13:19:22
# 源文件     : D:\…\H310U10004.pdf
# 页面 / DPI : 第 1 页 / 300 DPI
# 坐标说明   : 绝对 = 页面像素坐标（左上原点）；局部 = 相对该零件图左上角；mm = 页面毫米坐标
# 零件图数量 : 12
# 文字来源   : PDF 文字层（按词边界）；位图输入无文字层，内容为空

[Part_01.png]  绝对(x=2663 y=539 w=2160 h=1055)  mm(x=225.47 y=45.64 w=182.88 h=89.32)  文字行 6 条 / 词 11 个
    y=1026  x=3051  w=1306  h=102    局部(388,487)  1 鴨居 G部
    y=1402  x=3851  w=693   h=101    局部(1188,863)  上レール F部

[Part_04.png]  绝对(x=586 y=1358 w=436 h=2677)  mm(x=49.61 y=114.98 w=36.91 h=226.65)  文字行 1 条 / 词 1 个
    y=1390  x=707   w=261   h=74     局部(121,32)  戸当り
```

> 文字取自 PDF 文字层，因此扫描件 / 文字已转曲的 PDF 取不到文字（`texts` 为空、清单标注
> 「（该图范围未检测到文字）」）；`□` 等以矢量图元绘制的符号也会缺失。
> 详见 [docs/分割原理.md](docs/分割原理.md) 3.8 与第 5 节。

### outside_info.json / outside_text.txt（被分割部分以外）

零件框以外的文字也在同一套坐标系里留档，分两类：

* **表格 / 标题栏区文字**：词中心 X ≥ 遮蔽线（日志 `右侧遮蔽：自动检出表格/标题栏区 X=7074`）
  的词，按「表 → 行 → 单元格」重组——同一水平线（纵向中心差 ≤ 0.6 倍字高）为一行，
  行内与前一个词的水平间隙 > `max(24px, 1 倍字高)` 就断开成新单元格，列号是表内按
  单元格中心 X 聚类的推断值；
* **其它未归属文字**：零件框外、又不在表格区的零散文字（页边注记、签署栏等），给词级坐标。

`outside_info.json`（UTF-8 无 BOM，`pixel` 为页面绝对像素、`pdf_mm` 为页面毫米）：

```json
{
  "source": "D:\\…\\H310U10004.pdf",
  "page": 1,
  "dpi": 300,
  "image": { "w": 9933, "h": 7016 },
  "table_start_x": 7074,
  "word_count": 630,
  "outside_word_count": 580,
  "table_word_count": 569,
  "other_word_count": 11,
  "tables": [
    {
      "name": "table_1",
      "pixel": { "x": 7074, "y": 210, "w": 2484, "h": 1040 },
      "pdf_mm": { "x": 598.93, "y": 17.78, "w": 210.31, "h": 88.05 },
      "row_count": 13,
      "cell_count": 91,
      "rows": [
        { "row": 1, "pixel": { "x": 7395, "y": 210, "w": 2135, "h": 59 }, "cells": [
          { "col": 1, "text": "番号",
            "pixel": { "x": 7395, "y": 212, "w": 114, "h": 57 },
            "pdf_mm": { "x": 626.11, "y": 17.95, "w": 9.65, "h": 4.83 } }
        ] }
      ]
    }
  ],
  "other_words": [
    { "text": "7 24/10/31 都吉昆",
      "pixel": { "x": 6440, "y": 6281, "w": 606, "h": 79 },
      "pdf_mm": { "x": 545.25, "y": 531.79, "w": 51.31, "h": 6.69 } }
  ]
}
```

`outside_text.txt`（UTF-8 带 BOM，记事本 / Excel 直接可读）：

```
# CadPartSplitter 未分割区域文字清单（被分割部分以外的文字与坐标）
# 表格区起点 : X = 7074（零件检测时遮蔽的右侧区域，其右即表格 / 标题栏）
# 统计       : 全页文字 630 个词；零件框外 580 个（表格区 569 / 其它 11）
#              表格 10 张 / 行 77 / 单元格 546

── 一、表格 / 标题栏区文字（X ≥ 7074）──────────────────────────

[table_1]  区域 绝对(x=7074 y=210 w=2484 h=1040)  行 13 / 单元格 91
  行   1  y=210   x=7395  w=2135  h=59    单元格 7
    [列1 ] x=7395  w=114   h=57    番号
    [列2 ] x=7685  w=171   h=58    部品名
    [列3 ] x=8062  w=171   h=58    枠区分
    [列4 ] x=8327  w=114   h=58    員数
  行   2  y=290   x=7438  w=2066  h=61    单元格 7
    [列1 ] x=7438  w=29    h=49    1
    [列2 ] x=7650  w=242   h=61    ブレーキ部品
    …

── 二、其它未归属文字（零件框外、表格区左侧的页边注记等）─────────────
  y=6281  x=6440  w=606   h=79    7 24/10/31 都吉昆
```

> 分表规则：相邻行垂直间隙 > `max(80px, 3 倍行高)`，或相邻行单元格数突变 > 2 个，即视为另一张表
> （7 列部品表紧接 15 列型枠表也能切开）。列号是推断值，跨列合并的单元格会落到其中心所在列；
> 与 `parts_*` 一样只读文字层，位图输入不生成这两个文件。
> 详见 [docs/分割原理.md](docs/分割原理.md) 3.9。

## 排错

- **检测不到零件**：日志会列出 4 项检查建议（遮蔽比例 / 面积阈值 / 膨胀核 / 调试图），先开「保存调试图」看 `debug_03_dilated.png` 是否把零件糊成了一整块。
- **图纸右侧的零件没被切出来（例如同一张图上画了两个「戸当り」，只出一个）**：说明右侧零件落在了遮蔽区内。默认「自动遮蔽」会按表格/标题栏的实际起始列遮蔽（日志中有 `右侧遮蔽：自动检出表格/标题栏区 X=…`），正常情况下不会切掉右侧零件；若日志显示退回了固定比例，或图纸右侧确实有很宽的表格区，请调小「右侧遮蔽」（如 0.30）或把 `--mask-ink` 调小。
- **分不清表格区与图纸区**：`--fixed-mask` 可强制沿用固定比例遮蔽（旧行为），配合 `debug_02_masked.png` 直接看遮蔽边界位置。
- **零件被截断**：出现 `[WARN] 有零件贴近遮蔽边界` 时，说明有零件跨越了遮蔽线，适当减小「右侧遮蔽」比例。
- **PDF 报 `BadImageFormatException`**：输出目录里的 `pdfium.dll` 位数与进程不匹配。本项目固定为 x64，`CadPartSplitter.csproj` 中已显式设置 `Prefer32Bit=false` 与 `DocnetRuntime=win-x64`；若改过平台目标，请同步这两项。
- **`parts_text.txt` 里没有文字、或坐标整体偏移**：前者多为扫描件 / 文字已转曲的 PDF（无文字层，日志会提示 `该页没有文字层`，此时也不会生成 `outside_*`）；后者多为页面 `/Rotate` 旋转或 CropBox 偏移，日志会出现 `N/M 个词换算后落在画面外` 警告。`□` 这类以矢量图元绘制的方框一定取不到，属正常现象。
- **`outside_info.json` 里表格被切得太碎 / 粘成一张**：分表靠「垂直间隙 + 相邻行单元格数突变」两条启发式（3.9）。单元格数突变阈值是代码常量 `PartTextExtractor.CellCountJumpTol`（默认 2）；列数多的表若被误切，把它调大。

## 项目结构

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 入口：无参数启动 GUI，带参数走命令行 |
| `MainForm.cs` / `MainForm.Designer.cs` | 图形界面与布局 |
| `CommandLineRunner.cs` | 命令行参数解析与输出 |
| `PartSplitterService.cs` | 「加载文件 → 检测分割 → 落盘」流程封装，GUI 与 CLI 共用（写 `parts_info.json` / `parts_text.txt` / `outside_info.json` / `outside_text.txt`） |
| `PdfRenderer.cs` | PDF → OpenCV Mat（Docnet.Core / PDFium） |
| `PartDetector.cs` | 核心图像处理与零件裁切（`MaskStartX` 对外暴露表格区起点） |
| `PartTextExtractor.cs` | 读 PDF 文字层：框内按重叠率归属各零件图；框外分「表格区 / 其它」并重组成表→行→单元格 |
| `PartText.cs` | 词级文字坐标模型、行合并模型、表格行列模型、页面文字结果模型 |
| `DetectorOptions.cs` | 全部可调参数 + 日志回调 |
| `docs/分割原理.md` | 分割原理技术说明（流水线、算法依据、调参、案例复盘、文字坐标换算） |

## 构建

```
msbuild CadPartSplitter.csproj /t:Restore /p:RuntimeIdentifiers=win-x64
msbuild CadPartSplitter.csproj /p:Configuration=Release /p:RuntimeIdentifiers=win-x64
```

> `Docnet.Core` 的 build targets 会按 RID 拷贝 `pdfium.dll`，若不指定 RID
> （或 `obj/project.assets.json` 里没有 `win-x64` 这一项）会报
> *"doesn't list 'win-x64' as a RuntimeIdentifier"*。首次构建、以及改过
> `PackageReference` 之后都需要带 `/t:Restore` 重新生成 assets。
> 另外重新构建前请先关闭正在运行的 `CadPartSplitter.exe`，否则 `bin\` 下的 DLL 会被占用。

依赖（NuGet，见 `CadPartSplitter.csproj`）：`OpenCvSharp4`、`OpenCvSharp4.runtime.win`、
`Docnet.Core`（渲染）、`PdfPig`（只读 PDF 文字层，用于文字坐标）。

## 自测脚本

`_test/gen_test_raster.ps1`（PowerShell 5.1+）生成一张合成图纸 `_test/test_raster.png`
（白底 + 两个粗线框"零件" + 右侧假表格），用于验证「位图输入」分支：

```
powershell -ExecutionPolicy Bypass -File _test\gen_test_raster.ps1
bin\Debug\CadPartSplitter.exe _test\test_raster.png _test\out --area 20000 --kw 40 --kh 10 --pad 10
```

位图分支应输出 2 张零件图，`parts_text.txt` 中标注「（该图范围未检测到文字）」，
并且不生成 `outside_info.json` / `outside_text.txt`（位图没有文字层）。
