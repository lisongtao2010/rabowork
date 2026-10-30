# TITLE_BLOCK_API.py 使用说明

`title_block`（タイトルブロック）数据登录接口 —— JSON CLI。
供 AI / 外部程序以命令行方式向 SQLite 登录（插入/更新/删除/查询）タイトルブロック数据。
接口模式与 `PART_BASIC_INFO_API.py` / `PART_CODE_REF_API.py` / `PROC_DRAWING_REF_API.py` 完全一致。

**特点：每図番号仅 1 行**（一张图纸一条记录），upsert 直接按 `zumen_no` 匹配，无第二匹配键。

## 表结构

| 字段 | 类型 | 必填 | 说明 |
|------|------|:----:|------|
| `id` | INTEGER | — | 自增主键（数据库自动赋值，请勿传入） |
| `zumen_no` | TEXT | ✔ | 図番号（upsert 匹配键） |
| `product_name` | TEXT | | 品名（シリーズ+商品名） |
| `drawing_name` | TEXT | | 図名 |
| `approval_date` | TEXT | | 承認日（如 2024/11/29） |
| `approvers` | TEXT | | 承認者（多个以逗号分隔） |
| `aux_date` | TEXT | | 中间窄列日期（如 16/09/23，多个以 ' / ' 连接） |
| `rev1_no` ~ `rev5_content` | TEXT | | 改訂履歴第 1~5 行（从新到旧），每行 6 字段：`no`(改訂番号) / `sub`(副番号) / `date`(改訂日) / `person`(承認者) / `request_no`(依頼番号) / `content`(改訂内容) |
| `remarks` | TEXT | | 备注 |

数据示例（H310U10004，节选）：

```json
{
  "zumen_no": "H310U10004",
  "product_name": "…シリーズ+商品名…",
  "drawing_name": "…図名…",
  "approval_date": "2024/11/29",
  "approvers": "…,…,…",
  "aux_date": "16/09/23",
  "rev1_no": "7", "rev1_sub": "-7", "rev1_date": "24/10/31",
  "rev1_person": "…", "rev1_request_no": "TJ-3086", "rev1_content": "色追加",
  "rev2_no": "6", "rev2_date": "22/12/16", "rev2_person": "…",
  "rev2_request_no": "TJ-2631", "rev2_content": "色追加"
}
```

（rev3~rev5 同理；不足 5 行的改訂履歴，多余 rev 字段不传即可，存 NULL）

## 命令一览

| 命令 | 说明 |
|------|------|
| `insert` | 登录数据（1 条 JSON 对象或多条对象数组均可） |
| `insert-many` | 批量登录（与 insert 等价） |
| `upsert` | 按 `zumen_no` 匹配：命中则更新，未命中则插入 |
| `update` | 按 `--id` 或 `--zumen` 定位 1 条，部分字段更新 |
| `delete` | 按 `--id` 或 `--zumen` 删除 |
| `query` | 查询：`--id` / `--zumen` / `--all`（可选 `--limit`） |
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
- `--id <主键ID>` / `--zumen <図番号>`：定位/过滤（同时指定时 `--id` 优先）

## 输出格式

- 成功（退出码 0）：`{"success": true, ...}`
- 失败（退出码 1）：`{"success": false, "error": "...", "detail": "..."}`

## 常用示例

```bat
cd /d ツール\PyMuPDF\WindowsFormsApp1

rem 1) 登录一张图纸的タイトルブロック（幂等，可重复执行）
python TITLE_BLOCK_API.py insert --replace --data "{\"zumen_no\":\"H310U10004\",\"product_name\":\"…\",\"drawing_name\":\"…\",\"approval_date\":\"2024/11/29\",\"approvers\":\"…\",\"aux_date\":\"16/09/23\",\"rev1_no\":\"7\",\"rev1_sub\":\"-7\",\"rev1_date\":\"24/10/31\",\"rev1_person\":\"…\",\"rev1_request_no\":\"TJ-3086\",\"rev1_content\":\"色追加\"}"

rem 2) 查询指定図番号
python TITLE_BLOCK_API.py query --zumen H310U10004

rem 3) 只更新承認日（部分字段更新）
python TITLE_BLOCK_API.py update --zumen H310U10004 --data "{\"approval_date\":\"2024/11/29\"}"

rem 4) upsert（命中 zumen_no 则更新，否则插入）
python TITLE_BLOCK_API.py upsert --data "{\"zumen_no\":\"H310U10004\",\"approval_date\":\"2024/11/29\"}"

rem 5) 删除指定図番号的一条
python TITLE_BLOCK_API.py delete --zumen H310U10004

rem 6) 清空指定図番号 / 全表
python TITLE_BLOCK_API.py clear --zumen H310U10004
python TITLE_BLOCK_API.py clear

rem 7) 从 JSON 文件批量登录
python TITLE_BLOCK_API.py insert-many --file title_blocks.json --replace

rem 8) 管道输入
type title_blocks.json | python TITLE_BLOCK_API.py insert-many
```

## 注意事项

- `zumen_no` 必填（非空）；每図番号仅 1 行，重复登录请用 `insert --replace` 或 `upsert`
- upsert 匹配键为 `zumen_no`（本表无第二匹配键）
- 改訂履歴从新到旧对应 rev1 → rev5，最多 5 行；不足的行不传字段即可
- 字符串自动去首尾空白；空字符串按 NULL 处理
- 未知字段会报错（防止拼写错误静默丢数据）
- 全表 clear 会重置自增 ID（下次插入从 1 开始）
