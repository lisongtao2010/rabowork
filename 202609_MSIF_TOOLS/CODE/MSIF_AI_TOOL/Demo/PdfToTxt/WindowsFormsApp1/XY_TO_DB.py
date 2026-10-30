# -*- coding: utf-8 -*-
"""
xy 坐标文件 → SQLite 数据库登录脚本

把 PDF_SPLIT.py 输出的 <図番号>_xy.txt 中、图纸右半边表格的 6 个部分分别
解析并登录到 SQLite 数据库的 6 张表（左侧图形区文字 → figure_text）：

  part_basic_info            部品基本信息表（番号/部品名/枠区分/員数/基本寸法/切断寸法/部品コード）
  part_code_ref              部品コード参照表（見込み別の部品コード）
  frame_parts_set_detail     枠用部品セット詳細表（タイプ名別の部品構成 A～G員数）
  part_color_expansion       部品色展開表（記号別 製品色→部品色）
  proc_drawing_ref           要領図参照表（項目記号/要領図名/参照図番）
  title_block                タイトルブロック（図番/品名/承認日/承認者/改訂履歴）

另外，把 PDF 左侧图形区（图纸本体）的文字分类整理后登录到 figure_text
（図形部文字表）：分類 = 部品番号（バルーン数字）/ 部品名（図中ラベル）/
部位記号（A部 等）/ 寸法値 / 注記，每条记录带整理番号与坐标。

另外还登录一张 table_order（表顺序记录表）：按 PDF 中自上而下的顺序记录
上述 6 张表（区域名称、对应表名、y 坐标区间）。

用法:
    python XY_TO_DB.py <xy文件路径> [--db <数据库文件路径>]

说明:
  * 図番号(zumen_no)取自文件名（去掉 _xy 后缀）。每张表都有 zumen_no 列，
    因此同一个数据库文件可以存放多张图纸的数据。
  * 同一図番号重复执行时，先删除旧数据再重新登录（可重复执行）。
  * 默认数据库路径: <xy文件所在文件夹>\\..\\db\\zumen_data.sqlite
  * title_block 的 rev1～rev5 对应改訂履歴行位（rev1=最上行=最新改訂）。
"""

import argparse
import os
import re
import sqlite3
import sys

# 保证被 C# 程序调用（重定向输出）时不乱码
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

# xy 文件每行的格式
WORD_RE = re.compile(
    r"^文字: '(.*)' \| 坐标: x0=([-\d.]+), y0=([-\d.]+), x1=([-\d.]+), y1=([-\d.]+)$"
)
# 部品色展開表中的颜色代码（1～4 个英文字母，如 DD / M / WA / T）
COLOR_RE = re.compile(r"^[A-Za-z]{1,4}$")

RIGHT_X_MIN = 1700.0    # 部品表～要領図参照表区域的左边界
TITLE_X_MIN = 1540.0    # タイトルブロック区域左边界（含改訂履歴）
LEFT_X_MAX = 1520.0     # 左侧图形区右边界（改訂履歴的更左侧）
REV_X_MAX = 1800.0      # 改訂履歴表右边界
AUX_X_MIN = 1800.0      # タイトルブロック中间窄列（如 16/09/23）
AUX_X_MAX = 2015.0
MAIN_X_MIN = 2015.0     # タイトルブロック主区（承認者/品名/図番/承認日）
ROW_TOL = 9.0           # 行聚类容差（按文字 y 中心）
JOIN_GAP = 4.0          # 同一单元格内词间隙 >= 该值时用空格连接


class Word:
    """一个文字块（文字及其坐标）"""

    __slots__ = ("text", "x0", "y0", "x1", "y1")

    def __init__(self, text, x0, y0, x1, y1):
        self.text = text
        self.x0, self.y0, self.x1, self.y1 = x0, y0, x1, y1

    @property
    def cx(self):
        return (self.x0 + self.x1) / 2.0

    @property
    def cy(self):
        return (self.y0 + self.y1) / 2.0

    def __repr__(self):
        return f"{self.text}@({self.x0:.0f},{self.y0:.0f})"

def parse_xy_file(path):
    """解析 xy 文件，返回第 1 页的全部 Word 列表"""
    words = []
    page, page_count = 1, 0
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("--- 第"):
                page_count += 1
                page = page_count
                continue
            m = WORD_RE.match(line)
            if m:
                x0, y0, x1, y1 = (float(v) for v in m.groups()[1:])
                words.append((page, Word(m.group(1), x0, y0, x1, y1)))
    if page_count > 1:
        print(f"[警告] xy 文件共 {page_count} 页，仅处理第 1 页")
    return [w for p, w in words if p == 1]


def cluster_rows(words, tol=ROW_TOL):
    """按 y 中心把文字聚类成行；行内按 x 排序，行间按 y 排序"""
    rows, ref = [], None
    for w in sorted(words, key=lambda w: w.cy):
        if rows and w.cy - ref <= tol:
            rows[-1].append(w)
        else:
            rows.append([w])
            ref = w.cy
    for r in rows:
        r.sort(key=lambda w: w.x0)
    return rows


def join_cell(words):
    """把同一单元格内的多个词拼成一个字符串（间隙大时补空格）"""
    ws = sorted(words, key=lambda w: w.x0)
    out = ws[0].text
    for prev, cur in zip(ws, ws[1:]):
        out += (" " if cur.x0 - prev.x1 >= JOIN_GAP else "") + cur.text
    return out


def band_words(words, top, bottom=None):
    """取 y0 在 [top, bottom) 区间内的文字"""
    if bottom is None:
        return [w for w in words if w.y0 >= top]
    return [w for w in words if top <= w.y0 < bottom]


def zone_cells(row, zones):
    """按 x 区间把一行文字分配到各列；zones = [(列名, lo, hi), ...]"""
    cells = {name: [] for name, _, _ in zones}
    stray = []
    for w in row:
        for name, lo, hi in zones:
            if lo <= w.cx < hi:
                cells[name].append(w)
                break
        else:
            stray.append(w)
    return cells, stray


def is_note_row(row):
    """是否注释行（以 ※ 开头的词所在的行）"""
    return any(w.text.startswith("※") for w in row)

def locate_sections(words):
    """根据锚点文字确定右侧 5 个表格部分的 y 起点"""
    right = [w for w in words if w.x0 >= RIGHT_X_MIN]
    tops = {"s1": None, "s2": None, "s3": None, "s4": None, "s5": None}

    # S1: 部品基本信息表表头（番号/部品名/…）
    for r in cluster_rows(right):
        texts = {w.text for w in r}
        if "番号" in texts and "部品名" in texts:
            tops["s1"] = min(w.y0 for w in r) - 10.0
            break
    # S2: 「枠用部品セット詳細表」区块标题（其下紧跟部品コード参照表）
    hits = [w for w in right if "部品セット詳細表" in w.text]
    if hits:
        tops["s2"] = min(w.y0 for w in hits) - 5.0
    # S3: タイプ名表头（タイプ別の部品構成表）
    hits = [w for w in right if w.text == "タイプ名"]
    if hits:
        tops["s3"] = min(w.y0 for w in hits) - 5.0
    # S4: 部品色展開表图例（□：部品色展開表 等）
    hits = [w for w in right if "部品色展開表" in w.text]
    if hits:
        tops["s4"] = min(w.y0 for w in hits) - 5.0
    # S5: 要領図参照表表头（項目/図番）
    for r in cluster_rows(right):
        texts = {w.text for w in r}
        if "項目" in texts and "図番" in texts:
            tops["s5"] = min(w.y0 for w in r) - 5.0
            break

    names = {"s1": "部品基本信息表", "s2": "部品コード参照表",
             "s3": "枠用部品セット詳細表", "s4": "部品色展開表", "s5": "要領図参照表"}
    missing = [names[k] for k, v in tops.items() if v is None]
    if missing:
        raise RuntimeError("未找到锚点文字: " + "、".join(missing))
    return right, tops["s1"], tops["s2"], tops["s3"], tops["s4"], tops["s5"]


def _nearest_cells(row, centers):
    """把一行文字按 x 中心分配到最近的列（centers 与列一一对应）"""
    cells = [[] for _ in centers]
    for w in row:
        i = min(range(len(centers)), key=lambda k: abs(w.cx - centers[k]))
        cells[i].append(w)
    return cells


def parse_basic_info(right, top, bottom, log):
    """S1 部品基本信息表 → part_basic_info"""
    rows = cluster_rows(band_words(right, top, bottom))
    header = next((r for r in rows
                   if "番号" in {w.text for w in r}
                   and "部品名" in {w.text for w in r}), None)
    if header is None:
        raise RuntimeError("部品基本信息表: 未找到表头行")
    cols = ["番号", "部品名", "枠区分", "員数", "基本寸法", "切断寸法", "部品コード"]
    hcenters = {w.text: w.cx for w in header}
    for c in cols:
        if c not in hcenters:
            raise RuntimeError(f"部品基本信息表: 表头缺少「{c}」")
    centers = [hcenters[c] for c in cols]

    records = []
    for r in rows:
        if r is header or is_note_row(r):
            continue
        cells = _nearest_cells(r, centers)
        if not any(cells):
            continue
        vals = [join_cell(c) if c else None for c in cells]
        seq = vals[0]
        try:
            seq = int(seq)
        except (TypeError, ValueError):
            log.append(f"部品基本信息表: 番号不是数字: {seq!r}")
            seq = None
        records.append({
            "seq_no": seq,
            "part_name": vals[1],
            "frame_class": vals[2],
            "quantity": vals[3],
            "base_dim": vals[4],
            "cut_dim": vals[5],
            "part_code": vals[6],
        })
    return records


def parse_code_ref(right, top, bottom, log):
    """S2 部品コード参照表（見込み別の部品コード）→ part_code_ref"""
    rows = cluster_rows(band_words(right, top, bottom))
    data_rows = []
    for r in rows:
        texts = {w.text for w in r}
        if any("詳細表" in t for t in texts):
            continue                       # 区块标题行
        if any("見込み" in t for t in texts) or "部品コード" in texts:
            continue                       # 表头行
        if is_note_row(r):
            continue
        data_rows.append(r)
    if not data_rows:
        log.append("部品コード参照表: 没有解析到数据行")
        return []

    # 用全部数据词的 x 中心找最大间隙，把左右两列分开
    cxs = sorted(w.cx for r in data_rows for w in r)
    if len(cxs) < 2:
        raise RuntimeError("部品コード参照表: 数据太少，无法分列")
    gi = max(range(len(cxs) - 1), key=lambda i: cxs[i + 1] - cxs[i])
    boundary = (cxs[gi] + cxs[gi + 1]) / 2.0

    records = []
    for r in data_rows:
        left = [w for w in r if w.cx < boundary]
        right_ = [w for w in r if w.cx >= boundary]
        if not left and not right_:
            continue
        records.append({
            "rail_type": join_cell(left) if left else None,
            "part_code": join_cell(right_) if right_ else None,
        })
    return records

def parse_set_detail(right, top, bottom, log):
    """S3 枠用部品セット詳細表（タイプ名別の部品構成）→ frame_parts_set_detail"""
    rows = cluster_rows(band_words(right, top, bottom))
    header = next((r for r in rows if "タイプ名" in {w.text for w in r}), None)
    if header is None:
        raise RuntimeError("枠用部品セット詳細表: 未找到表头行")
    cols = ["タイプ名"] + [f"{k}{s}" for k in "ABCDEFG" for s in ("", "員数")]
    hcenters = {w.text: w.cx for w in header}
    for c in cols:
        if c not in hcenters:
            raise RuntimeError(f"枠用部品セット詳細表: 表头缺少「{c}」")
    centers = [hcenters[c] for c in cols]

    records = []
    for r in rows:
        if r is header or is_note_row(r):
            continue
        cells = _nearest_cells(r, centers)
        if not any(cells):
            continue
        vals = [join_cell(c) if c else None for c in cells]
        rec = {"type_name": vals[0]}
        for idx, k in enumerate("ABCDEFG"):
            rec[f"mark_{k.lower()}"] = vals[1 + idx * 2]
            rec[f"qty_{k.lower()}"] = vals[2 + idx * 2]
        records.append(rec)
    return records


def parse_color_expansion(right, top, bottom, log):
    """S4 部品色展開表（記号別 製品色→部品色）→ part_color_expansion"""
    band = band_words(right, top, bottom)
    legends = sorted((w for w in band if "部品色展開表" in w.text), key=lambda w: w.x0)
    if not legends:
        raise RuntimeError("部品色展開表: 未找到图例（□：部品色展開表 等）")
    marks, lx = [], []
    for w in legends:
        mark = w.text.split("部品色展開表")[0].rstrip("：:").strip()
        marks.append(mark or "?")
        lx.append(w.x0)
    legend_bottom = max(w.y1 for w in legends)

    # 数据词 = 图例行以下、形如颜色代码（1～4 个英文字母）的词
    data_words = []
    for w in band:
        if w.y0 < legend_bottom + 3.0:
            continue
        if COLOR_RE.match(w.text):
            data_words.append(w)
        else:
            log.append(f"部品色展開表: 忽略非颜色代码: {w.text!r} @y={w.y0:.0f}")

    records = []
    for k, mark in enumerate(marks):
        lo = lx[k] - 8.0
        hi = (lx[k + 1] - 8.0) if k + 1 < len(lx) else (lx[k] + 95.0)
        mid = (lo + hi) / 2.0
        pw = [w for w in data_words if lo <= w.cx < hi]
        for r in cluster_rows(pw):
            prod = [w for w in r if w.cx < mid]
            part = [w for w in r if w.cx >= mid]
            if not prod and not part:
                continue
            records.append({
                "ref_mark": mark,
                "product_color": join_cell(prod) if prod else None,
                "part_color": join_cell(part) if part else None,
            })
    return records


# 要領図参照表各列的 x 区间
PROC_ZONES = [
    ("remarks", 1795.0, 1830.0),
    ("item_mark", 1830.0, 1875.0),
    ("category", 1875.0, 1899.0),
    ("drawing_name", 1899.0, 2160.0),
    ("ref_zumen_no", 2160.0, 2330.0),
]


def parse_proc_ref(right, top, bottom, log):
    """S5 要領図参照表 → proc_drawing_ref"""
    rows = cluster_rows(band_words(right, top, bottom))
    records = []
    for r in rows:
        texts = {w.text for w in r}
        if "項目" in texts or "図番" in texts:
            continue                       # 表头行
        if is_note_row(r):
            continue
        cells, stray = zone_cells(r, PROC_ZONES)
        for w in stray:
            log.append(f"要領図参照表: 忽略区域外文字: {w.text!r} @x={w.x0:.0f}")
        rec = {k: (join_cell(v) if v else None) for k, v in cells.items()}
        if any(v is not None for v in rec.values()):
            records.append(rec)
    return records

# 改訂履歴各列的 x 区间
REV_ZONES = [
    ("no", 1535.0, 1565.0),
    ("sub", 1565.0, 1595.0),
    ("date", 1595.0, 1645.0),
    ("person", 1645.0, 1720.0),
    ("request_no", 1720.0, 1762.0),
    ("content", 1762.0, 1800.0),
]
MAX_REVS = 5   # title_block 最多保存 5 行改訂履歴


def parse_title_block(words, top, zumen_no, log):
    """S6 タイトルブロック → title_block（每张图纸 1 行）"""
    band = [w for w in words if w.y0 >= top and w.x0 >= TITLE_X_MIN]

    # ---- 左侧：改訂履歴（从上到下 = 从新到旧）----
    rev_words = [w for w in band if w.cx < REV_X_MAX]
    revs = []
    for r in cluster_rows(rev_words):
        cells, stray = zone_cells(r, REV_ZONES)
        for w in stray:
            log.append(f"タイトルブロック: 改訂履歴区域外文字: {w.text!r}")
        rec = {k: (join_cell(v) if v else None) for k, v in cells.items()}
        if any(v is not None for v in rec.values()):
            revs.append(rec)
    if len(revs) > MAX_REVS:
        log.append(f"タイトルブロック: 改訂行有 {len(revs)} 行，仅保留 {MAX_REVS} 行")
        revs = revs[:MAX_REVS]

    # ---- 右侧：承認者/品名/図番/承認日 + 中间窄列日期 ----
    right_words = [w for w in band if w.cx >= REV_X_MAX]
    aux_dates, name_lines = [], []
    approval_date, found_zumen = None, False
    for r in cluster_rows(right_words):
        aux = [w for w in r if AUX_X_MIN <= w.cx < AUX_X_MAX]
        main = [w for w in r if w.cx >= MAIN_X_MIN]
        if aux:
            aux_dates.append(join_cell(aux))
        if not main:
            continue
        texts = [w.text for w in main]
        if zumen_no in texts:
            found_zumen = True
            continue
        hit = next((w for w in main if w.text.startswith("承認日")), None)
        if hit is not None:
            approval_date = hit.text
            for sep in ("：", ":"):
                if sep in approval_date:
                    approval_date = approval_date.split(sep, 1)[1]
                    break
            approval_date = approval_date.strip()
            continue
        name_lines.append(main)

    # 第一行（由多个短人名组成）= 承認者；其后依次为 品名行 / 図名行
    approvers = None
    if name_lines and len(name_lines[0]) >= 2 and all(
            len(w.text) <= 5 for w in name_lines[0]):
        first = sorted(name_lines.pop(0), key=lambda w: w.x0)
        approvers = ",".join(w.text for w in first)
    product_name = join_cell(name_lines[0]) if len(name_lines) >= 1 else None
    drawing_name = join_cell(name_lines[1]) if len(name_lines) >= 2 else None
    extras = [join_cell(line) for line in name_lines[2:]]

    rec = {
        "zumen_no": zumen_no,
        "product_name": product_name,
        "drawing_name": drawing_name,
        "approval_date": approval_date,
        "approvers": approvers,
        "aux_date": " / ".join(aux_dates) if aux_dates else None,
        "remarks": " | ".join(extras) if extras else None,
    }
    for i in range(MAX_REVS):
        rev = revs[i] if i < len(revs) else {}
        for k, _, _ in REV_ZONES:
            rec[f"rev{i + 1}_{k}"] = rev.get(k)
    if not found_zumen:
        log.append(f"タイトルブロック: 未找到図番「{zumen_no}」文字（図番以文件名为准）")
    return rec


# ---- 左侧图形区（图纸本体）文字的分类规则 ----
NOTE_NO_RE = re.compile(r"\d+\.")         # 注記编号（"2." 等）
BALLOON_RE = re.compile(r"\d{1,2}")       # 部品番号（バルーン内 1～2 位数字）
MARK_RE = re.compile(r"([A-Z])部")        # 部位記号（"A部" 等）
DIM_RE = re.compile(r"\d+\.\d+|\d{3,}")   # 寸法値（小数或 3 位以上整数）


def _classify_figure_word(w):
    """把左侧图形区的一个词分类，返回 (分类, 关联键)"""
    t = w.text
    if BALLOON_RE.fullmatch(t):
        return "部品番号", t            # → part_basic_info.seq_no
    m = MARK_RE.fullmatch(t)
    if m:
        return "部位記号", m.group(1)    # → proc_drawing_ref.item_mark
    if DIM_RE.fullmatch(t):
        return "寸法値", None
    return "部品名", None


def parse_figure_text(words, log):
    """S7 左侧图形区（x < LEFT_X_MAX）文字的分类整理 → figure_text

    分類：部品番号（バルーン数字）/ 部品名（図中ラベル）/ 部位記号（A部 等）/
          寸法値 / 注記（「注）」或 "1." "2." 编号开头的行 → 整行合并为一条）
    """
    left = [w for w in words if w.x0 < LEFT_X_MAX]
    records = []
    for r in cluster_rows(left):
        if any(w.text.startswith("注）") or NOTE_NO_RE.fullmatch(w.text) for w in r):
            # 注記行：整行合并为一条记录
            records.append({
                "category": "注記", "text": join_cell(r), "ref_key": None,
                "x0": min(w.x0 for w in r), "y0": min(w.y0 for w in r),
                "x1": max(w.x1 for w in r), "y1": max(w.y1 for w in r),
            })
            continue
        for w in r:
            cat, key = _classify_figure_word(w)
            records.append({
                "category": cat, "text": w.text, "ref_key": key,
                "x0": w.x0, "y0": w.y0, "x1": w.x1, "y1": w.y1,
            })
    # cluster_rows 已按 y 排行、行内按 x 排词 → 直接编号即阅读顺序
    for i, rec in enumerate(records):
        rec["seq"] = i + 1
    return records

SCHEMA = """
CREATE TABLE IF NOT EXISTS part_basic_info (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no    TEXT NOT NULL,   -- 図番号
    seq_no      INTEGER,         -- 番号
    part_name   TEXT,            -- 部品名
    frame_class TEXT,            -- 枠区分（三方枠/下枠）
    quantity    TEXT,            -- 員数（数字或 A～G 記号）
    base_dim    TEXT,            -- 基本寸法
    cut_dim     TEXT,            -- 切断寸法
    part_code   TEXT,            -- 部品コード
    remarks     TEXT
);
CREATE TABLE IF NOT EXISTS part_code_ref (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no   TEXT NOT NULL,   -- 図番号
    rail_type  TEXT,            -- 見込み区分（NC95/115、その他）
    part_code  TEXT,            -- 部品コード
    remarks    TEXT
);
CREATE TABLE IF NOT EXISTS frame_parts_set_detail (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no  TEXT NOT NULL,    -- 図番号
    type_name TEXT,             -- タイプ名（HH_…）
    mark_a TEXT, qty_a TEXT,    -- A / A員数
    mark_b TEXT, qty_b TEXT,    -- B / B員数
    mark_c TEXT, qty_c TEXT,    -- C / C員数
    mark_d TEXT, qty_d TEXT,    -- D / D員数
    mark_e TEXT, qty_e TEXT,    -- E / E員数
    mark_f TEXT, qty_f TEXT,    -- F / F員数
    mark_g TEXT, qty_g TEXT,    -- G / G員数
    remarks   TEXT
);
CREATE TABLE IF NOT EXISTS part_color_expansion (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no      TEXT NOT NULL,   -- 図番号
    ref_mark      TEXT,            -- 部品コード中的占位记号（□/△△/○○/□□）
    product_color TEXT,            -- 製品色
    part_color    TEXT,            -- 部品色
    remarks       TEXT
);
CREATE TABLE IF NOT EXISTS proc_drawing_ref (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no     TEXT NOT NULL,   -- 図番号
    item_mark    TEXT,            -- 項目記号（A,M/B/D/F/G/H/N）
    category     TEXT,            -- 区分（枠）
    drawing_name TEXT,            -- 要領図名
    ref_zumen_no TEXT,            -- 参照図番
    remarks      TEXT
);
CREATE TABLE IF NOT EXISTS title_block (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no       TEXT NOT NULL,   -- 図番号
    product_name   TEXT,            -- 品名（シリーズ+商品名）
    drawing_name   TEXT,            -- 図名
    approval_date  TEXT,            -- 承認日
    approvers      TEXT,            -- 承認者（逗号分隔）
    aux_date       TEXT,            -- 标题栏中间窄列的日期（如 16/09/23）
    rev1_no TEXT, rev1_sub TEXT, rev1_date TEXT, rev1_person TEXT, rev1_request_no TEXT, rev1_content TEXT,
    rev2_no TEXT, rev2_sub TEXT, rev2_date TEXT, rev2_person TEXT, rev2_request_no TEXT, rev2_content TEXT,
    rev3_no TEXT, rev3_sub TEXT, rev3_date TEXT, rev3_person TEXT, rev3_request_no TEXT, rev3_content TEXT,
    rev4_no TEXT, rev4_sub TEXT, rev4_date TEXT, rev4_person TEXT, rev4_request_no TEXT, rev4_content TEXT,
    rev5_no TEXT, rev5_sub TEXT, rev5_date TEXT, rev5_person TEXT, rev5_request_no TEXT, rev5_content TEXT,
    remarks        TEXT
);
CREATE TABLE IF NOT EXISTS figure_text (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no  TEXT NOT NULL,    -- 図番号
    seq       INTEGER,          -- 整理番号（図の上から下・左から右の阅读顺序）
    category  TEXT,             -- 分類（部品番号/部品名/部位記号/寸法値/注記）
    text      TEXT,             -- 文字内容
    ref_key   TEXT,             -- 关联键（部品番号→番号、部位記号→項目記号）
    x0 REAL, y0 REAL, x1 REAL, y1 REAL,   -- 文字坐标
    remarks   TEXT
);
CREATE TABLE IF NOT EXISTS table_order (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no     TEXT NOT NULL,   -- 図番号
    sort_no      INTEGER,         -- PDF 中自上而下的顺序（1 = 最上）
    table_name   TEXT,            -- 对应的数据表名
    section_name TEXT,            -- 图纸上的区域名称
    y_top        REAL,            -- 区域顶部 y 坐标
    y_bottom     REAL,            -- 区域底部 y 坐标（タイトルブロック为 NULL）
    remarks      TEXT
);
CREATE INDEX IF NOT EXISTS idx_pbi_zumen ON part_basic_info(zumen_no);
CREATE INDEX IF NOT EXISTS idx_pcr_zumen ON part_code_ref(zumen_no);
CREATE INDEX IF NOT EXISTS idx_fps_zumen ON frame_parts_set_detail(zumen_no);
CREATE INDEX IF NOT EXISTS idx_pce_zumen ON part_color_expansion(zumen_no);
CREATE INDEX IF NOT EXISTS idx_pdr_zumen ON proc_drawing_ref(zumen_no);
CREATE INDEX IF NOT EXISTS idx_tb_zumen  ON title_block(zumen_no);
CREATE INDEX IF NOT EXISTS idx_to_zumen  ON table_order(zumen_no);
CREATE INDEX IF NOT EXISTS idx_ft_zumen  ON figure_text(zumen_no);
"""

TABLE_ORDER = [
    "part_basic_info",
    "part_code_ref",
    "frame_parts_set_detail",
    "part_color_expansion",
    "proc_drawing_ref",
    "title_block",
    "figure_text",
    "table_order",
]


def default_db_path(xy_path):
    """默认数据库: <xy文件夹>\\..\\db\\zumen_data.sqlite"""
    d = os.path.dirname(os.path.abspath(xy_path))
    return os.path.abspath(os.path.join(d, os.pardir, "db", "zumen_data.sqlite"))

def main():
    ap = argparse.ArgumentParser(description="把 *_xy.txt 的表格数据登录到 SQLite")
    ap.add_argument("xy_file", help="PDF_SPLIT.py 输出的 <図番号>_xy.txt")
    ap.add_argument("--db", default=None,
                    help="SQLite 数据库路径（默认: xy 文件夹上一层的 db 子文件夹）")
    args = ap.parse_args()

    if not os.path.isfile(args.xy_file):
        print(f"ERROR: 找不到文件: {args.xy_file}")
        return 1

    stem = os.path.splitext(os.path.basename(args.xy_file))[0]
    zumen_no = stem[:-3] if stem.endswith("_xy") else stem
    db_path = args.db or default_db_path(args.xy_file)

    print(f"xy 文件 : {os.path.abspath(args.xy_file)}")
    print(f"図番号  : {zumen_no}")
    print(f"数据库  : {db_path}")
    print()

    log = []
    try:
        # ---- 1) 解析 ----
        all_words = parse_xy_file(args.xy_file)
        # 过滤坍缩成极小尺寸的异常文字（如右下角的尺寸标注）
        words, tiny = [], []
        for w in all_words:
            (tiny if (w.x1 - w.x0 < 1.0 and w.y1 - w.y0 < 1.0) else words).append(w)
        if tiny:
            print(f"[提示] 忽略 {len(tiny)} 个尺寸异常的极小文字: "
                  + ", ".join(w.text for w in tiny[:12])
                  + (" …" if len(tiny) > 12 else ""))
        print(f"第 1 页共 {len(all_words)} 个词（有效 {len(words)} 个）")

        right, s1, s2, s3, s4, s5 = locate_sections(words)
        print(f"分区区间: 基本信息[{s1:.0f},{s2:.0f}) コード参照[{s2:.0f},{s3:.0f}) "
              f"セット詳細[{s3:.0f},{s4:.0f}) 色展開[{s4:.0f},{s5:.0f}) 要領図[{s5:.0f},...]")

        # 要領図参照表区间的下界：取标题栏中図番文字上方 80（避开改訂履歴）
        zw = [w for w in words if w.text == zumen_no and w.x0 >= TITLE_X_MIN]
        if zw:
            s5_end = max(w.y0 for w in zw) - 80.0
        else:
            s5_end = s5 + 170.0
            log.append("标题栏中未找到図番文字，要領図参照表区间按默认长度截断")

        basic = parse_basic_info(right, s1, s2, log)
        code_ref = parse_code_ref(right, s2, s3, log)
        set_detail = parse_set_detail(right, s3, s4, log)
        color = parse_color_expansion(right, s4, s5, log)
        proc = parse_proc_ref(right, s5, s5_end, log)
        s5_zone = [w for w in band_words(words, s5, s5_end) if w.x0 >= TITLE_X_MIN]
        s6_top = max((w.y1 for w in s5_zone), default=s5_end) + 5.0
        title = parse_title_block(words, s6_top, zumen_no, log)

        # 六张表按 PDF 中自上而下的顺序记录（按区域顶部 y 排序后编号 1～6）
        # 要領図参照表的实际底部 = 区间内最下方文字的 y1
        #   （s5_end 只是用来避开改訂履歴的保守上界，不代表表格实际底边）
        proc_bottom = s6_top - 5.0
        # タイトルブロック的实际顶部 = 该区域内最上方文字的 y0
        tb_words = [w for w in words if w.y0 >= s6_top and w.x0 >= TITLE_X_MIN]
        title_top = min((w.y0 for w in tb_words), default=s6_top)
        sections = [
            ("part_basic_info", "部品基本信息表", s1, s2),
            ("part_code_ref", "部品コード参照表", s2, s3),
            ("frame_parts_set_detail", "枠用部品セット詳細表", s3, s4),
            ("part_color_expansion", "部品色展開表", s4, s5),
            ("proc_drawing_ref", "要領図参照表", s5, proc_bottom),
            ("title_block", "タイトルブロック", title_top, None),
        ]
        order = [
            {"sort_no": i + 1, "table_name": t, "section_name": n,
             "y_top": round(top, 1),
             "y_bottom": round(bot, 1) if bot is not None else None}
            for i, (t, n, top, bot) in enumerate(sorted(sections, key=lambda s: s[2]))
        ]
        figure = parse_figure_text(words, log)
    except Exception as e:
        print(f"ERROR: 解析失败: {e}")
        return 1

    data = {
        "part_basic_info": basic,
        "part_code_ref": code_ref,
        "frame_parts_set_detail": set_detail,
        "part_color_expansion": color,
        "proc_drawing_ref": proc,
        "title_block": [title],
        "figure_text": figure,
        "table_order": order,
    }

    # ---- 2) 登录数据库（同一図番号先删旧数据）----
    db_dir = os.path.dirname(db_path)
    if db_dir:
        os.makedirs(db_dir, exist_ok=True)
    conn = sqlite3.connect(db_path)
    try:
        conn.executescript(SCHEMA)
        cur = conn.cursor()
        for tname in TABLE_ORDER:
            recs = data[tname]
            cur.execute(f"DELETE FROM {tname} WHERE zumen_no = ?", (zumen_no,))
            if not recs:
                print(f"[警告] {tname}: 没有数据")
                continue
            keys = sorted({k for r in recs for k in r} - {"zumen_no"})
            cols = ", ".join(["zumen_no"] + keys)
            ph = ", ".join("?" * (len(keys) + 1))
            cur.executemany(
                f"INSERT INTO {tname} ({cols}) VALUES ({ph})",
                [(zumen_no, *(r.get(k) for k in keys)) for r in recs],
            )
        conn.commit()

        # ---- 3) 结果确认 ----
        print()
        print("==== 登录行数 ====")
        for tname in TABLE_ORDER:
            n = cur.execute(
                f"SELECT COUNT(*) FROM {tname} WHERE zumen_no = ?", (zumen_no,)
            ).fetchone()[0]
            print(f"  {tname:24s}: {n} 行")

        def dump(sql, title_str):
            print()
            print(f"---- {title_str} ----")
            cur.execute(sql, (zumen_no,))
            print(" | ".join(d[0] for d in cur.description))
            for row in cur.fetchall():
                print(" | ".join("" if v is None else str(v) for v in row))

        dump("SELECT seq_no, part_name, frame_class, quantity, base_dim, cut_dim, part_code"
             " FROM part_basic_info WHERE zumen_no=? ORDER BY seq_no",
             "part_basic_info（部品基本信息表）")
        dump("SELECT rail_type, part_code FROM part_code_ref WHERE zumen_no=? ORDER BY id",
             "part_code_ref（部品コード参照表）")
        dump("SELECT type_name, mark_a, qty_a, mark_b, qty_b, mark_c, qty_c,"
             " mark_d, qty_d, mark_e, qty_e, mark_f, qty_f, mark_g, qty_g"
             " FROM frame_parts_set_detail WHERE zumen_no=? ORDER BY id",
             "frame_parts_set_detail（枠用部品セット詳細表）")
        dump("SELECT ref_mark, product_color, part_color FROM part_color_expansion"
             " WHERE zumen_no=? ORDER BY id",
             "part_color_expansion（部品色展開表）")
        dump("SELECT item_mark, category, drawing_name, ref_zumen_no, remarks"
             " FROM proc_drawing_ref WHERE zumen_no=? ORDER BY id",
             "proc_drawing_ref（要領図参照表）")
        dump("SELECT sort_no, table_name, section_name, y_top, y_bottom"
             " FROM table_order WHERE zumen_no=? ORDER BY sort_no",
             "table_order（六表自上而下顺序）")
        dump("SELECT seq, category, text, ref_key, x0, y0 FROM figure_text"
             " WHERE zumen_no=? ORDER BY seq",
             "figure_text（図形部文字分类）")

        print()
        print("---- title_block（タイトルブロック）----")
        cur.execute("SELECT * FROM title_block WHERE zumen_no=? ORDER BY id", (zumen_no,))
        row = cur.fetchone()
        if row:
            for name, v in zip([d[0] for d in cur.description], row):
                if v not in (None, ""):
                    print(f"  {name} = {v}")
    finally:
        conn.close()

    if log:
        print()
        print("==== 处理日志 ====")
        for m in log:
            print("  [日志] " + m)

    print()
    print("SUCCESS")
    print(f"DB_PATH: {db_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
