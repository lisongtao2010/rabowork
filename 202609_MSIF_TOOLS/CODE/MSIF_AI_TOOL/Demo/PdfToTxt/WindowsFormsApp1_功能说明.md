# WindowsFormsApp1（PDF 分解工具）功能说明

> 对应解决方案：`ツール\PyMuPDF\WindowsFormsApp1.sln`
> 主程序：`WindowsFormsApp1\bin\Debug\WindowsFormsApp1.exe`
> 更新日期：2026-10-26

---

## 1. 一句话概要

**WindowsFormsApp1** 是一个 Windows 桌面小工具（.NET Framework 4.8 WinForms），
界面只有一个文件夹选择框和一个按钮。点击按钮后，它会把所选文件夹里的
**所有図面 PDF 一次性批量分解**，对每个 PDF 同时做两种提取，结果保存到该文件夹下
两个自动创建的子文件夹中：

| 子文件夹 | 输出文件 | 提取引擎 | 内容 |
|----------|----------|----------|------|
| `<文件夹>\xy\` | `<図番号>_xy.txt` | **PyMuPDF**（Python `fitz` 库） | 每一页的每个文字及其坐标（x0/y0/x1/y1） |
| `<文件夹>\table\` | `<図番号>_table.txt` | **MarkItDown**（Microsoft `markitdown` 库） | PDF 内容转 Markdown（重点是表格，保留表格结构） |

其中 **図番号 = PDF 文件名去掉 `.pdf` 扩展名**（例如 `H310U10004.pdf` → `H310U10004_xy.txt`）。

---

## 2. "把两个工具合并到了一起"的由来

本软件合并了以前**两个各自独立运行的工具**：

| 合并前 | 工具 | 输入 | 输出 | 现状 |
|--------|------|------|------|------|
| 工具① | `ZUOBIAO.py`（PyMuPDF 坐标提取，一次只处理 1 个 PDF，命令行传参） | 单个 PDF 路径 + 输出 txt 路径 | 坐标 txt | 已并入 `PDF_SPLIT.py` 的 `extract_xy()`；`bin\Debug\ZUOBIAO.py` 历史残留文件已于 2026-10-26 删除 |
| 工具② | MarkItDown 表格转换（命令行/脚本单独调用） | 单个 PDF | Markdown txt | 已并入 `PDF_SPLIT.py` 的 `extract_table()`；旧试验脚本 `ツール\MarkItDown\run.py` 已于 2026-10-26 删除（程序用的是 pip 安装的 markitdown 库，与该文件夹无关） |

合并后的形态：

- 两个引擎被整合进一个 Python 脚本 `PDF_SPLIT.py`（脚本开头自述即"PyMuPDF + MarkItDown 合并版"），
  对每个 PDF **依次跑两种提取**，一次批量化；
- C# WinForms 程序作为**图形外壳**，负责选文件夹、启动脚本、显示进度和结果弹窗，
  让不熟悉命令行的人也能一键操作。

---

## 3. 软件构成（文件清单）

```
PyMuPDF\
├─ WindowsFormsApp1.sln                 解决方案（VS2022，Format 12.00）
└─ WindowsFormsApp1\
   ├─ WindowsFormsApp1.csproj           项目：.NET Framework 4.8 / WinExe / AnyCPU / C#
   ├─ Program.cs                        入口：Application.Run(new Form1())
   ├─ Form1.cs                          ★全部业务逻辑（界面 + 调度 Python）
   ├─ Form1.Designer.cs                 标准模板文件（InitializeComponent 为空壳）
   ├─ App.config                        标准 app.config（无自定义配置项）
   ├─ PDF_SPLIT.py                      ★实际干活的 Python 脚本（批量分解）
   ├─ Properties\...                    AssemblyInfo / Resources / Settings 标准模板
   ├─ PART_BASIC_INFO_API.py / .md      ┐
   ├─ PART_CODE_REF_API.py / .md        │ 配套脚本（不属于本 sln 的编译产物，
   ├─ PROC_DRAWING_REF_API.py / .md     │ 但与 exe 同目录存放，消费本工具的输出，
   ├─ TITLE_BLOCK_API.py / .md          │ 见第 10 节）
   ├─ XY_TO_DB.py / AI_LOGIN_PROMPTS.md ┘
   └─ bin\Debug\
      ├─ WindowsFormsApp1.exe           运行时只需要 exe + PDF_SPLIT.py 两个文件
      └─ PDF_SPLIT.py                   （csproj 中以 Content/CopyToOutputDirectory 随构建自动复制）
                                          （旧版残留 ZUOBIAO.py 已于 2026-10-26 删除）
```

---

## 4. 技术栈

| 层 | 技术 |
|----|------|
| 界面 | C# WinForms（.NET Framework 4.8），控件全部在 `SetupCustomGui()` 里代码动态创建，不用设计器拖拽 |
| 调度 | `System.Diagnostics.Process` 启动 `python PDF_SPLIT.py <文件夹>`，重定向 stdout/stderr |
| 提取引擎① | Python + PyMuPDF（`import fitz`，`page.get_text("words")`） |
| 提取引擎② | Python + Microsoft MarkItDown（`MarkItDown().convert(pdf)`） |
| 编码 | 全链路 UTF-8（脚本 `sys.stdout.reconfigure(encoding="utf-8")` + C# 侧 `StandardOutputEncoding = UTF8` + 环境变量 `PYTHONIOENCODING=utf-8`，防止中文/日文乱码） |

---

## 5. 画面与操作流程

窗口标题：**"PDF 分解工具 (坐标 + 表格)"**（620×200，固定大小，屏幕居中，不可最大化）。

| 控件 | 说明 |
|------|------|
| `PDF 文件夹:` 输入框 + **浏览...** 按钮 | 选择存放 PDF 図面的文件夹（FolderBrowserDialog） |
| **一键分解 (坐标 + 表格)** 按钮（加粗大按钮） | 开始批量处理，处理中按钮禁用 |
| 状态 Label（蓝字） | 就绪 / 正在分解中（橙色）/ 分解成功（绿色）/ 失败（红色） |

**操作步骤：**

1. 点「浏览...」选择図面 PDF 所在文件夹（也可手动输入路径）；
2. 点「一键分解 (坐标 + 表格)」；
3. 等待状态由"正在分解中（共 N 个PDF），请稍候..."变为"分解成功！N 个PDF → xy 文件夹 + table 文件夹"；
4. 弹窗确认后，到 `<所选文件夹>\xy\` 和 `<所选文件夹>\table\` 取结果。

---

## 6. 内部处理流程（点了按钮之后发生了什么）

### 6.1 C# 侧（Form1.BtnSplit_Click）

1. **输入校验**：文件夹为空/不存在 → 弹警告"请确认已选择有效的 PDF 文件夹！"；
2. **统计 PDF**：只统计所选文件夹**第一层**（不进子文件夹）中扩展名严格为 `.pdf`（不区分大小写）的文件；
   数量为 0 → 弹警告"该文件夹中没有找到 PDF 文件！"；
3. **检查脚本**：`PDF_SPLIT.py` 必须与 exe 同目录（`Application.StartupPath`），否则报错；
4. **禁用按钮**，状态变橙色"正在分解中（共 N 个PDF），请稍候..."，`DoEvents()` 刷新界面；
5. **启动子进程**：`python PDF_SPLIT.py "<文件夹>"`（隐藏窗口、重定向输出、UTF-8）；
   - stdout 用 `ReadToEndAsync()` **异步**读取（脚本会持续打印 `[i/N] 正在处理: xxx` 进度，防止输出量大时管道堵塞死锁）；
   - stderr 同步读取；`WaitForExit()` 等待结束；
6. **按退出码汇报**：
   - 退出码 0 → 绿色"分解成功！N 个PDF → xy 文件夹 + table 文件夹" + 完成弹窗（含两个输出目录路径）；
   - 非 0 → 红色"分解失败" + 弹窗显示 Python 的 stderr 报错；
   - 启动 Python 本身失败（如 python 不在 PATH）→ 弹"启动 Python 失败…请检查 Python 是否已加入环境变量"；
7. `finally` 中恢复按钮可用。

### 6.2 Python 侧（PDF_SPLIT.py main()）

1. 校验命令行参数（用法：`python PDF_SPLIT.py <PDF所在文件夹>`）和文件夹存在性；
2. `find_pdfs()`：列出第一层所有 PDF，**按文件名排序**后逐个处理；
3. 在 PDF 所在文件夹下创建（已存在则复用）`xy\` 和 `table\` 两个子文件夹；
4. 对每个 PDF（図番号 = 文件名去扩展名）：
   - **提取① 坐标**（`extract_xy`）：PyMuPDF 逐页 `get_text("words")`，写出 `<図番号>_xy.txt`；
   - **提取② 表格**（`extract_table`）：MarkItDown 整体转换，写出 `<図番号>_table.txt`；
   - 两个提取**互不影响**：某一个失败只记录错误继续处理下一个 PDF；
   - 每处理一个打印一行进度 `[i/N] 正在处理: xxx.pdf`；
5. 全部结束后：有任何失败 → 逐条打印 `ERROR: ...` 并打印 `FINISHED ok/total（有失败）`，退出码 1；
   全部成功 → 打印 `SUCCESS ok/total`、`XY_DIR: ...`、`TABLE_DIR: ...`，退出码 0。

---

## 7. 输出文件格式

### 7.1 `<図番号>_xy.txt`（PyMuPDF 坐标文件）

纯文本，按页分块。每页先一行页头，随后每行一个"词"：

```
--- 第 1 页 (共 633 个词) ---
文字: 'H310U10004' | 坐标: x0=2065.15, y0=1581.76, x1=2246.13, y1=1616.02
文字: 'H310' | 坐标: x0=2020.17, y0=1541.58, x1=2056.85, y1=1558.71
文字: '１７ラシッサ' | 坐标: x0=2062.08, y0=1541.58, x1=2144.47, y1=1558.71
```

- `x0/y0` = 文字左上角坐标，`x1/y1` = 右下角坐标（PDF 点坐标，原点在页面左上）；
- 下游 AI 解析工具（如 `AiApi\TxtExcelService.cs` 的 `TxtPreprocessor`）正是靠
  **y 坐标分行、x 坐标排序**来重建表格行列关系的。

### 7.2 `<図番号>_table.txt`（MarkItDown 表格文件）

PDF 内容整体转成的 Markdown 文本，表格以 `|` 管道符呈现（列可能错位、含大量空列，但保留了行列结构）：

```
| 番号 | 部品名 | 枠区分 | 員数 | 基本寸法 | 切断寸法 | 部品コード |
| --- | --- | --- | --- | --- | --- | --- |
| 2 | 鬼目ナット（ブレーキ取付用） | 三方枠 | 8 | - |  | MDT955 |
| 7 | 部品セット | 三方枠 | 1 | - |  | 下表参照 |
枠用部品セット詳細表
| 見込み | 部品コード |
| NC95/115 | MZA□968 |
| その他 | MZA674 |
```

---

## 8. 运行前提（部署要求）

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows（.NET Framework 4.8 已安装即可，Win10/11 自带） |
| Python | 已安装并**加入 PATH**（程序直接调用 `python` 命令） |
| Python 库 | `pip install pymupdf markitdown`（缺库时对应 PDF 会报"坐标提取失败"或"表格提取失败"） |
| 部署文件 | 运行时只需 `WindowsFormsApp1.exe` + `PDF_SPLIT.py` **放在同一目录**（csproj 已配置自动复制，正常构建即满足） |

---

## 9. 错误处理一览

| 场景 | 表现 |
|------|------|
| 未选文件夹 / 文件夹不存在 | 弹警告，不启动处理 |
| 文件夹里没有 PDF | 弹警告，不启动处理 |
| 找不到 PDF_SPLIT.py | 弹错误，提示与 exe 同目录 |
| python 不在 PATH | 弹"启动 Python 失败" |
| 个别 PDF 提取失败（损坏/加密/缺库） | **不中断整批**，脚本记录 ERROR 继续处理其余 PDF，最后以退出码 1 汇总失败明细 |
| 输出 txt 打不开/磁盘问题 | 同上，按单文件失败处理 |

---

## 10. 在整体流水线中的位置

本工具是"図面 PDF → 数据库 / Excel"整条链路的**最上游**：

```
図面 PDF 文件夹
   │  ← WindowsFormsApp1.exe（本工具）一键分解
   ├─→ xy\<図番号>_xy.txt     ──→ AiApi（AI 解析 → 部品表 Excel，参照元=図番号）
   │                            ──→ AI 指挥话术（AI_LOGIN_PROMPTS.md 的识别数据源）
   └─→ table\<図番号>_table.txt ──→ 人工核对 / AI 识别辅助
                                     ↓
              XY_TO_DB.py + 四个 API 脚本（PART_BASIC_INFO / PART_CODE_REF /
              PROC_DRAWING_REF / TITLE_BLOCK）→ 図面\db\zumen_data.sqlite
                                     ↓
              数据库查询 → 填充 Excel 部品表模板（部品名同名匹配）
```

配套脚本与 exe 同目录存放（见第 3 节文件清单），各脚本的用法见各自的 `.md` 文档。

---

## 11. 已知注意事项

1. **只处理文件夹第一层**的 PDF，不递归子文件夹（C# 统计与 Python 遍历口径一致）；
2. PDF 文件名即図番号：文件名不规范会直接传导到输出文件名和下游匹配；
3. MarkItDown 输出的表格**列宽/错位不做校正**（保留原样），需要人工或 AI 判断列归属；
   PyMuPDF 坐标文件里也可能存在重复文字、旋转文字等噪声，由下游预处理去重；
4. 同名 PDF 重复运行会**直接覆盖**旧的 `_xy.txt` / `_table.txt`（无备份）；
5. `bin\Debug\ZUOBIAO.py`（旧版单文件坐标脚本残留，无任何程序引用）已于 2026-10-26 删除
   （备份在 `ツール\_削除済みバックアップ\`）。

