"""
通用 PDF 表格提取程序 — 适用于 CAD 导出的 PDF 文件
自动检测所有页面上的表格，无需预先知道表格数量或位置

用法:
    py extract_tables.py                         # 处理当前目录中所有 PDF
    py extract_tables.py drawing.pdf             # 处理指定文件
    py extract_tables.py drawing.pdf --preview   # 仅预览，不保存

原理:
    1. 提取每一页所有文字及其坐标
    2. 按 Y 坐标将文字分组为"行"
    3. 用「Y间距」+「列对齐度」双重标准找到连续的表格行块
       - 图纸标注（与表格主列范围不重叠）→ 直接跳过，不触发断块
       - 不同表格（列范围重叠但列不对齐）→ 断块，开始新表格
    4. 对每个块做 X 坐标聚类（不量化），自动推断列结构
    5. 过滤非空占比过低的噪声列（图形标注等）
    6. 输出到 Excel，每个表格占一个 Sheet
"""

import io
import re
import sys
from pathlib import Path

import pandas as pd
import pdfplumber

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")


# ── 可调参数 ──────────────────────────────────────────────────────────────────
Y_BAND = 8          # Y 量化精度（pt）：将 Y 差距在此范围内的词归入同一行
ROW_GAP_MAX = 40    # 同一表格内相邻行允许的最大 Y 间距（pt）
MIN_WORDS = 2       # 行中至少有几个词才被视为表格行（过滤单词图形标签）
MIN_ROWS = 2        # 表格至少包含几行
COL_GAP_MIN = 20    # X 坐标聚类时，间距超过此值视为不同列（pt）
MIN_COL_FILL = 0.15 # 列的非空占比低于此值时视为噪声列并丢弃
MIN_ALIGN = 0.45    # 新行至少有此比例的词对齐已有列，否则不归入同一表格
TITLE_ABOVE = 60    # 在表格上方多少 pt 范围内搜索标题
TITLE_BELOW = 25    # 在表格下方多少 pt 范围内搜索标题
# ─────────────────────────────────────────────────────────────────────────────

_INVALID_SHEET_CHARS = re.compile(r'[\\/:?\[\]*]')


# ══════════════════════════════════════════════════════════════════════════════
#  底层工具函数
# ══════════════════════════════════════════════════════════════════════════════

def get_page_words(pdf_path: Path, page_num: int) -> list[dict]:
    with pdfplumber.open(pdf_path) as pdf:
        if page_num >= len(pdf.pages):
            return []
        return pdf.pages[page_num].extract_words(x_tolerance=3, y_tolerance=3)


def group_words_by_row(words: list[dict]) -> dict[int, list[dict]]:
    groups: dict[int, list] = {}
    for w in words:
        key = round(w["top"] / Y_BAND) * Y_BAND
        groups.setdefault(key, []).append(w)
    return {k: sorted(v, key=lambda w: w["x0"]) for k, v in sorted(groups.items())}


def detect_column_centers(block: list[tuple]) -> list[float]:
    """
    对块内所有词语的 x0 做 1D 间距聚类，返回各列的中心 X。
    使用原始 x0 值（不量化），避免量化误差导致相近列被误判为分离。
    """
    raw_x0 = sorted(w["x0"] for _, row in block for w in row)
    if not raw_x0:
        return []
    col_groups: list[list[float]] = [[raw_x0[0]]]
    for x in raw_x0[1:]:
        if x - col_groups[-1][-1] > COL_GAP_MIN:
            col_groups.append([x])
        else:
            col_groups[-1].append(x)
    return [sum(g) / len(g) for g in col_groups]


def col_tolerance(col_centers: list[float]) -> float:
    if len(col_centers) > 1:
        min_gap = min(b - a for a, b in zip(col_centers, col_centers[1:]))
        return min_gap / 2.0 + 4.0
    return 35.0


def alignment_ratio(words: list[dict], col_centers: list[float]) -> float:
    """计算行中词语对齐到已有列中心的比例。"""
    if not col_centers or not words:
        return 0.0
    tol = col_tolerance(col_centers)
    fits = sum(
        1 for w in words
        if min(abs((w["x0"] + w["x1"]) / 2 - cx) for cx in col_centers) <= tol
    )
    return fits / len(words)


def filtered_col_centers(block: list[tuple]) -> list[float]:
    """
    返回去除噪声列后的列中心。
    噪声列 = 非空占比 < MIN_COL_FILL 的列（通常是图纸标注偶然出现在表格Y范围内）。
    用于计算表格的"有效X范围"，避免图纸标注词语污染边界。
    """
    col_centers = detect_column_centers(block)
    if not col_centers:
        return []
    n_rows = len(block)
    tol = col_tolerance(col_centers)
    min_fill = max(1, n_rows * MIN_COL_FILL)
    keep = []
    for cx in col_centers:
        count = sum(
            1 for _, row in block
            for w in row
            if abs((w["x0"] + w["x1"]) / 2 - cx) <= tol
        )
        if count >= min_fill:
            keep.append(cx)
    return keep


def effective_x_range(block: list[tuple]) -> tuple[float, float]:
    """
    计算块的有效 X 范围（基于过滤后的列中心，而非原始词语坐标）。
    避免混入的图纸标注词语（低 X 位置）错误地拉低块的 X 下界。
    """
    fcc = filtered_col_centers(block)
    if fcc:
        return fcc[0] - 15, fcc[-1] + 15
    # fallback：用所有词语
    xs = [w["x0"] for _, row in block for w in row]
    return min(xs) - 5, max(xs) + 5


def x_ranges_overlap(x_range: tuple[float, float], words: list[dict]) -> bool:
    """判断给定 X 范围与词语列表的 X 范围是否重叠。"""
    w_min = min(w["x0"] for w in words)
    w_max = max(w["x1"] for w in words)
    return w_min < x_range[1] and w_max > x_range[0]


# ══════════════════════════════════════════════════════════════════════════════
#  表格块检测
# ══════════════════════════════════════════════════════════════════════════════

def find_table_blocks(row_groups: dict[int, list]) -> list[list[tuple]]:
    """
    从行分组中找出所有表格块。

    对于每一个新行，判断规则（按顺序）：
      1. Y 间距 > ROW_GAP_MAX           → 断块（不同表格）
      2. 列对齐度 ≥ MIN_ALIGN            → 归入当前块
      3. 列对齐度 < MIN_ALIGN，且与当前块有效 X 范围有重叠 → 断块（新表格）
      4. 列对齐度 < MIN_ALIGN，且与当前块有效 X 范围无重叠 → 跳过（图纸噪声）

    有效 X 范围 = 去除噪声列后的列中心范围，防止被混入的图纸标注词语误导。
    """
    sorted_ys = sorted(row_groups.keys())
    valid = [
        (y, row_groups[y])
        for y in sorted_ys
        if len(row_groups[y]) >= MIN_WORDS
    ]
    if not valid:
        return []

    blocks: list[list] = []
    current = [valid[0]]
    cur_cols = detect_column_centers(current)

    for i in range(1, len(valid)):
        y_gap = valid[i][0] - valid[i - 1][0]
        new_words = valid[i][1]

        # 规则1：Y 间距过大 → 强制断块
        if y_gap > ROW_GAP_MAX:
            _flush(current, blocks)
            current = [valid[i]]
            cur_cols = detect_column_centers(current)
            continue

        # 规则2~4：检查列对齐度
        align = alignment_ratio(new_words, cur_cols) if cur_cols else 1.0

        if align >= MIN_ALIGN:
            current.append(valid[i])
            cur_cols = detect_column_centers(current)
        else:
            # 用有效 X 范围（去除噪声列）来区分"新表格"与"图纸噪声"
            eff_range = effective_x_range(current)
            if x_ranges_overlap(eff_range, new_words):
                # 规则3：X 范围重叠 → 不同表格，断块
                _flush(current, blocks)
                current = [valid[i]]
                cur_cols = detect_column_centers(current)
            else:
                # 规则4：X 范围无重叠 → 图纸标注噪声，跳过
                pass

    _flush(current, blocks)
    return blocks


def _flush(current: list, blocks: list) -> None:
    if len(current) >= MIN_ROWS:
        blocks.append(list(current))


# ══════════════════════════════════════════════════════════════════════════════
#  块 → DataFrame
# ══════════════════════════════════════════════════════════════════════════════

def block_to_dataframe(block: list[tuple]) -> pd.DataFrame | None:
    """
    将表格块转换为 DataFrame：
      - 根据列中心将词语分配到对应列（只分配在容差范围内的词）
      - 丢弃非空占比低于 MIN_COL_FILL 的噪声列
    """
    col_centers = detect_column_centers(block)
    if not col_centers or len(col_centers) < 2:
        return None

    n_cols = len(col_centers)
    tol = col_tolerance(col_centers)
    all_rows = []

    for _, row_words in block:
        cells = [""] * n_cols
        for w in row_words:
            x_mid = (w["x0"] + w["x1"]) / 2
            _, best_col = min(
                (abs(x_mid - cx), i) for i, cx in enumerate(col_centers)
            )
            if abs(x_mid - col_centers[best_col]) <= tol:
                cells[best_col] = (cells[best_col] + " " + w["text"]).strip()
        all_rows.append(cells)

    # 过滤噪声列
    n_rows = len(all_rows)
    min_fill = max(1, n_rows * MIN_COL_FILL)
    keep = [
        i for i in range(n_cols)
        if sum(1 for r in all_rows if r[i]) >= min_fill
    ]
    if len(keep) < 2:
        return None

    filtered = [[r[i] for i in keep] for r in all_rows]
    non_empty = [r for r in filtered if any(r)]
    return pd.DataFrame(non_empty) if non_empty else None


# ══════════════════════════════════════════════════════════════════════════════
#  标题检测
# ══════════════════════════════════════════════════════════════════════════════

def find_table_title(
    row_groups: dict[int, list],
    block: list[tuple],
    all_block_ys: set[int],
) -> str:
    """
    在表格上方（TITLE_ABOVE pt）或下方（TITLE_BELOW pt）搜索标题文本。
    只查找不属于任何表格块的行，且 X 范围与表格有效范围重叠。
    """
    block_top = block[0][0]
    block_bottom = block[-1][0]
    eff_min, eff_max = effective_x_range(block)

    best: tuple[float, str] | None = None
    for y, row in row_groups.items():
        if y in all_block_ys:
            continue
        dist_above = block_top - y
        dist_below = y - block_bottom
        if 0 < dist_above <= TITLE_ABOVE:
            dist = dist_above
        elif 0 < dist_below <= TITLE_BELOW:
            dist = dist_below
        else:
            continue

        row_x_min = min(w["x0"] for w in row)
        row_x_max = max(w["x1"] for w in row)
        if row_x_min >= eff_max or row_x_max <= eff_min:
            continue

        text = " ".join(w["text"] for w in row)
        if best is None or dist < best[0]:
            best = (dist, text)

    return best[1] if best else ""


# ══════════════════════════════════════════════════════════════════════════════
#  主提取逻辑
# ══════════════════════════════════════════════════════════════════════════════

def sanitize_sheet_name(name: str) -> str:
    return _INVALID_SHEET_CHARS.sub("_", name)[:31]


def extract_tables_from_page(words: list[dict], page_num: int) -> list[dict]:
    row_groups = group_words_by_row(words)
    blocks = find_table_blocks(row_groups)
    all_block_ys = {y for block in blocks for y, _ in block}

    results = []
    for idx, block in enumerate(blocks, start=1):
        df = block_to_dataframe(block)
        if df is None or df.empty:
            continue

        title = find_table_title(row_groups, block, all_block_ys)
        prefix = f"P{page_num + 1}_T{idx:02d}"
        suffix = "_" + title[: 31 - len(prefix) - 1] if title else ""
        name = sanitize_sheet_name(prefix + suffix)

        results.append({
            "name": name,
            "title": title,
            "df": df,
            "page": page_num + 1,
            "rows": len(df),
            "cols": len(df.columns),
        })

    return results


def extract_tables_from_pdf(pdf_path: Path) -> list[dict]:
    all_tables = []
    with pdfplumber.open(pdf_path) as pdf:
        n_pages = len(pdf.pages)
    for page_num in range(n_pages):
        words = get_page_words(pdf_path, page_num)
        tables = extract_tables_from_page(words, page_num)
        all_tables.extend(tables)
    return all_tables


def save_to_excel(tables: list[dict], output_path: Path) -> None:
    seen: dict[str, int] = {}
    with pd.ExcelWriter(output_path, engine="openpyxl") as writer:
        for t in tables:
            base = t["name"]
            count = seen.get(base, 0)
            seen[base] = count + 1
            sheet = base if count == 0 else sanitize_sheet_name(f"{base}_{count}")
            t["df"].to_excel(writer, sheet_name=sheet, index=False, header=False)
    print(f"已保存: {output_path}")


# ══════════════════════════════════════════════════════════════════════════════
#  命令行入口
# ══════════════════════════════════════════════════════════════════════════════

def main() -> None:
    args = sys.argv[1:]
    preview_only = "--preview" in args
    args = [a for a in args if not a.startswith("--")]

    if args:
        pdf_files = [Path(args[0])]
    else:
        script_dir = Path(__file__).parent
        pdf_files = sorted(script_dir.glob("*.pdf"))
        if not pdf_files:
            print("用法: py extract_tables.py <file.pdf> [--preview]")
            sys.exit(1)

    for pdf_path in pdf_files:
        if not pdf_path.exists():
            print(f"错误: {pdf_path} 不存在")
            continue

        print(f"\n{'='*60}")
        print(f"文件: {pdf_path.name}")
        print("=" * 60)

        tables = extract_tables_from_pdf(pdf_path)

        if not tables:
            print("  未检测到表格")
            continue

        print(f"\n共检测到 {len(tables)} 个表格:")
        for t in tables:
            title_hint = f"  ← {t['title'][:40]}" if t["title"] else ""
            print(f"  [{t['name']}]  {t['rows']} 行 x {t['cols']} 列{title_hint}")

        if not preview_only:
            output = pdf_path.with_name(pdf_path.stem + "_tables.xlsx")
            save_to_excel(tables, output)

        for t in tables:
            print(f"\n--- {t['name']} ---")
            print(t["df"].to_string(index=False, header=False, max_rows=8))
            if t["rows"] > 8:
                print(f"  ... (共 {t['rows']} 行，已截断)")


if __name__ == "__main__":
    main()
