# PROC_DRAWING_REF_API.py 使用说明

`proc_drawing_ref`（要領図参照表）数据登录接口 —— JSON CLI。
供 AI / 外部程序以命令行方式向 SQLite 登录（插入/更新/删除/查询）要領図参照数据。
接口模式与 `PART_BASIC_INFO_API.py` / `PART_CODE_REF_API.py` 完全一致。

## 表结构

| 字段 | 类型 | 必填 | 说明 |
|------|------|:----:|------|
| `id` | INTEGER | — | 自增主键（数据库自动赋值，请勿传入） |
| `zumen_no` | TEXT | ✔ | 図番号 |
| `item_mark` | TEXT | | 項目記号（A,M/B/D/F/G/H/N）；upsert 时与 zumen_no 组合匹配 |
| `category` | TEXT | | 区分（枠） |
| `drawing_name` | TEXT | | 要領図名 |
| `ref_zumen_no` | TEXT | | 参照図番 |
| `remarks` | TEXT | | 备注 |

数据示例（H310U10004 要領図参照表）：

| zumen_no | item_mark | category | drawing_name | ref_zumen_no |
|----------|-----------|----------|--------------|--------------|
| H310U10004 | A,M | 枠 | 縦枠/小縦枠スペーサー取付要領図 | H310U10022 |

## 命令一览

| 命令 | 说明 |
|------|------|
| `insert` | 登录数据（1 条 JSON 对象或多条对象数组均可） |
| `insert-many` | 批量登录（与 insert 等价，语义上更明确） |
| `upsert` | 按 `zumen_no + item_mark` 匹配：命中则更新，未命中则插入 |
| `update` | 按 `--id` 或 `--zumen [--item-mark]` 定位 1 条，部分字段更新 |
| `delete` | 按 `--id` 或 `--zumen [--item-mark]` 删除 |
| `query` | 查询：`--id` / `--zumen [--item-mark]` / `--all`（可选 `--limit`） |
| `count` | 统计行数（可按 `--zumen`） |
| `clear` | 清空：`--zumen` 清指定図番号；不指定清全表并重置自增 ID |
| `schema` | 输出表结构与字段说明（JSON） |

## 数据输入方式

`insert` / `insert-many` / `upsert` / `update` 三选一：

1. `--data '<JSON>'`：直接给 JSON 字符串
2. `--file <路径>`：从 UTF-8 JSON 文件读取（支持 BOM）
3. 都不指定时从标准输入 stdin 读取（管道）

## 主要选项

- `--db <路径>`：数据库文件路径（优先级: `--db` > 环境变量 `ZUMEN_DB` > 默认路径）
- `--replace`：仅 insert / insert-many 有效，同一図番号先删后插（幂等，可重复执行）
- `--id <主键ID>` / `--zumen <図番号>` / `--item-mark <項目記号>`：定位/过滤（同时指定时 `--id` 优先）

## 输出格式

- 成功（退出码 0）：`{"success": true, ...}`
- 失败（退出码 1）：`{"success": false, "error": "...", "detail": "..."}`

## 常用示例

```bat
cd /d ツール\PyMuPDF\WindowsFormsApp1

rem 1) 登录一条数据
python PROC_DRAWING_REF_API.py insert --data "{\"zumen_no\":\"H310U10004\",\"item_mark\":\"A,M\",\"category\":\"枠\",\"drawing_name\":\"縦枠/小縦枠スペーサー取付要領図\",\"ref_zumen_no\":\"H310U10022\"}"

rem 2) 登录 H310U10004 的参照表数据（幂等，可重复执行）
python PROC_DRAWING_REF_API.py insert-many --replace --data "[{\"zumen_no\":\"H310U10004\",\"item_mark\":\"A,M\",\"category\":\"枠\",\"drawing_name\":\"縦枠/小縦枠スペーサー取付要領図\",\"ref_zumen_no\":\"H310U10022\"},{\"zumen_no\":\"H310U10004\",\"item_mark\":\"B\",\"category\":\"枠\",\"drawing_name\":\"ロットNO.ラベル貼付要領図\"}]"

rem 3) 查询指定図番号的全部要領図参照数据
python PROC_DRAWING_REF_API.py query --zumen H310U10004

rem 4) 只查某項目記号的一条
python PROC_DRAWING_REF_API.py query --zumen H310U10004 --item-mark A,M

rem 5) 更新 id=5 的要領図名（部分字段更新）
python PROC_DRAWING_REF_API.py update --id 5 --data "{\"drawing_name\":\"更新後の要領図名\"}"

rem 6) upsert（命中 zumen_no+item_mark 则更新，否则插入）
python PROC_DRAWING_REF_API.py upsert --data "{\"zumen_no\":\"H310U10004\",\"item_mark\":\"A,M\",\"ref_zumen_no\":\"H310U10022\"}"

rem 7) 删除指定項目記号的一条
python PROC_DRAWING_REF_API.py delete --zumen H310U10004 --item-mark B

rem 8) 清空指定図番号 / 全表
python PROC_DRAWING_REF_API.py clear --zumen H310U10004
python PROC_DRAWING_REF_API.py clear

rem 9) 从 JSON 文件批量登录
python PROC_DRAWING_REF_API.py insert-many --file proc_ref.json --replace

rem 10) 管道输入
type proc_ref.json | python PROC_DRAWING_REF_API.py insert-many
```

## 注意事项

- `zumen_no` 必填（非空）；`item_mark`/`category`/`drawing_name`/`ref_zumen_no`/`remarks` 可空
- upsert 匹配键为 `zumen_no + item_mark`（`item_mark IS ?` NULL 安全比较）
- 字符串自动去首尾空白；空字符串按 NULL 处理
- 未知字段会报错（防止拼写错误静默丢数据）
- 全表 clear 会重置自增 ID（下次插入从 1 开始）
