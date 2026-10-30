# -*- coding: utf-8 -*-
"""
part_code_ref 表数据登录接口（JSON CLI 接口）

为 AI / 外部程序提供向 SQLite 数据库 part_code_ref（部品コード参照表）
登录（插入/更新/删除/查询）数据的命令行接口。
所有输入输出均为 JSON 格式，便于程序化调用与结果解析。
接口模式与 PART_BASIC_INFO_API.py 完全一致。

表结构（part_code_ref）:
    id         INTEGER PRIMARY KEY AUTOINCREMENT   自增主键（自动赋值）
    zumen_no   TEXT NOT NULL                       図番号（必填）
    rail_type  TEXT                                見込み区分（NC95/115、その他）
    part_code  TEXT                                部品コード
    remarks    TEXT                                备注

用法:
    python PART_CODE_REF_API.py <命令> [选项]

命令:
    insert       登录数据（1 条 JSON 对象或多条对象数组均可）
    insert-many  批量登录（与 insert 等价，语义上更明确）
    upsert       登录或更新（按 zumen_no + rail_type 匹配，命中则更新，否则插入）
    update       更新已有记录（--id 或 --zumen [--rail-type] 定位，部分字段更新）
    delete       删除记录（--id 或 --zumen [--rail-type]）
    query        查询记录（--id / --zumen [--rail-type] / --all，可选 --limit）
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
    --rail-type <区分>  按見込み区分 rail_type 过滤/定位（与 --zumen 同用）
    --id <主键ID>      按 id 定位

输出（stdout，JSON）:
    成功: {"success": true, ...}                                  退出码 0
    失败: {"success": false, "error": "...", "detail": "..."}      退出码 1

示例:
    rem 1) 登录一条数据
    python PART_CODE_REF_API.py insert --data "{\"zumen_no\":\"H310U10004\",\"rail_type\":\"NC95/115\",\"part_code\":\"MZA□968\"}"

    rem 2) 从 JSON 文件批量登录（同一図番号先删旧数据，可重复执行）
    python PART_CODE_REF_API.py insert-many --file code_ref.json --replace

    rem 3) 管道输入
    type code_ref.json | python PART_CODE_REF_API.py insert-many

    rem 4) 更新 id=5 的记录的部品コード
    python PART_CODE_REF_API.py update --id 5 --data "{\"part_code\":\"MZA674\"}"

    rem 5) 查询図番号 H310U10004 的全部数据
    python PART_CODE_REF_API.py query --zumen H310U10004

    rem 6) 清空全表
    python PART_CODE_REF_API.py clear
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

TABLE = "part_code_ref"

# 除 id（自增主键）外可写入的字段
# rail_type 是 upsert 的匹配键（与 zumen_no 组合），其余为普通文本字段
MATCH_KEY = "rail_type"                     # 見込み区分
TEXT_FIELDS = ["part_code", "remarks"]
WRITABLE_FIELDS = ["zumen_no", MATCH_KEY] + TEXT_FIELDS

# 各字段说明（schema 命令输出用）
FIELD_COMMENTS = {
    "id": "自增主键（由数据库自动赋值，请勿指定）",
    "zumen_no": "図番号（必填，非空）",
    "rail_type": "見込み区分（NC95/115、その他）；upsert 时与 zumen_no 组合匹配",
    "part_code": "部品コード",
    "remarks": "备注",
}

# 与 XY_TO_DB.py 保持一致的建表语句（库/表不存在时自动创建）
SCHEMA = """
CREATE TABLE IF NOT EXISTS part_code_ref (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    zumen_no   TEXT NOT NULL,   -- 図番号
    rail_type  TEXT,            -- 見込み区分（NC95/115、その他）
    part_code  TEXT,            -- 部品コード
    remarks    TEXT
);
CREATE INDEX IF NOT EXISTS idx_pcr_zumen ON part_code_ref(zumen_no);
"""

# ------------------------------------------------------------------
# 基础工具
# ------------------------------------------------------------------

class ApiError(Exception):
    """接口业务错误（会以 JSON 形式返回给调用方）"""

    def __init__(self, message, detail=None):
        super().__init__(message)
        self.message = message
        self.detail = detail


def output(payload):
    """把结果以 JSON 输出到 stdout，并返回退出码（0=成功 / 1=失败）"""
    print(json.dumps(payload, ensure_ascii=False, indent=2))
    return 0 if payload.get("success") else 1


def open_db(db_path):
    """打开数据库（不存在则连同目录一起创建），并确保表存在"""
    db_path = os.path.abspath(db_path)
    db_dir = os.path.dirname(db_path)
    if db_dir:
        os.makedirs(db_dir, exist_ok=True)
    conn = sqlite3.connect(db_path)
    conn.executescript(SCHEMA)
    return conn


def read_payload(args):
    """读取 JSON 输入（优先 --data，其次 --file，最后 stdin）"""
    if getattr(args, "data", None) is not None:
        raw, src = args.data, "--data"
    elif getattr(args, "file", None) is not None:
        try:
            # utf-8-sig: 兼容带 BOM 的 UTF-8 文件（Excel/记事本保存的 JSON）
            with open(args.file, encoding="utf-8-sig") as f:
                raw, src = f.read(), args.file
        except OSError as e:
            raise ApiError("无法读取输入文件", f"{args.file}: {e}")
    else:
        raw, src = sys.stdin.read(), "stdin"
    try:
        return json.loads(raw)
    except json.JSONDecodeError as e:
        raise ApiError(f"JSON 解析失败（来源: {src}）", str(e))


def to_records(payload, what="输入数据"):
    """把 JSON 载荷统一为记录列表: 对象 → [对象]；对象数组 → 原样"""
    if isinstance(payload, dict):
        return [dict(payload)]
    if isinstance(payload, list):
        if payload and all(isinstance(r, dict) for r in payload):
            return [dict(r) for r in payload]
        if not payload:
            return []
        raise ApiError(f"{what}格式错误", "JSON 数组中存在非对象元素")
    raise ApiError(f"{what}格式错误", "应为 JSON 对象或对象数组")


def _as_text(v, field):
    """文本字段值规范化: None → None；标量 → 字符串"""
    if v is None:
        return None
    if isinstance(v, str):
        return v
    if isinstance(v, bool):
        return "TRUE" if v else "FALSE"
    if isinstance(v, (int, float)):
        return str(v)
    raise ApiError(
        f"字段 {field} 的值类型不支持: {type(v).__name__}",
        "请使用字符串或 null",
    )


def normalize_record(rec, require_zumen=True):
    """
    校验并规范化 1 条记录。
    - 拒绝未知字段（错误信息中列出全部可写字段，便于 AI 调用方自我纠正）
    - zumen_no:  必填时非空；自动 strip
    - rail_type: 匹配键；自动 strip，空白/空串 → None（NULL）
    - part_code / remarks: 普通文本字段，原样保留
    返回: 只含「本次输入里出现」的可写字段的 dict
    """
    unknown = sorted(k for k in rec if k not in WRITABLE_FIELDS)
    if unknown:
        detail = "可写字段: " + ", ".join(WRITABLE_FIELDS)
        if "id" in unknown:
            detail += "（id 为自增主键，由数据库自动赋值，请勿指定）"
        raise ApiError("存在无法识别的字段: " + ", ".join(unknown), detail)

    out = {}

    # ---- zumen_no（図番号，必填字段）----
    if "zumen_no" in rec:
        v = rec["zumen_no"]
        if v is not None:
            if not isinstance(v, str):
                v = str(v)
            v = v.strip()
        if v:
            out["zumen_no"] = v
        elif require_zumen:
            raise ApiError("zumen_no（図番号）不能为空")
    if require_zumen and "zumen_no" not in out:
        raise ApiError(
            "缺少必填字段 zumen_no（図番号）",
            "可写字段: " + ", ".join(WRITABLE_FIELDS),
        )

    # ---- rail_type（見込み区分，匹配键）----
    if MATCH_KEY in rec:
        v = rec[MATCH_KEY]
        if v is None:
            out[MATCH_KEY] = None
        elif isinstance(v, str):
            v = v.strip()
            out[MATCH_KEY] = v if v else None
        else:
            out[MATCH_KEY] = _as_text(v, MATCH_KEY)

    # ---- 普通文本字段 ----
    for k in TEXT_FIELDS:
        if k in rec:
            out[k] = _as_text(rec[k], k)
    return out

# ------------------------------------------------------------------
# 命令实现
# ------------------------------------------------------------------

def _count(cur, zumen=None):
    """统计行数（可按図番号）"""
    if zumen is None:
        return cur.execute(f"SELECT COUNT(*) FROM {TABLE}").fetchone()[0]
    return cur.execute(
        f"SELECT COUNT(*) FROM {TABLE} WHERE zumen_no = ?", (zumen,)
    ).fetchone()[0]


def _locate(args):
    """按 --id 或 --zumen [--rail-type] 生成定位条件，返回 (where_sql, params)"""
    if args.id is not None:
        return "id = ?", [args.id]
    if args.zumen:
        if getattr(args, "rail_type", None) is not None:
            # IS 用于 NULL 安全比较（rail_type 为 NULL 的记录也能匹配）
            return "zumen_no = ? AND rail_type IS ?", [args.zumen, args.rail_type]
        return "zumen_no = ?", [args.zumen]
    raise ApiError(
        "必须指定定位条件",
        "--id <主键ID> 或 --zumen <図番号> [--rail-type <見込み区分>] 中的任意一个",
    )


def cmd_insert(conn, args):
    """insert / insert-many: 登录数据（--replace 时同一図番号先删旧数据）"""
    recs = [normalize_record(r) for r in to_records(read_payload(args))]
    if not recs:
        raise ApiError("没有可登录的数据", "请提供至少 1 条 JSON 记录")

    cur = conn.cursor()
    replaced = 0
    if getattr(args, "replace", False):
        for z in sorted({r["zumen_no"] for r in recs}):
            cur.execute(f"DELETE FROM {TABLE} WHERE zumen_no = ?", (z,))
            replaced += cur.rowcount

    ids = []
    for r in recs:
        cols = list(r.keys())
        cur.execute(
            f"INSERT INTO {TABLE} ({', '.join(cols)})"
            f" VALUES ({', '.join('?' * len(cols))})",
            [r[c] for c in cols],
        )
        ids.append(cur.lastrowid)
    conn.commit()

    payload = {
        "success": True,
        "command": args.command,
        "inserted": len(ids),
        "ids": ids,
        "total_rows": _count(cur),
    }
    if getattr(args, "replace", False):
        payload["replaced_deleted"] = replaced
    return payload


def cmd_upsert(conn, args):
    """
    upsert: 按 zumen_no + rail_type 匹配
    - 命中 → 更新匹配记录的全部指定字段
    - 未命中 → 插入新记录
    （rail_type 省略或为空时按 NULL 匹配/写入）
    """
    recs = [normalize_record(r) for r in to_records(read_payload(args))]
    if not recs:
        raise ApiError("没有可登录的数据", "请提供至少 1 条 JSON 记录")

    cur = conn.cursor()
    inserted, updated, new_ids = 0, 0, []
    for r in recs:
        cur.execute(
            f"SELECT id FROM {TABLE} WHERE zumen_no = ? AND rail_type IS ?",
            (r["zumen_no"], r.get(MATCH_KEY)),
        )
        match_ids = [row[0] for row in cur.fetchall()]
        if match_ids:
            cols = list(r.keys())
            set_sql = ", ".join(f"{c} = ?" for c in cols)
            for mid in match_ids:
                cur.execute(
                    f"UPDATE {TABLE} SET {set_sql} WHERE id = ?",
                    [r[c] for c in cols] + [mid],
                )
                updated += cur.rowcount
        else:
            cols = list(r.keys())
            cur.execute(
                f"INSERT INTO {TABLE} ({', '.join(cols)})"
                f" VALUES ({', '.join('?' * len(cols))})",
                [r[c] for c in cols],
            )
            inserted += 1
            new_ids.append(cur.lastrowid)
    conn.commit()
    return {
        "success": True,
        "command": "upsert",
        "inserted": inserted,
        "updated": updated,
        "ids": new_ids,
        "total_rows": _count(cur),
    }


def cmd_update(conn, args):
    """update: 定位已有记录并做部分字段更新（只更新指定了的字段）"""
    payload = read_payload(args)
    if not isinstance(payload, dict):
        raise ApiError("update 的输入必须是 JSON 对象", '例: {"part_code": "MZA674"}')
    fields = normalize_record(payload, require_zumen=False)
    if not fields:
        raise ApiError("没有可更新的字段", "请提供至少 1 个字段（如 part_code）")

    where, params = _locate(args)
    cur = conn.cursor()
    matched = cur.execute(
        f"SELECT COUNT(*) FROM {TABLE} WHERE {where}", params
    ).fetchone()[0]
    if matched == 0:
        raise ApiError("未找到匹配的记录", f"定位条件: {where} {params}")

    set_sql = ", ".join(f"{k} = ?" for k in fields)
    cur.execute(
        f"UPDATE {TABLE} SET {set_sql} WHERE {where}",
        [fields[k] for k in fields] + params,
    )
    conn.commit()
    return {
        "success": True,
        "command": "update",
        "matched": matched,
        "updated": cur.rowcount,
    }

def cmd_delete(conn, args):
    """delete: 删除匹配的记录"""
    where, params = _locate(args)
    cur = conn.cursor()
    cur.execute(f"DELETE FROM {TABLE} WHERE {where}", params)
    deleted = cur.rowcount
    conn.commit()
    return {
        "success": True,
        "command": "delete",
        "deleted": deleted,
        "remaining": _count(cur),
    }


def cmd_query(conn, args):
    """query: 查询记录（结果按 zumen_no, id 排序）"""
    where, params = None, []
    if args.id is not None:
        where, params = "id = ?", [args.id]
    elif args.zumen:
        if getattr(args, "rail_type", None) is not None:
            where, params = ("zumen_no = ? AND rail_type IS ?",
                             [args.zumen, args.rail_type])
        else:
            where, params = "zumen_no = ?", [args.zumen]
    elif not args.all:
        raise ApiError(
            "必须指定查询条件",
            "--id <主键ID> / --zumen <図番号> [--rail-type <見込み区分>] / --all 中的任意一个",
        )

    sql = f"SELECT * FROM {TABLE}"
    if where:
        sql += f" WHERE {where}"
    sql += " ORDER BY zumen_no, id"
    if args.limit is not None:
        sql += f" LIMIT {int(args.limit)}"

    cur = conn.cursor()
    cur.execute(sql, params)
    cols = [d[0] for d in cur.description]
    rows = [dict(zip(cols, row)) for row in cur.fetchall()]
    return {
        "success": True,
        "command": "query",
        "count": len(rows),
        "rows": rows,
    }


def cmd_count(conn, args):
    """count: 统计行数（--zumen 指定図番号时只统计该図番号）"""
    cur = conn.cursor()
    payload = {
        "success": True,
        "command": "count",
        "count": _count(cur, args.zumen),
    }
    if args.zumen:
        payload["zumen_no"] = args.zumen
    return payload


def cmd_clear(conn, args):
    """clear: 清空数据（--zumen 清指定図番号；不指定则清全表并重置自增ID）"""
    cur = conn.cursor()
    if args.zumen:
        cur.execute(f"DELETE FROM {TABLE} WHERE zumen_no = ?", (args.zumen,))
        deleted = cur.rowcount        # 须在下一条 SQL 执行前读取，否则会被覆盖
        scope = args.zumen
    else:
        cur.execute(f"DELETE FROM {TABLE}")
        deleted = cur.rowcount        # 须在重置自增ID前读取，否则会被覆盖
        # 重置自增 ID 计数器（表已清空，下次插入从 1 开始）
        try:
            cur.execute("DELETE FROM sqlite_sequence WHERE name = ?", (TABLE,))
        except sqlite3.Error:
            pass        # sqlite_sequence 表不存在时忽略
        scope = "ALL"
    conn.commit()
    return {
        "success": True,
        "command": "clear",
        "scope": scope,
        "deleted": deleted,
        "remaining": _count(cur),
    }


def cmd_schema(conn, args):
    """schema: 输出表结构说明（便于 AI 调用方自查字段）"""
    cur = conn.cursor()
    create_sql = cur.execute(
        "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?",
        (TABLE,),
    ).fetchone()[0]
    cur.execute(f"PRAGMA table_info({TABLE})")
    fields = [
        {
            "name": r[1],
            "type": r[2],
            "notnull": bool(r[3]),
            "primary_key": bool(r[5]),
            "comment": FIELD_COMMENTS.get(r[1], ""),
        }
        for r in cur.fetchall()
    ]
    return {
        "success": True,
        "command": "schema",
        "table": TABLE,
        "fields": fields,
        "writable_fields": WRITABLE_FIELDS,
        "required_fields": ["zumen_no"],
        "match_key": ["zumen_no", MATCH_KEY],
        "create_sql": create_sql,
    }


# 命令名 → 处理函数（insert 与 insert-many 共用同一实现）
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

# ------------------------------------------------------------------
# 命令行入口
# ------------------------------------------------------------------

def _add_db_opt(p):
    # default=SUPPRESS: 子命令未指定 --db 时，保留主解析器解析到的值
    p.add_argument("--db", default=argparse.SUPPRESS,
                   help=f"数据库文件路径（默认: {DEFAULT_DB}，或环境变量 ZUMEN_DB）")


def _add_input_opts(p):
    p.add_argument("--data", help="JSON 字符串（对象或对象数组）")
    p.add_argument("--file", help="JSON 文件路径（UTF-8）")


def _add_locate_opts(p, all_flag=False):
    p.add_argument("--id", type=int, help="按主键 id 定位")
    p.add_argument("--zumen", help="按図番号 zumen_no 定位/过滤")
    p.add_argument("--rail-type", dest="rail_type",
                   help="按見込み区分 rail_type 定位/过滤（与 --zumen 同用）")
    if all_flag:
        p.add_argument("--all", action="store_true", help="查询全部记录")


def build_parser():
    parser = argparse.ArgumentParser(
        prog="PART_CODE_REF_API.py",
        description="part_code_ref（部品コード参照表）数据登录接口。"
                    "JSON 输入 / JSON 输出，供 AI 或外部程序调用。",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "示例:\n"
            "  python PART_CODE_REF_API.py schema\n"
            "  python PART_CODE_REF_API.py insert "
            "--data '{\"zumen_no\":\"H310U10004\",\"rail_type\":\"NC95/115\",\"part_code\":\"MZA□968\"}'\n"
            "  python PART_CODE_REF_API.py insert-many --file code_ref.json --replace\n"
            "  python PART_CODE_REF_API.py query --zumen H310U10004\n"
            "  python PART_CODE_REF_API.py update --id 5 --data '{\"part_code\":\"MZA674\"}'\n"
            "  python PART_CODE_REF_API.py delete --zumen H310U10004 --rail-type その他\n"
            "  python PART_CODE_REF_API.py clear\n"
        ),
    )
    parser.add_argument("--db", default=None,
                        help=f"数据库文件路径（默认: {DEFAULT_DB}，或环境变量 ZUMEN_DB）")
    sub = parser.add_subparsers(dest="command", metavar="命令")

    p = sub.add_parser("insert", help="登录数据（对象或对象数组）")
    _add_input_opts(p)
    p.add_argument("--replace", action="store_true",
                   help="同一図番号已有数据时先删除再登录")
    _add_db_opt(p)

    p = sub.add_parser("insert-many", help="批量登录（与 insert 等价）")
    _add_input_opts(p)
    p.add_argument("--replace", action="store_true",
                   help="同一図番号已有数据时先删除再登录")
    _add_db_opt(p)

    p = sub.add_parser("upsert", help="登录或更新（按 zumen_no+rail_type 匹配）")
    _add_input_opts(p)
    _add_db_opt(p)

    p = sub.add_parser("update", help="更新已有记录（部分字段）")
    _add_input_opts(p)
    _add_locate_opts(p)
    _add_db_opt(p)

    p = sub.add_parser("delete", help="删除记录")
    _add_locate_opts(p)
    _add_db_opt(p)

    p = sub.add_parser("query", help="查询记录")
    _add_locate_opts(p, all_flag=True)
    p.add_argument("--limit", type=int, help="最多返回的行数")
    _add_db_opt(p)

    p = sub.add_parser("count", help="统计行数")
    p.add_argument("--zumen", help="只统计指定図番号")
    _add_db_opt(p)

    p = sub.add_parser("clear", help="清空数据（不指定 --zumen 则清全表）")
    p.add_argument("--zumen", help="只清空指定図番号的数据")
    _add_db_opt(p)

    p = sub.add_parser("schema", help="显示表结构（JSON）")
    _add_db_opt(p)

    return parser


def main():
    parser = build_parser()
    args = parser.parse_args()

    if not args.command:
        parser.print_help(sys.stderr)
        return output({
            "success": False,
            "error": "未指定命令",
            "detail": "用法: python PART_CODE_REF_API.py <命令> [选项]；"
                      "可用命令: " + ", ".join(COMMANDS),
        })

    # 数据库路径优先级: --db 参数 > 环境变量 ZUMEN_DB > 默认路径
    db_path = getattr(args, "db", None) or os.environ.get("ZUMEN_DB") or DEFAULT_DB

    try:
        conn = open_db(db_path)
    except (sqlite3.Error, OSError) as e:
        return output({
            "success": False,
            "error": "数据库打开失败",
            "detail": str(e),
            "db": os.path.abspath(db_path),
        })

    try:
        payload = COMMANDS[args.command](conn, args)
        payload.setdefault("db", os.path.abspath(db_path))
        return output(payload)
    except ApiError as e:
        conn.rollback()
        return output({"success": False, "error": e.message, "detail": e.detail})
    except sqlite3.Error as e:
        conn.rollback()
        return output({"success": False, "error": "数据库操作失败", "detail": str(e)})
    finally:
        conn.close()


if __name__ == "__main__":
    sys.exit(main())

