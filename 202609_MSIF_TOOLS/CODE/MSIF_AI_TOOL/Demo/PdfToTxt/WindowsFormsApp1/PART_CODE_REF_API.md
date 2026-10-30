# PART_CODE_REF_API.py 使用说明

`part_code_ref`（部品コード参照表）数据登录接口。AI / 外部程序通过命令行调用，
**JSON 输入 / JSON 输出**，便于程序化调用与结果解析。接口模式与 `PART_BASIC_INFO_API.py` 完全一致。

- 脚本位置: `ツール\PyMuPDF\WindowsFormsApp1\PART_CODE_REF_API.py`
- 目标数据库（默认）: `ツール\図面\db\zumen_data.sqlite`（与 XY_TO_DB.py 同库）
- 目标表: `part_code_ref`（库/表不存在时自动创建，结构与 XY_TO_DB.py 一致）

## 表结构

| 字段 | 类型 | 必填 | 说明 |
|------|------|:---:|------|
| `id` | INTEGER | — | 自增主键（数据库自动赋值，请勿传入） |
| `zumen_no` | TEXT | ✔ | 図番号 |
| `rail_type` | TEXT | | 見込み区分（NC95/115、その他）；upsert 时与 zumen_no 组合匹配 |
| `part_code` | TEXT | | 部品コード |
| `remarks` | TEXT | | 备注 |

数据示例（H310U10004 的「下表参照」展开来源）:

| zumen_no | rail_type | part_code |
|----------|-----------|-----------|
| H310U10004 | NC95/115 | MZA□968 |
| H310U10004 | その他 | MZA674 |

## 用法

```
python PART_CODE_REF_API.py <命令> [选项]
```

| 命令 | 说明 |
|------|------|
| `insert` | 登录数据（1 条 JSON 对象或多条对象数组均可）；`--replace` 时同一図番号先删旧数据 |
| `insert-many` | 批量登录（与 insert 等价） |
| `upsert` | 登录或更新（按 zumen_no + rail_type 匹配，命中则更新，否则插入） |
| `update` | 更新已有记录（`--id` 或 `--zumen [--rail-type]` 定位，部分字段更新） |
| `delete` | 删除记录（`--id` 或 `--zumen [--rail-type]`） |
| `query` | 查询记录（`--id` / `--zumen [--rail-type]` / `--all`，可选 `--limit`） |
| `count` | 统计行数（可按 `--zumen`） |
| `clear` | 清空数据（`--zumen` 清指定図番号；不指定则清全表并重置自增 ID） |
| `schema` | 显示表结构与字段说明（JSON） |

### 输入方式（insert / insert-many / upsert / update）

- `--data '<JSON>'`：直接指定 JSON 字符串
- `--file <路径>`：从 JSON 文件读取（UTF-8，兼容 BOM）
- 都不指定时从标准输入 stdin 读取

### 主要选项

- `--db <路径>`：数据库文件路径（优先级: `--db` > 环境变量 `ZUMEN_DB` > 默认路径）
- `--replace`：仅 insert / insert-many 有效，同一図番号先删后插（幂等，可重复执行）
- `--id <主键ID>` / `--zumen <図番号>` / `--rail-type <見込み区分>`：定位/过滤（同时指定时 `--id` 优先）

## 输出格式

stdout 输出 JSON，退出码 0 = 成功、1 = 失败（便于调用方判断）:

```
成功: {"success": true, ...}
失败: {"success": false, "error": "...", "detail": "..."}
```

## 使用示例

```bat
rem 1) 确认表结构
python PART_CODE_REF_API.py schema

rem 2) 登录 H310U10004 的参照表数据（幂等，可重复执行）
python PART_CODE_REF_API.py insert-many --replace --data "[{\"zumen_no\":\"H310U10004\",\"rail_type\":\"NC95/115\",\"part_code\":\"MZA□968\"},{\"zumen_no\":\"H310U10004\",\"rail_type\":\"その他\",\"part_code\":\"MZA674\"}]"

rem 3) 查询指定図番号的全部参照数据
python PART_CODE_REF_API.py query --zumen H310U10004

rem 4) 只查某見込み区分的一条
python PART_CODE_REF_API.py query --zumen H310U10004 --rail-type "NC95/115"

rem 5) 更新 id=5 的部品コード（部分字段更新）
python PART_CODE_REF_API.py update --id 5 --data "{\"part_code\":\"MZA674\"}"

rem 6) upsert（命中 zumen_no+rail_type 则更新，否则插入）
python PART_CODE_REF_API.py upsert --data "{\"zumen_no\":\"H310U10004\",\"rail_type\":\"NC95/115\",\"part_code\":\"MZA□968\"}"

rem 7) 删除指定見込み区分的一条
python PART_CODE_REF_API.py delete --zumen H310U10004 --rail-type その他

rem 8) 统计
python PART_CODE_REF_API.py count
python PART_CODE_REF_API.py count --zumen H310U10004

rem 9) 清空全表（重置自增 ID） / 只清某図番号
python PART_CODE_REF_API.py clear
python PART_CODE_REF_API.py clear --zumen H310U10004
```

## 注意事项

1. **必填字段**：`zumen_no`（図番号）在 insert / insert-many / upsert 时必填且不能为空；
   update 不要求（只更新给出的字段）。
2. **未知字段**：输入中出现表外字段（含 `id`）会报错并在 detail 中列出可写字段，便于 AI 自我纠正。
3. **`--replace` 与 `upsert` 的区别**：
   - `--replace` 按**図番号**整批替换（先删该図番号全部数据再插入，条数以本次输入为准）；
   - `upsert` 按 **zumen_no + rail_type** 逐条匹配，命中更新、未命中插入。
4. **rail_type 的 NULL 语义**：insert/upsert 时 rail_type 省略、为空串或 null 均按 NULL 处理；
   upsert 匹配时 NULL 只匹配 NULL（SQL 用 `IS` 比较，NULL 安全）。
5. **与 PART_BASIC_INFO_API.py 的配合**：`part_basic_info` 中部品コード为「下表参照」的记录，
   可通过 `query --zumen <図番号>` 从本表获取应展开的部品コード列表。
6. 本接口不做 □□/△△/○○ → □ 的替换等业务加工，登录内容以输入为准。
