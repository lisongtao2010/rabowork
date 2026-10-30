# -*- coding: utf-8 -*-
"""
proc_drawing_ref 表数据登录接口（JSON CLI 接口）

为 AI / 外部程序提供向 SQLite 数据库 proc_drawing_ref（要領図参照表）
登录（插入/更新/删除/查询）数据的命令行接口。
所有输入输出均为 JSON 格式，便于程序化调用与结果解析。
接口模式与 PART_BASIC_INFO_API.py / PART_CODE_REF_API.py 完全一致。

表结构（proc_drawing_ref）:
    id           INTEGER PRIMARY KEY AUTOINCREMENT   自增主键（自动赋值）
    zumen_no     TEXT NOT NULL                       図番号（必填）
    item_mark    TEXT                                項目記号（A,M/B/D/F/G/H/N）
    category     TEXT                                区分（枠）
    drawing_name TEXT                                要領図名
    ref_zumen_no TEXT                                参照図番
    remarks      TEXT                                备注

用法:
    python PROC_DRAWING_REF_API.py <命令> [选项]

命令:
    insert       登录数据（1 条 JSON 对象或多条对象数组均可）
    insert-many  批量登录（与 insert 等价，语义上更明确）
    upsert       登录或更新（按 zumen_no + item_mark 匹配，命中则更新，否则插入）
    update       更新已有记录（--id 或 --zumen [--item-mark] 定位，部分字段更新）
    delete       删除记录（--id 或 --zumen [--item-mark]）
    query        查询记录（--id / --zumen [--item-mark] / --all，可选 --limit）
    count        统计行数（可按 --zumen 统计）
    clear        清空数据（--zumen 清指定図番号；不指定则清全表并重置自增ID）
    schema       显示表结构与字段说明（JSON）

数据输入方式（insert / insert-many / upsert / update）:
    --data '<JSON>'    直接指定 JSON 字符串
    --file <路径>      从 JSON 文件读取（UTF-8）
    （两者都未指定时，从标准输入 stdin 读取 JSON）

主要选项:
    --db <路径>        数据库文件路径（默认: 本脚本目录\\..\\..\\図面\\db\\zumen_data.sqlite，
                       也可用环境变量 ZUMEN_DB 指定）
    --replace          （仅 insert / insert-many）登录前先删除同一図番号的旧数据
    --zumen <図番号>    按 zumen_no 过滤/定位
    --item-mark <記号>  按項目記号 item_mark 过滤/定位（与 --zumen 同用）
    --id <主键ID>      按 id 定位

输出（stdout，JSON）:
    成功: {"success": true, ...}                                  退出码 0
    失败: {"success": false, "error": "...", "detail": "..."}      退出码 1

示例:
    rem 1) 登录一条数据
    python PROC_DRAWING_REF_API.py insert --data "{\"zumen_no\":\"H310U10004\",\"item_mark\":\"A,M\",\"category\":\"枠\",\"drawing_name\":\"縦枠/小縦枠スペーサー取付要領図\",\"ref_zumen_no\":\"H310U10022\"}"

    rem 2) 从 JSON 文件批量登录（同一図番号先删旧数据，可重复执行）
    python PROC_DRAWING_REF_API.py insert-many --file proc_ref.json --replace

    rem 3) 管道输入
    type proc_ref.json | python PROC_DRAWING_REF_API.py insert-many

    rem 4) 更新 id=5 的记录的参照図番
    python PROC_DRAWING_REF_API.py update --id 5 --data "{\"ref_zumen_no\":\"H310U10022\"}"

    rem 5) 查询図番号 H310U10004 的全部数据
    python PROC_DRAWING_REF_API.py query --zumen H310U10004

    rem 6) 清空全表
    python PROC_DRAWING_REF_API.py clear
"""

import argparse
import json
import os
import sqlite3
import sys

# 保证被外部程序 / AI 调用（重定向输出）时不乱码
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

# ------------------------------------------------------------------
# 常量
# ------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# 默认数据库: <本脚本目录>\..\..\図面\db\zumen_data.sqlite
# （即 ツール\図面\db\zumen_data.sqlite，与 XY_TO_DB.py 的默认库一致）
DEFAULT_DB = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "図面", "db", "zumen_data.sqlite"))

TABLE = "proc_drawing_ref"

# 除 id（自增主键）外可写入的字段
# item_mark 是 upsert 的匹配键（与 zumen_no 组合），其余为普通文本字段
MATCH_KEY = "item_mark"                    # 項目記号
TEXT_FIELDS = ["category", "drawing_name", "ref_zumen_no", "remarks"]
WRITABLE_FIELDS = ["zumen_no", MATCH_KEY] + TEXT_FIELDS

# 各字段说明（schema 命令输出用）
FIELD_COMMENTS = {
    "id": "自增主键（由数据库自动赋值，请勿指定）",
    "zumen_no": "図番号（必填，非空）",
    "item_mark": "項目記号（A,M/B/D/F/G/H/N）；upsert 时与 zumen_no 组合匹配",
    "category": "区分（枠）",
    "drawing_name": "要領図名",
    "ref_zumen_no": "参照図番",
    "remarks": "备注",
}

# 与 XY_TO_DB.py 保持一致的建表语句（库/表不存在时自动创建）
SCHEMA = """
CREATE TABLE IF NOT EXISTS proc_drawing_ref (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no     TEXT NOT NULL,   -- 図番号
    item_mark    TEXT,            -- 項目記号（A,M/B/D/F/G/H/N）
    category     TEXT,            -- 区分（枠）
    drawing_name TEXT,            -- 要領図名
    ref_zumen_no TEXT,            -- 参照図番
    remarks      TEXT
);
CREATE INDEX IF NOT EXISTS idx_pdr_zumen ON proc_drawing_ref(zumen_no);
"""

# ------------------------------------------------------------------
# 辅助函数
# ------------------------------------------------------------------

def get_db_path(args):
    """数据库路径优先级: --db > 环境变量 ZUMEN_DB > 默认路径"""
    if getattr(args, "db", None):
        return args.db
    env = os.environ.get("ZUMEN_DB")
    if env:
        return env
    return DEFAULT_DB


def connect_db(db_path):
    """连接数据库并确保表存在。路径不存在时自动创建（含父目录）。"""
    parent = os.path.dirname(os.path.abspath(db_path))
    if parent and not os.path.isdir(parent):
        os.makedirs(parent, exist_ok=True)
    conn = sqlite3.connect(db_path)
    conn.execute("PRAGMA foreign_keys = ON")
    conn.executescript(SCHEMA)
    conn.commit()
    return conn


def fail(msg, detail=None, code=1):
    """输出错误 JSON 到 stdout 并以非零退出码结束"""
    out = {"success": False, "error": msg}
    if detail is not None:
        out["detail"] = detail
    print(json.dumps(out, ensure_ascii=False, indent=2))
    sys.exit(code)


def ok(payload):
    """输出成功 JSON 到 stdout 并正常退出"""
    out = {"success": True}
    out.update(payload)
    print(json.dumps(out, ensure_ascii=False, indent=2))
    sys.exit(0)


def read_json_arg(args):
    """读取 JSON 数据: --data > --file > stdin。允许 UTF-8 BOM。"""
    raw = None
    source = None
    if getattr(args, "data", None):
        raw = args.data
        source = "--data"
    elif getattr(args, "file", None):
        try:
            with open(args.file, "r", encoding="utf-8-sig") as f:
                raw = f.read()
        except OSError as e:
            fail(f"无法读取 JSON 文件: {args.file}", repr(e))
        source = "--file"
    else:
        raw = sys.stdin.read()
        source = "stdin"
    if raw is None or not str(raw).strip():
        fail(f"JSON 数据为空（来源: {source}）")
    try:
        return json.loads(raw)
    except json.JSONDecodeError as e:
        fail(f"JSON 解析失败（来源: {source}）", f"位置 {e.lineno} 行 {e.colno} 列: {e.msg}")


def to_records(obj):
    """把输入 JSON 规范化为记录列表：dict → [dict]，list → 原样"""
    if isinstance(obj, dict):
        return [obj]
    if isinstance(obj, list) and all(isinstance(x, dict) for x in obj):
        return obj
    fail("JSON 数据必须是对象或对象数组", f"实际类型: {type(obj).__name__}")


def norm_value(field, value):
    """字段值规范化: 字符串去首尾空白；空白字符串视为 NULL"""
    if isinstance(value, str):
        value = value.strip()
        if value == "":
            return None
    return value


def extract_fields(record, *, allow_id=False):
    """从记录 dict 中抽取合法字段，未知字段报错"""
    unknown = [k for k in record if k not in WRITABLE_FIELDS and k != "id"]
    if unknown:
        fail("包含未知字段: " + ", ".join(map(str, sorted(map(str, unknown)))),
             f"合法字段: {', '.join(WRITABLE_FIELDS)}")
    out = {}
    for field in WRITABLE_FIELDS:
        if field in record:
            out[field] = norm_value(field, record[field])
    if allow_id and "id" in record:
        iid = record["id"]
        if not isinstance(iid, int) or isinstance(iid, bool):
            fail("id 必须是整数")
        out["id"] = iid
    return out


def require_zumen(fields):
    """校验必填字段 zumen_no"""
    zumen = fields.get("zumen_no")
    if zumen is None:
        fail("zumen_no 为必填字段（不能为空）")
    return zumen


def row_to_dict(row):
    """数据库行 → dict（键顺序 = 表定义顺序）"""
    keys = ["id", "zumen_no", MATCH_KEY] + TEXT_FIELDS
    return {k: row[i] for i, k in enumerate(keys)}


# ------------------------------------------------------------------
# 命令实现
# ------------------------------------------------------------------

def cmd_insert(args, conn):
    """insert / insert-many: 登录数据（--replace 时先删同一図番号旧数据）"""
    records = to_records(read_json_arg(args))
    parsed = [extract_fields(r) for r in records]
    for fields in parsed:
        require_zumen(fields)

    cur = conn.cursor()
    replaced = 0
    if getattr(args, "replace", False):
        zumens = sorted({f["zumen_no"] for f in parsed})
        for zumen in zumens:
            cur.execute(f"DELETE FROM {TABLE} WHERE zumen_no = ?", (zumen,))
            replaced += cur.rowcount

    ids = []
    for fields in parsed:
        cols = list(fields.keys())
        if not cols:
            fail("记录为空对象（没有可写入的字段）")
        placeholders = ", ".join("?" for _ in cols)
        sql = (f"INSERT INTO {TABLE} ({', '.join(cols)}) "
               f"VALUES ({placeholders})")
        cur.execute(sql, [fields[c] for c in cols])
        ids.append(cur.lastrowid)

    conn.commit()
    total = cur.execute(f"SELECT COUNT(*) FROM {TABLE}").fetchone()[0]
    ok({
        "command": args.command,
        "inserted": len(ids),
        "ids": ids,
        "replaced_deleted": replaced,
        "total_rows": total,
        "db": os.path.abspath(get_db_path(args)),
    })


def cmd_upsert(args, conn):
    """upsert: 按 zumen_no + item_mark 匹配，命中则更新，否则插入"""
    record = read_json_arg(args)
    if isinstance(record, list):
        fail("upsert 只接受单个 JSON 对象（批量请用 insert-many --replace）")
    fields = extract_fields(record)
    zumen = require_zumen(fields)
    item_mark = fields.get(MATCH_KEY)

    cur = conn.cursor()
    sql = (f"SELECT id FROM {TABLE} "
           f"WHERE zumen_no = ? AND {MATCH_KEY} IS ? LIMIT 1")
    row = cur.execute(sql, (zumen, item_mark)).fetchone()

    updates = {k: v for k, v in fields.items()
               if k not in ("zumen_no", MATCH_KEY)}
    if row is not None:
        target_id = row[0]
        if updates:
            sets = ", ".join(f"{k} = ?" for k in updates)
            cur.execute(f"UPDATE {TABLE} SET {sets} WHERE id = ?",
                        list(updates.values()) + [target_id])
        conn.commit()
        ok({"command": "upsert", "action": "updated", "id": target_id,
            "db": os.path.abspath(get_db_path(args))})
    else:
        cols = list(fields.keys())
        placeholders = ", ".join("?" for _ in cols)
        cur.execute(f"INSERT INTO {TABLE} ({', '.join(cols)}) "
                    f"VALUES ({placeholders})",
                    [fields[c] for c in cols])
        conn.commit()
        ok({"command": "upsert", "action": "inserted",
            "id": cur.lastrowid,
            "db": os.path.abspath(get_db_path(args))})


def _locate(args, cur):
    """按 --id / --zumen [--item-mark] 生成 WHERE 子句与参数"""
    if getattr(args, "id", None) is not None:
        return f"id = ?", [args.id]
    if getattr(args, "zumen", None):
        conds, params = ["zumen_no = ?"], [args.zumen]
        mark = getattr(args, "item_mark", None)
        if mark is not None:
            conds.append(f"{MATCH_KEY} IS ?")
            params.append(mark)
        return " AND ".join(conds), params
    fail("请指定定位条件: --id <主键ID> 或 --zumen <図番号> [--item-mark <項目記号>]")


def cmd_update(args, conn):
    """update: 定位 1 条记录并部分字段更新"""
    record = read_json_arg(args)
    if isinstance(record, list):
        fail("update 只接受单个 JSON 对象")
    fields = extract_fields(record)
    if not fields:
        fail("更新内容为空（没有可更新的字段）")

    cur = conn.cursor()
    where, params = _locate(args, cur)
    row = cur.execute(f"SELECT id FROM {TABLE} WHERE {where}", params).fetchone()
    if row is None:
        fail("未找到匹配的记录", f"条件: {where} {params}")

    sets = ", ".join(f"{k} = ?" for k in fields)
    cur.execute(f"UPDATE {TABLE} SET {sets} WHERE {where}",
                list(fields.values()) + params)
    conn.commit()
    ok({"command": "update", "updated": cur.rowcount, "id": row[0],
        "db": os.path.abspath(get_db_path(args))})


def cmd_delete(args, conn):
    """delete: 删除匹配的记录"""
    cur = conn.cursor()
    where, params = _locate(args, cur)
    cur.execute(f"DELETE FROM {TABLE} WHERE {where}", params)
    deleted = cur.rowcount
    conn.commit()
    total = cur.execute(f"SELECT COUNT(*) FROM {TABLE}").fetchone()[0]
    ok({"command": "delete", "deleted": deleted, "total_rows": total,
        "db": os.path.abspath(get_db_path(args))})


def cmd_query(args, conn):
    """query: 查询记录"""
    cur = conn.cursor()
    order = "ORDER BY zumen_no, id"
    if getattr(args, "id", None) is not None:
        rows = cur.execute(
            f"SELECT * FROM {TABLE} WHERE id = ?", (args.id,)).fetchall()
    elif getattr(args, "all", False):
        sql = f"SELECT * FROM {TABLE} {order}"
        limit = getattr(args, "limit", None)
        if limit is not None:
            sql += f" LIMIT {int(limit)}"
        rows = cur.execute(sql).fetchall()
    elif getattr(args, "zumen", None):
        conds, params = ["zumen_no = ?"], [args.zumen]
        mark = getattr(args, "item_mark", None)
        if mark is not None:
            conds.append(f"{MATCH_KEY} IS ?")
            params.append(mark)
        rows = cur.execute(
            f"SELECT * FROM {TABLE} WHERE {' AND '.join(conds)} {order}",
            params).fetchall()
    else:
        fail("请指定查询条件: --id <主键ID> / --zumen <図番号> [--item-mark <項目記号>] / --all")

    ok({"command": "query", "count": len(rows),
        "rows": [row_to_dict(r) for r in rows],
        "db": os.path.abspath(get_db_path(args))})


def cmd_count(args, conn):
    """count: 统计行数"""
    cur = conn.cursor()
    if getattr(args, "zumen", None):
        n = cur.execute(f"SELECT COUNT(*) FROM {TABLE} WHERE zumen_no = ?",
                        (args.zumen,)).fetchone()[0]
    else:
        n = cur.execute(f"SELECT COUNT(*) FROM {TABLE}").fetchone()[0]
    ok({"command": "count", "count": n,
        "db": os.path.abspath(get_db_path(args))})


def cmd_clear(args, conn):
    """clear: 清空数据。--zumen 只清指定図番号；否则清全表并重置自增ID"""
    cur = conn.cursor()
    if getattr(args, "zumen", None):
        cur.execute(f"DELETE FROM {TABLE} WHERE zumen_no = ?", (args.zumen,))
        deleted = cur.rowcount
        conn.commit()
    else:
        cur.execute(f"DELETE FROM {TABLE}")
        deleted = cur.rowcount          # 必须在删除 sqlite_sequence 之前读取!
        try:
            cur.execute("DELETE FROM sqlite_sequence WHERE name = ?", (TABLE,))
        except sqlite3.Error:
            pass                        # sqlite_sequence 不存在时忽略
        conn.commit()
        cur.execute("VACUUM")
        conn.commit()
    total = cur.execute(f"SELECT COUNT(*) FROM {TABLE}").fetchone()[0]
    ok({"command": "clear", "deleted": deleted, "total_rows": total,
        "db": os.path.abspath(get_db_path(args))})


def cmd_schema(args, conn):
    """schema: 输出表结构与字段说明"""
    cur = conn.cursor()
    cols = []
    for row in cur.execute(f"PRAGMA table_info({TABLE})"):
        # row: (cid, name, type, notnull, dflt_value, pk)
        cols.append({
            "name": row[1],
            "type": row[2],
            "notnull": bool(row[3]),
            "primary_key": bool(row[5]),
            "comment": FIELD_COMMENTS.get(row[1], ""),
        })
    ok({"command": "schema", "table": TABLE,
        "columns": cols, "match_key": ["zumen_no", MATCH_KEY],
        "db": os.path.abspath(get_db_path(args))})


# ------------------------------------------------------------------
# 命令行解析与入口
# ------------------------------------------------------------------

COMMANDS = {
    "insert": cmd_insert,
    "insert-many": cmd_insert,
    "upsert": cmd_upsert,
    "update": cmd_update,
    "delete": cmd_delete,
    "query": cmd_query,
    "count": cmd_count,
    "clear": cmd_clear,
    "schema": cmd_schema,
}

EPILOG = """\
示例:
  python PROC_DRAWING_REF_API.py insert --data "{\\"zumen_no\\":\\"H310U10004\\",\\"item_mark\\":\\"A,M\\",\\"category\\":\\"枠\\",\\"drawing_name\\":\\"縦枠/小縦枠スペーサー取付要領図\\",\\"ref_zumen_no\\":\\"H310U10022\\"}"
  python PROC_DRAWING_REF_API.py insert-many --file proc_ref.json --replace
  python PROC_DRAWING_REF_API.py upsert --data "{\\"zumen_no\\":\\"H310U10004\\",\\"item_mark\\":\\"A,M\\",\\"ref_zumen_no\\":\\"H310U10022\\"}"
  python PROC_DRAWING_REF_API.py update --id 5 --data "{\\"remarks\\":\\"注記\\"}"
  python PROC_DRAWING_REF_API.py query --zumen H310U10004
  python PROC_DRAWING_REF_API.py query --all --limit 100
  python PROC_DRAWING_REF_API.py delete --zumen H310U10004 --item-mark B
  python PROC_DRAWING_REF_API.py clear
"""

DATA_CMDS = {"insert", "insert-many", "upsert", "update"}
NEED_DB = set(COMMANDS)   # 所有命令都需要连接数据库


def _add_common_data_opts(p):
    p.add_argument("--data", help="JSON 数据字符串（对象或对象数组）")
    p.add_argument("--file", help="JSON 文件路径（UTF-8，支持 BOM）")


def _add_locate_opts(p):
    p.add_argument("--id", type=int, help="按主键 id 定位")
    p.add_argument("--zumen", help="按図番号 zumen_no 过滤/定位")
    p.add_argument("--item-mark", dest="item_mark",
                   help="按項目記号 item_mark 过滤/定位（与 --zumen 同用）")


def build_parser():
    parser = argparse.ArgumentParser(
        prog="PROC_DRAWING_REF_API.py",
        description="proc_drawing_ref（要領図参照表）数据登录接口 —— JSON CLI",
        epilog=EPILOG,
        formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("insert", help="登录数据（1 条或多条）",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    _add_common_data_opts(p)
    p.add_argument("--replace", action="store_true",
                   help="登录前先删除同一図番号的旧数据（幂等）")

    p = sub.add_parser("insert-many", help="批量登录（与 insert 等价）",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    _add_common_data_opts(p)
    p.add_argument("--replace", action="store_true",
                   help="登录前先删除同一図番号的旧数据（幂等）")

    p = sub.add_parser("upsert", help="登录或更新（zumen_no+item_mark 匹配）",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    _add_common_data_opts(p)

    p = sub.add_parser("update", help="更新已有记录（部分字段）",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    _add_common_data_opts(p)
    _add_locate_opts(p)

    p = sub.add_parser("delete", help="删除记录",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    _add_locate_opts(p)

    p = sub.add_parser("query", help="查询记录",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    _add_locate_opts(p)
    p.add_argument("--all", action="store_true", help="查询全部记录")
    p.add_argument("--limit", type=int, help="最多返回条数（仅 --all）")

    p = sub.add_parser("count", help="统计行数",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--zumen", help="按図番号 zumen_no 统计")

    p = sub.add_parser("clear", help="清空数据",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--zumen", help="只清指定図番号（不指定则清全表并重置自增ID）")

    p = sub.add_parser("schema", help="显示表结构与字段说明",
                       epilog=EPILOG,
                       formatter_class=argparse.RawDescriptionHelpFormatter)

    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command not in COMMANDS:
        fail(f"未知命令: {args.command}")
    if args.command in DATA_CMDS and not (args.data or args.file):
        if sys.stdin.isatty():
            fail("请通过 --data、--file 或管道 stdin 提供 JSON 数据")

    db_path = get_db_path(args)
    conn = connect_db(db_path)
    try:
        COMMANDS[args.command](args, conn)
    except SystemExit:
        raise
    except sqlite3.Error as e:
        fail("数据库操作失败", repr(e))
    except Exception as e:                       # noqa: BLE001
        fail(f"未预期的错误: {type(e).__name__}", repr(e))
    finally:
        conn.close()


if __name__ == "__main__":
    main()




